using System.Globalization;
using System.Text.Json.Nodes;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI.UnrealTypes;
using UEBPR.Models;

namespace UEBPR.Services;

public sealed class AssetWritebackService
{
    private readonly NodeLibraryService nodeLibrary;

    public AssetWritebackService(NodeLibraryService nodeLibrary)
    {
        this.nodeLibrary = nodeLibrary;
    }

    public ApplyGraphResultDto ApplyGraph(AssetSession session, int exportIndex, GraphDocumentDto graph)
    {
        var export = GetStructExport(session.Asset, exportIndex);
        var result = new ApplyGraphResultDto { ExportIndex = exportIndex };
        var previous = export.ScriptBytecode ?? [];
        var compiled = new List<KismetExpression>();

        foreach (var node in graph.Nodes.Where(IsTopLevelNode).OrderBy(GetNodeOrder))
        {
            var expression = TryGetOriginalExpression(previous, node, out var original)
                ? UpdateOriginalExpression(session.Asset, original, node, result)
                : CompileNewExpression(session.Asset, node, result);

            compiled.Add(expression);
        }

        RecalculateOffsets(session.Asset, compiled, result);
        export.ScriptBytecode = compiled.ToArray();
        export.ScriptBytecodeRaw = [];
        export.ScriptBytecodeSize = 0;
        result.CompiledExpressionCount = compiled.Count;
        return result;
    }

    public SaveResultDto Save(AssetSession session)
    {
        var backupPath = session.AssetPath + ".bak";
        File.Copy(session.AssetPath, backupPath, overwrite: true);
        if (session.UexpPath is not null && File.Exists(session.UexpPath))
        {
            File.Copy(session.UexpPath, session.UexpPath + ".bak", overwrite: true);
        }

        session.Asset.Write(session.AssetPath);

        return new SaveResultDto
        {
            OutputPath = session.AssetPath,
            BackupPath = backupPath,
            Warnings = session.Warnings.ToList()
        };
    }

    private KismetExpression UpdateOriginalExpression(UAsset asset, KismetExpression original, GraphNodeDto node, ApplyGraphResultDto result)
    {
        switch (original)
        {
            case EX_IntConst intConst when TryGetInt(node.Payload, "value", out var intValue):
                intConst.Value = intValue;
                break;
            case EX_FloatConst floatConst when TryGetDouble(node.Payload, "value", out var floatValue):
                floatConst.Value = (float)floatValue;
                break;
            case EX_DoubleConst doubleConst when TryGetDouble(node.Payload, "value", out var doubleValue):
                doubleConst.Value = doubleValue;
                break;
            case EX_StringConst stringConst when TryGetString(node.Payload, "value", out var stringValue):
                stringConst.Value = stringValue;
                break;
            case EX_NameConst nameConst when TryGetString(node.Payload, "value", out var nameValue):
                nameConst.Value = new FName(asset, nameValue);
                break;
            case EX_Context context:
                ResolveContextOwner(asset, context, node, result);
                break;
        }

        return original;
    }

    private KismetExpression CompileNewExpression(UAsset asset, GraphNodeDto node, ApplyGraphResultDto result)
    {
        var expressionType = node.ExpressionType.Trim();
        try
        {
            return expressionType switch
            {
                "EX_IntConst" or "IntConst" => new EX_IntConst { Value = GetInt(node.Payload, "value") },
                "EX_FloatConst" or "FloatConst" => new EX_FloatConst { Value = (float)GetDouble(node.Payload, "value") },
                "EX_DoubleConst" or "DoubleConst" => new EX_DoubleConst { Value = GetDouble(node.Payload, "value") },
                "EX_StringConst" or "StringConst" => new EX_StringConst { Value = GetString(node.Payload, "value") },
                "EX_NameConst" or "NameConst" => new EX_NameConst { Value = new FName(asset, GetString(node.Payload, "value")) },
                "EX_True" or "True" => new EX_True(),
                "EX_False" or "False" => new EX_False(),
                "EX_Self" or "Self" => new EX_Self(),
                "EX_Return" or "Return" => new EX_Return { ReturnExpression = new EX_Nothing() },
                "EX_Jump" or "Jump" => new EX_Jump { CodeOffset = (uint)Math.Max(0, GetInt(node.Payload, "codeOffset")) },
                "EX_JumpIfNot" or "JumpIfNot" => new EX_JumpIfNot { CodeOffset = (uint)Math.Max(0, GetInt(node.Payload, "codeOffset")), BooleanExpression = new EX_False() },
                "EX_VirtualFunction" or "VirtualFunction" => new EX_VirtualFunction
                {
                    VirtualFunctionName = new FName(asset, node.FunctionName),
                    Parameters = []
                },
                _ => Unsupported(node, result)
            };
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Node {node.Id} could not be compiled: {ex.Message}");
            return new EX_Nothing();
        }
    }

