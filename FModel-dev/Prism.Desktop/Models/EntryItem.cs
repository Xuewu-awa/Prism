using PakTool.Core;

namespace Prism.Desktop.Models;

/// <summary>扁平文件列表项（对应 Web 版的 Name/Type/Size 三列）。</summary>
public sealed record EntryItem(
    string FullPath,
    string Name,
    bool IsDirectory,
    string Kind,
    string SizeText)
{
    /// <summary>类型图标前缀（DIR/IMG/AUD/VID/3D/MAT/BP/LOC/UE/FILE）。</summary>
    public string KindIcon => IsDirectory ? "DIR" : Kind switch
    {
        "Texture" => "IMG",
        "Audio" => "AUD",
        "Video" => "VID",
        "Model" => "3D",
        "Material" => "MAT",
        "Blueprint" => "BP",
        "Locres" => "LOC",
        "UAsset" => "UE",
        _ => (Kind.Length > 4 ? Kind[..4] : Kind).ToUpperInvariant(),
    };

    /// <summary>次要行：目录显示路径，文件显示大小。</summary>
    public string MetaText => IsDirectory ? FullPath : SizeText;

    public static EntryItem Create(ArchiveEntryDto entry) => new(
        entry.FullPath,
        entry.Name,
        entry.IsDirectory,
        GuessKind(entry),
        entry.IsDirectory ? string.Empty : FormatSize(entry.Size));

    public static string GuessKind(ArchiveEntryDto entry)
    {
        if (entry.IsDirectory)
        {
            return "Folder";
        }

        string ext = entry.Extension.TrimStart('.').ToLowerInvariant();
        if (ext == "locres")
        {
            return "Locres";
        }

        if (ext is "wav" or "ogg" or "wem" or "binka" or "opus" or "at9")
        {
            return "Audio";
        }

        if (ext is "mp4" or "webm" or "m4v" or "mov" or "bk2" or "bik")
        {
            return "Video";
        }

        if (!entry.IsAssetPackage)
        {
            return string.IsNullOrWhiteSpace(ext) ? "File" : ext.ToUpperInvariant();
        }

        string path = entry.FullPath.Replace('\\', '/');
        if (path.Contains("/Blueprint", StringComparison.OrdinalIgnoreCase) || path.Contains("/BP_", StringComparison.OrdinalIgnoreCase))
        {
            return "Blueprint";
        }

        if (path.Contains("/Texture", StringComparison.OrdinalIgnoreCase) || path.Contains("/T_", StringComparison.OrdinalIgnoreCase))
        {
            return "Texture";
        }

        if (path.Contains("/Material", StringComparison.OrdinalIgnoreCase) || path.Contains("/MI_", StringComparison.OrdinalIgnoreCase) || path.Contains("/M_", StringComparison.OrdinalIgnoreCase))
        {
            return "Material";
        }

        if (path.Contains("/Mesh", StringComparison.OrdinalIgnoreCase) || path.Contains("/SK_", StringComparison.OrdinalIgnoreCase) || path.Contains("/SM_", StringComparison.OrdinalIgnoreCase))
        {
            return "Model";
        }

        return "UAsset";
    }

    public static string FormatSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = size;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{size} B" : $"{value:0.##} {units[unit]}";
    }
}

/// <summary>预览详情行（Label: Value）。</summary>
public sealed record DetailItem(string Label, string Value);
