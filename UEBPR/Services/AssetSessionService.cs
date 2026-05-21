using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Unversioned;
using UAssetAPI.UnrealTypes;
using UEBPR.Models;

namespace UEBPR.Services;

public sealed class AssetSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string AssetPath { get; init; }
    public string? UexpPath { get; init; }
    public string? UsmapPath { get; set; }
    public EngineVersion EngineVersion { get; init; } = EngineVersion.UNKNOWN;
    public required UAsset Asset { get; set; }
    public Usmap? Mappings { get; set; }
    public List<string> Warnings { get; } = [];
}

public sealed class AssetSessionService
{
    private readonly IWebHostEnvironment environment;
    private readonly Dictionary<Guid, AssetSession> sessions = [];
    private readonly object gate = new();

    public AssetSessionService(IWebHostEnvironment environment)
    {
        this.environment = environment;
    }

    public async Task<AssetSession> OpenAsync(IFormFile uasset, IReadOnlyList<IFormFile> files, string? engineVersionName, CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var sessionRoot = GetSessionRoot(sessionId);
        Directory.CreateDirectory(sessionRoot);

        var assetPath = Path.Combine(sessionRoot, Path.GetFileName(uasset.FileName));
        await CopyFileAsync(uasset, assetPath, cancellationToken);

        var uexp = files.FirstOrDefault(file =>
            string.Equals(Path.GetFileName(file.FileName), Path.GetFileName(Path.ChangeExtension(uasset.FileName, ".uexp")), StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(file.FileName), Path.GetFileNameWithoutExtension(uasset.FileName), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(file.FileName), ".uexp", StringComparison.OrdinalIgnoreCase));

        string? uexpPath = null;
        if (uexp is not null)
        {
            uexpPath = Path.ChangeExtension(assetPath, ".uexp");
            await CopyFileAsync(uexp, uexpPath, cancellationToken);
        }

        var usmap = files.FirstOrDefault(file =>
            string.Equals(file.Name, "usmap", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(file.FileName), ".usmap", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(file.FileName), ".jmap", StringComparison.OrdinalIgnoreCase)
            || file.FileName.EndsWith(".jmap.gz", StringComparison.OrdinalIgnoreCase));

        Usmap? mappings = null;
        string? usmapPath = null;
        if (usmap is not null)
        {
            usmapPath = Path.Combine(sessionRoot, Path.GetFileName(usmap.FileName));
            await CopyFileAsync(usmap, usmapPath, cancellationToken);
            mappings = new Usmap(usmapPath);
        }

        var engineVersion = ParseEngineVersion(engineVersionName);
        var asset = new UAsset(assetPath, loadUexp: uexpPath is not null, engineVersion: engineVersion, mappings: mappings);
        var session = new AssetSession
        {
            Id = sessionId,
            AssetPath = assetPath,
            UexpPath = uexpPath,
            UsmapPath = usmapPath,
            EngineVersion = engineVersion,
            Asset = asset,
            Mappings = mappings
        };

        if (mappings is null)
        {
            session.Warnings.Add("未提供 usmap。未版本化属性和部分 Pin 类型可能不完整。");
        }
        if (uexpPath is null)
        {
            session.Warnings.Add("未找到匹配的 .uexp。如果该资产使用分离导出，请同时选择 uasset 与 uexp，或使用目录选择。");
        }

        lock (gate)
        {
            sessions[session.Id] = session;
        }

        return session;
    }

    public async Task<AssetOpenResultDto> AttachUsmapAsync(AssetSession session, IFormFile usmap, CancellationToken cancellationToken)
    {
        var sessionRoot = GetSessionRoot(session.Id);
        Directory.CreateDirectory(sessionRoot);

        var usmapPath = Path.Combine(sessionRoot, Path.GetFileName(usmap.FileName));
        await CopyFileAsync(usmap, usmapPath, cancellationToken);

        session.UsmapPath = usmapPath;
        session.Mappings = new Usmap(usmapPath);
        session.Asset = new UAsset(session.AssetPath, loadUexp: session.UexpPath is not null, engineVersion: session.EngineVersion, mappings: session.Mappings);
        session.Warnings.RemoveAll(static warning => warning.Contains("usmap", StringComparison.OrdinalIgnoreCase));

        return Describe(session);
    }

    public bool TryGet(Guid id, out AssetSession session)
    {
        lock (gate)
        {
            return sessions.TryGetValue(id, out session!);
        }
    }

    public AssetOpenResultDto Describe(AssetSession session)
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

    private string GetSessionRoot(Guid sessionId)
    {
        return Path.Combine(environment.ContentRootPath, "App_Data", "sessions", sessionId.ToString("N"));
    }

    private static async Task CopyFileAsync(IFormFile source, string targetPath, CancellationToken cancellationToken)
    {
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target, cancellationToken);
    }

    private static EngineVersion ParseEngineVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            return EngineVersion.UNKNOWN;
        }

        if (Enum.TryParse<EngineVersion>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"不支持的 Unreal Engine 版本：{value}。");
    }
}
