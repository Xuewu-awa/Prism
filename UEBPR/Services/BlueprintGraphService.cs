using System.Text.Json.Nodes;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI.UnrealTypes;
using UEBPR.Models;

namespace UEBPR.Services;

public sealed class BlueprintGraphService
{
    private readonly NodeLibraryService nodeLibrary;

    public BlueprintGraphService(NodeLibraryService nodeLibrary)
    {
        this.nodeLibrary = nodeLibrary;
    }

    public AssetOpenResultDto DescribeSession(AssetSession session)
    {
        var exports = session.Asset.Exports
            .Select((export, index) =>
            {
                var structExport = export as StructExport;
                return new ExportSummaryDto(
                    index + 1,
                    export.ObjectName?.ToString() ?? $"Export {index + 1}",
                    export.GetType().Name,
                    structExport?.ScriptBytecode is { Length: > 0 },
                    structExport?.ScriptBytecode?.Length ?? 0,
                    structExport?.LoadedProperties?.Length ?? 0);
            })
            .ToList();

        return new AssetOpenResultDto(
            session.Id,
            Path.GetFileName(session.AssetPath),
            session.AssetPath,
            session.Mappings is not null,
            exports,
            session.Warnings);
    }

    public GraphDocumentDto BuildGraph(AssetSession session, int exportIndex)
    {
        var export = GetStructExport(session.Asset, exportIndex);
        var graph = new GraphDocumentDto
        {
            SessionId = session.Id,
            ExportIndex = exportIndex,
            AssetName = Path.GetFileName(session.AssetPath),
            ExportName = export.ObjectName?.ToString() ?? $"Export {exportIndex}",
            ExportType = export.GetType().Name
        };

        if (export.ScriptBytecode is null)
        {
            graph.Warnings.Add("This export did not parse script bytecode. Raw bytecode is preserved until it is successfully parsed.");
            return graph;
        }

        for (var i = 0; i < export.ScriptBytecode.Length; i++)
        {
            AddExpressionNode(session.Asset, graph, export, export.ScriptBytecode[i], $"expr-{i}", i, null, null);
        }

        graph.Metadata["loadedPropertyCount"] = export.LoadedProperties?.Length ?? 0;
        graph.Metadata["scriptExpressionCount"] = export.ScriptBytecode.Length;
        return graph;
    }

    private GraphNodeDto AddExpressionNode(
        UAsset asset,
        GraphDocumentDto graph,
        StructExport ownerExport,
        KismetExpression expression,
        string id,
        int sequence,
        GraphNodeDto? parent,
        string? parentPinName)
    {
        var node = CreateNode(asset, ownerExport, expression, id, sequence);
        graph.Nodes.Add(node);

        if (parent is not null)
        {
            var fromPin = parent.Pins.FirstOrDefault(pin => pin.Name == parentPinName && pin.Direction == "Output")
                ?? AddPin(parent, parentPinName ?? "Child", "Output", "Exec");
            var toPin = node.Pins.First(pin => pin.Direction == "Input");
            fromPin.LinkedTo.Add(toPin.Id);
            toPin.LinkedTo.Add(fromPin.Id);
            graph.Connections.Add(new GraphConnectionDto(parent.Id, fromPin.Id, node.Id, toPin.Id));
        }

        foreach (var child in GetChildren(expression))
        {
            AddExpressionNode(asset, graph, ownerExport, child.Expression, $"{id}-{child.Name}-{child.Index}", sequence + child.Index + 1, node, child.Name);
        }

        if (expression is EX_Context context)
        {
            UpsertContextTemplate(asset, ownerExport, context, node);
        }

        return node;
    }