    private static KismetExpression Unsupported(GraphNodeDto node, ApplyGraphResultDto result)
    {
        result.Warnings.Add($"New node {node.Id} uses unsupported expression type '{node.ExpressionType}' and was compiled as Nothing.");
        return new EX_Nothing();
    }

    private void ResolveContextOwner(UAsset asset, EX_Context context, GraphNodeDto node, ApplyGraphResultDto result)
    {
        var parameterSignature = node.Payload["parameterSignature"]?.GetValue<string>() ?? string.Empty;
        var templateKey = node.Payload["templateKey"]?.GetValue<string>()
            ?? BlueprintGraphService.BuildTemplateKey(node.OwnerKey, node.FunctionName, parameterSignature);

        var resolvedOwner = -99;
        if (nodeLibrary.TryFind(templateKey, out var template) && template.ResolvedOwnerIndex != 0 && template.ResolvedOwnerIndex != -99)
        {
            resolvedOwner = template.ResolvedOwnerIndex;
        }
        else
        {
            result.UnresolvedNodeIds.Add(node.Id);
            result.Warnings.Add($"Node {node.Id} could not resolve owner '{node.OwnerKey}'. ResolvedOwner was set to -99.");
        }

        if (context.RValuePointer?.New is not null)
        {
            context.RValuePointer.New.ResolvedOwner = FPackageIndex.FromRawIndex(resolvedOwner);
        }
        else if (context.RValuePointer?.Old is not null)
        {
            context.RValuePointer.Old = FPackageIndex.FromRawIndex(resolvedOwner);
        }
    }

    private static void RecalculateOffsets(UAsset asset, IReadOnlyList<KismetExpression> expressions, ApplyGraphResultDto result)
    {
        uint offset = 0;
        foreach (var expression in expressions)
        {
            if (expression is EX_Context context)
            {
                context.Offset = context.ContextExpression?.GetSize(asset) ?? 0;
            }
            else if (expression is EX_Skip skip)
            {
                skip.CodeOffset = skip.SkipExpression?.GetSize(asset) ?? skip.CodeOffset;
            }
            else if (expression is EX_SkipOffsetConst skipOffset)
            {
                skipOffset.Value = offset;
            }

            expression.Visit(asset, ref offset, static (_, _) => { });
        }

        result.Warnings.Add("Offsets were recalculated for context/skip expressions. Explicit jump target editing is preserved from payload until label-based jumps are added.");
    }

    private static bool IsTopLevelNode(GraphNodeDto node)
    {
        if (node.Id.StartsWith("new-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!node.Id.StartsWith("expr-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return int.TryParse(node.Id.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static int GetNodeOrder(GraphNodeDto node)
    {
        if (node.Id.StartsWith("expr-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(node.Id.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
        {
            return order;
        }

        return 10_000 + (int)Math.Max(0, node.Position.Y) * 10 + (int)Math.Max(0, node.Position.X);
    }

    private static bool TryGetOriginalExpression(IReadOnlyList<KismetExpression> previous, GraphNodeDto node, out KismetExpression expression)
    {
        expression = null!;
        if (!node.Id.StartsWith("expr-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = node.Id[5..];
        var separator = suffix.IndexOf('-', StringComparison.Ordinal);
        if (separator >= 0)
        {
            suffix = suffix[..separator];
        }

        if (!int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            || index < 0
            || index >= previous.Count)
        {
            return false;
        }

        expression = previous[index];
        return true;
    }

    private static StructExport GetStructExport(UAsset asset, int exportIndex)
    {
        if (exportIndex <= 0 || exportIndex > asset.Exports.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(exportIndex), "Export index is outside the asset export map.");
        }

        return asset.Exports[exportIndex - 1] as StructExport
            ?? throw new InvalidOperationException("The selected export is not a StructExport and cannot contain script bytecode.");
    }

    private static bool TryGetInt(JsonObject payload, string key, out int value)
    {
        value = 0;
        return payload.TryGetPropertyValue(key, out var node) && node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static int GetInt(JsonObject payload, string key) => TryGetInt(payload, key, out var value) ? value : 0;

    private static bool TryGetDouble(JsonObject payload, string key, out double value)
    {
        value = 0;
        return payload.TryGetPropertyValue(key, out var node) && node is not null && double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static double GetDouble(JsonObject payload, string key) => TryGetDouble(payload, key, out var value) ? value : 0;

    private static bool TryGetString(JsonObject payload, string key, out string value)
    {
        value = string.Empty;
        if (!payload.TryGetPropertyValue(key, out var node) || node is null)
        {
            return false;
        }

        value = node.GetValue<string>();
        return true;
    }

    private static string GetString(JsonObject payload, string key) => TryGetString(payload, key, out var value) ? value : string.Empty;
}
