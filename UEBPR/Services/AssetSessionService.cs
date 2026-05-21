using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Unversioned;
using UEBPR.Models;

namespace UEBPR.Services;

public sealed class AssetSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string AssetPath { get; init; }
    public string? UexpPath { get; init; }
    public string? UsmapPath { get; set; }
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

    public async Task<AssetSession> OpenAsync(IFormFile uasset, IFormFile? uexp, IFormFile? usmap, CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var sessionRoot = GetSessionRoot(sessionId);
        Directory.CreateDirectory(sessionRoot);

        var assetPath = Path.Combine(sessionRoot, Path.GetFileName(uasset.FileName));
        await CopyFileAsync(uasset, assetPath, cancellationToken);

        string? uexpPath = null;
        if (uexp is not null)
        {
            uexpPath = Path.ChangeExtension(assetPath, ".uexp");
            await CopyFileAsync(uexp, uexpPath, cancellationToken);
        }

        Usmap? mappings = null;
        string? usmapPath = null;
        if (usmap is not null)
        {
            usmapPath = Path.Combine(sessionRoot, Path.GetFileName(usmap.FileName));
            await CopyFileAsync(usmap, usmapPath, cancellationToken);
            mappings = new Usmap(usmapPath);
        }

        var asset = new UAsset(assetPath, loadUexp: uexpPath is not null, mappings: mappings);
        var session = new AssetSession
        {
            Id = sessionId,
            AssetPath = assetPath,
            UexpPath = uexpPath,
            UsmapPath = usmapPath,
            Asset = asset,
            Mappings = mappings
        };

        if (mappings is null)
        {
            session.Warnings.Add("No usmap was supplied. Unversioned properties and some pin types may be incomplete.");
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
        session.Asset = new UAsset(session.AssetPath, loadUexp: session.UexpPath is not null, mappings: session.Mappings);
        session.Warnings.RemoveAll(static warning => warning.StartsWith("No usmap", StringComparison.OrdinalIgnoreCase));

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
}
