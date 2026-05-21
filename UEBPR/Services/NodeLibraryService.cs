using System.Text.Json;
using UEBPR.Models;

namespace UEBPR.Services;

public sealed class NodeLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string libraryPath;
    private readonly object gate = new();
    private NodeLibraryDto? cached;

    public NodeLibraryService(IWebHostEnvironment environment)
    {
        var appData = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appData);
        libraryPath = Path.Combine(appData, "node-library.json");
    }

    public NodeLibraryDto GetLibrary()
    {
        lock (gate)
        {
            cached ??= Load();
            return cached;
        }
    }

    public NodeLibraryDto Import(NodeLibraryDto imported)
    {
        lock (gate)
        {
            cached = new NodeLibraryDto
            {
                Version = Math.Max(1, imported.Version),
                Templates = imported.Templates
                    .Where(static template => !string.IsNullOrWhiteSpace(template.Key))
                    .GroupBy(static template => template.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static group => group.OrderByDescending(template => template.UpdatedAt).First())
                    .OrderBy(static template => template.OwnerKey, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static template => template.FunctionName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
            Save(cached);
            return cached;
        }
    }

    public void Upsert(NodeTemplateDto template)
    {
        if (string.IsNullOrWhiteSpace(template.Key))
        {
            return;
        }

        lock (gate)
        {
            cached ??= Load();
            var existingIndex = cached.Templates.FindIndex(item => string.Equals(item.Key, template.Key, StringComparison.OrdinalIgnoreCase));
            template.UpdatedAt = DateTimeOffset.UtcNow;
            if (existingIndex >= 0)
            {
                cached.Templates[existingIndex] = template;
            }
            else
            {
                cached.Templates.Add(template);
            }

            cached.Templates = cached.Templates
                .OrderBy(static item => item.OwnerKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.FunctionName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Save(cached);
        }
    }

    public bool TryFind(string key, out NodeTemplateDto template)
    {
        lock (gate)
        {
            cached ??= Load();
            template = cached.Templates.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))!;
            return template is not null;
        }
    }

    public byte[] ExportBytes()
    {
        lock (gate)
        {
            cached ??= Load();
            return JsonSerializer.SerializeToUtf8Bytes(cached, JsonOptions);
        }
    }

    private NodeLibraryDto Load()
    {
        if (!File.Exists(libraryPath))
        {
            var empty = new NodeLibraryDto();
            Save(empty);
            return empty;
        }

        using var stream = File.OpenRead(libraryPath);
        return JsonSerializer.Deserialize<NodeLibraryDto>(stream, JsonOptions) ?? new NodeLibraryDto();
    }

    private void Save(NodeLibraryDto library)
    {
        using var stream = File.Create(libraryPath);
        JsonSerializer.Serialize(stream, library, JsonOptions);
    }
}