    private GraphNodeDto CreateNode(UAsset asset, StructExport ownerExport, KismetExpression expression, string id, int sequence)
    {
        var node = new GraphNodeDto
        {
            Id = id,
            Kind = expression is EX_Context ? "Context" : "Expression",
            Title = expression.Inst,
            ExpressionType = expression.GetType().Name,
            Position = new GraphPositionDto
            {
                X = 80 + (sequence % 4) * 320,
                Y = 80 + (sequence / 4) * 180
            }
        };

        AddPin(node, "In", "Input", "Exec");
        AddPin(node, "Out", "Output", "Exec");

        node.Payload["token"] = expression.Token.ToString();
        node.Payload["rawKind"] = expression.GetType().FullName;

        switch (expression)
        {
            case EX_IntConst intConst:
                node.Title = $"Int {intConst.Value}";
                node.Payload["value"] = intConst.Value;
                AddPin(node, "Value", "Output", "Int");
                break;
            case EX_FloatConst floatConst:
                node.Title = $"Float {floatConst.Value}";
                node.Payload["value"] = floatConst.Value;
                AddPin(node, "Value", "Output", "Float");
                break;
            case EX_DoubleConst doubleConst:
                node.Title = $"Double {doubleConst.Value}";
                node.Payload["value"] = doubleConst.Value;
                AddPin(node, "Value", "Output", "Double");
                break;
            case EX_StringConst stringConst:
                node.Title = "String";
                node.Payload["value"] = stringConst.Value;
                AddPin(node, "Value", "Output", "String");
                break;
            case EX_NameConst nameConst:
                node.Title = $"Name {nameConst.Value}";
                node.Payload["value"] = nameConst.Value.ToString();
                AddPin(node, "Value", "Output", "Name");
                break;
            case EX_FinalFunction finalFunction:
                node.Kind = "FunctionCall";
                node.FunctionName = GetObjectName(asset, finalFunction.StackNode.Index);
                node.OwnerKey = GetObjectOwner(asset, finalFunction.StackNode.Index);
                node.Title = string.IsNullOrWhiteSpace(node.FunctionName) ? "Final Function" : node.FunctionName;
                node.Payload["stackNodeIndex"] = finalFunction.StackNode.Index;
                AddParameterPins(node, finalFunction.Parameters);
                break;
            case EX_VirtualFunction virtualFunction:
                node.Kind = "FunctionCall";
                node.FunctionName = virtualFunction.VirtualFunctionName.ToString();
                node.Title = node.FunctionName;
                node.Payload["virtualFunctionName"] = node.FunctionName;
                AddParameterPins(node, virtualFunction.Parameters);
                break;
            case EX_Context context:
                ApplyContextMetadata(asset, ownerExport, context, node);
                break;
            case EX_Jump jump:
                node.Title = "Jump";
                node.Payload["codeOffset"] = jump.CodeOffset;
                break;
            case EX_JumpIfNot jumpIfNot:
                node.Title = "Branch";
                node.Payload["codeOffset"] = jumpIfNot.CodeOffset;
                AddPin(node, "Condition", "Input", "Bool");
                break;
            case EX_Return:
                node.Title = "Return";
                break;
            case EX_Self:
                node.Title = "Self";
                break;
            case EX_True:
                node.Title = "True";
                node.Payload["value"] = true;
                AddPin(node, "Value", "Output", "Bool");
                break;
            case EX_False:
                node.Title = "False";
                node.Payload["value"] = false;
                AddPin(node, "Value", "Output", "Bool");
                break;
        }

        return node;
    }

    private void ApplyContextMetadata(UAsset asset, StructExport ownerExport, EX_Context context, GraphNodeDto node)
    {
        node.Kind = "Context";
        node.Title = context is EX_Context_FailSilent ? "Context (Fail Silent)" : "Context";
        node.Payload["skipOffsetForNull"] = context.Offset;
        node.Payload["resolvedOwnerIndex"] = GetResolvedOwnerIndex(context);

        if (context.ContextExpression is EX_FinalFunction finalFunction)
        {
            node.Kind = "ContextFunctionCall";
            node.FunctionName = GetObjectName(asset, finalFunction.StackNode.Index);
            node.OwnerKey = GetOwnerKey(asset, context, finalFunction.StackNode.Index);
            node.Title = string.IsNullOrWhiteSpace(node.FunctionName) ? "Context Function" : node.FunctionName;
            node.Payload["stackNodeIndex"] = finalFunction.StackNode.Index;
            node.Payload["parameterSignature"] = BuildParameterSignature(finalFunction.Parameters);
            AddParameterPins(node, finalFunction.Parameters);
        }
        else if (context.ContextExpression is EX_VirtualFunction virtualFunction)
        {
            node.Kind = "ContextFunctionCall";
            node.FunctionName = virtualFunction.VirtualFunctionName.ToString();
            node.OwnerKey = GetOwnerKey(asset, context, 0);
            node.Title = node.FunctionName;
            node.Payload["virtualFunctionName"] = node.FunctionName;
            node.Payload["parameterSignature"] = BuildParameterSignature(virtualFunction.Parameters);
            AddParameterPins(node, virtualFunction.Parameters);
        }

        if (string.IsNullOrWhiteSpace(node.OwnerKey))
        {
            node.OwnerKey = ownerExport.ObjectName?.ToString() ?? string.Empty;
        }
    }

    private void UpsertContextTemplate(UAsset asset, StructExport ownerExport, EX_Context context, GraphNodeDto node)
    {
        if (string.IsNullOrWhiteSpace(node.FunctionName))
        {
            return;
        }

        var parameterSignature = node.Payload["parameterSignature"]?.GetValue<string>() ?? string.Empty;
        var resolvedOwnerIndex = GetResolvedOwnerIndex(context);
        var ownerKey = string.IsNullOrWhiteSpace(node.OwnerKey)
            ? ownerExport.ObjectName?.ToString() ?? string.Empty
            : node.OwnerKey;

        var template = new NodeTemplateDto
        {
            Key = BuildTemplateKey(ownerKey, node.FunctionName, parameterSignature),
            OwnerKey = ownerKey,
            FunctionName = node.FunctionName,
            ParameterSignature = parameterSignature,
            ResolvedOwnerIndex = resolvedOwnerIndex == 0 ? -99 : resolvedOwnerIndex,
            SourceExpression = node.ExpressionType,
            IsResolved = resolvedOwnerIndex != 0,
            Pins = node.Pins.Select(ClonePin).ToList()
        };

        node.Payload["templateKey"] = template.Key;
        nodeLibrary.Upsert(template);
    }

