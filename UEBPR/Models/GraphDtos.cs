using System.Text.Json.Nodes;

namespace UEBPR.Models;

public sealed record ErrorDto(string Message);

public sealed record AssetOpenResultDto(
    Guid SessionId,
    string AssetName,
    string AssetPath,
    bool HasUsmap,
    IReadOnlyList<ExportSummaryDto> Exports,
    IReadOnlyList<string> Warnings);

public sealed record ExportSummaryDto(
    int ExportIndex,
    string Name,
    string Type,
    bool HasScriptBytecode,
    int ScriptNodeCount,
    int LoadedPropertyCount);

public sealed class GraphDocumentDto
{
    public Guid SessionId { get; set; }
    public int ExportIndex { get; set; }
    public string AssetName { get; set; } = string.Empty;
    public string ExportName { get; set; } = string.Empty;
    public string ExportType { get; set; } = string.Empty;
    public List<GraphNodeDto> Nodes { get; set; } = [];
    public List<GraphConnectionDto> Connections { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public JsonObject Metadata { get; set; } = [];
}

public sealed class GraphNodeDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "Expression";
    public string Title { get; set; } = string.Empty;
    public string OwnerKey { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public string ExpressionType { get; set; } = string.Empty;
    public GraphPositionDto Position { get; set; } = new();
    public List<GraphPinDto> Pins { get; set; } = [];
    public JsonObject Payload { get; set; } = [];
}

public sealed class GraphPinDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Direction { get; set; } = "Input";
    public string PinType { get; set; } = "Exec";
    public List<string> LinkedTo { get; set; } = [];
}

public sealed record GraphConnectionDto(string FromNodeId, string FromPinId, string ToNodeId, string ToPinId);

public sealed class GraphPositionDto
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class NodeLibraryDto
{
    public int Version { get; set; } = 1;
    public List<NodeTemplateDto> Templates { get; set; } = [];
}

public sealed class NodeTemplateDto
{
    public string Key { get; set; } = string.Empty;
    public string OwnerKey { get; set; } = string.Empty;
    public int ResolvedOwnerIndex { get; set; }
    public string FunctionName { get; set; } = string.Empty;
    public string ParameterSignature { get; set; } = string.Empty;
    public string SourceExpression { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public List<GraphPinDto> Pins { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ApplyGraphResultDto
{
    public int ExportIndex { get; set; }
    public int CompiledExpressionCount { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<string> UnresolvedNodeIds { get; set; } = [];
}

public sealed class SaveResultDto
{
    public string OutputPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
}