    public static string BuildTemplateKey(string ownerKey, string functionName, string parameterSignature)
    {
        return $"{ownerKey.Trim()}::{functionName.Trim()}({parameterSignature.Trim()})";
    }

    private static void AddParameterPins(GraphNodeDto node, IReadOnlyList<KismetExpression> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            AddPin(node, $"Param {i + 1}", "Input", parameters[i].Inst);
        }
    }

    private static string BuildParameterSignature(IReadOnlyList<KismetExpression> parameters)
    {
        return string.Join(",", parameters.Select(parameter => parameter.GetType().Name));
    }

    private static GraphPinDto AddPin(GraphNodeDto node, string name, string direction, string pinType)
    {
        var pin = new GraphPinDto
        {
            Id = $"{node.Id}:{direction}:{name}".Replace(' ', '-'),
            Name = name,
            Direction = direction,
            PinType = pinType
        };
        node.Pins.Add(pin);
        return pin;
    }

    private static GraphPinDto ClonePin(GraphPinDto pin)
    {
        return new GraphPinDto
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = pin.Name,
            Direction = pin.Direction,
            PinType = pin.PinType
        };
    }

    private static IEnumerable<(string Name, int Index, KismetExpression Expression)> GetChildren(KismetExpression expression)
    {
        return expression switch
        {
            EX_Context context => Enumerate(
                ("Context", context.ObjectExpression),
                ("Expression", context.ContextExpression)),
            EX_FinalFunction function => function.Parameters.Select((param, index) => ("Param", index, param)),
            EX_VirtualFunction function => function.Parameters.Select((param, index) => ("Param", index, param)),
            EX_Return ret => Enumerate(("Return", ret.ReturnExpression)),
            EX_JumpIfNot jumpIfNot => Enumerate(("Condition", jumpIfNot.BooleanExpression)),
            EX_Let let => Enumerate(("Variable", let.Variable), ("Expression", let.Expression)),
            _ => []
        };

        static IEnumerable<(string Name, int Index, KismetExpression Expression)> Enumerate(params (string Name, KismetExpression? Expression)[] entries)
        {
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].Expression is not null)
                {
                    yield return (entries[i].Name, i, entries[i].Expression!);
                }
            }
        }
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

    private static int GetResolvedOwnerIndex(EX_Context context)
    {
        if (context.RValuePointer?.New?.ResolvedOwner is { } resolvedOwner)
        {
            return resolvedOwner.Index;
        }

        if (context.RValuePointer?.Old is { } old)
        {
            return old.Index;
        }

        return 0;
    }

    private static string GetOwnerKey(UAsset asset, EX_Context context, int fallbackIndex)
    {
        var resolvedOwner = GetResolvedOwnerIndex(context);
        if (resolvedOwner != 0)
        {
            return GetFullName(asset, resolvedOwner);
        }

        return fallbackIndex != 0 ? GetObjectOwner(asset, fallbackIndex) : string.Empty;
    }

    private static string GetObjectName(UAsset asset, int index)
    {
        return index switch
        {
            > 0 when index <= asset.Exports.Count => asset.Exports[index - 1].ObjectName?.ToString() ?? string.Empty,
            < 0 when -index <= asset.Imports.Count => asset.Imports[-index - 1].ObjectName?.ToString() ?? string.Empty,
            _ => string.Empty
        };
    }

    private static string GetObjectOwner(UAsset asset, int index)
    {
        return index switch
        {
            > 0 when index <= asset.Exports.Count => GetFullName(asset, asset.Exports[index - 1].OuterIndex.Index),
            < 0 when -index <= asset.Imports.Count => GetFullName(asset, asset.Imports[-index - 1].OuterIndex.Index),
            _ => string.Empty
        };
    }

    private static string GetFullName(UAsset asset, int index)
    {
        if (index > 0 && index <= asset.Exports.Count)
        {
            var export = asset.Exports[index - 1];
            var parent = GetFullName(asset, export.OuterIndex.Index);
            return string.IsNullOrWhiteSpace(parent) ? export.ObjectName?.ToString() ?? string.Empty : $"{parent}.{export.ObjectName}";
        }

        if (index < 0 && -index <= asset.Imports.Count)
        {
            var import = asset.Imports[-index - 1];
            var parent = GetFullName(asset, import.OuterIndex.Index);
            return string.IsNullOrWhiteSpace(parent) ? import.ObjectName?.ToString() ?? string.Empty : $"{parent}.{import.ObjectName}";
        }

        return string.Empty;
    }

}
