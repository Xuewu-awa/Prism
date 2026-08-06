using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Desktop.Services;

namespace Prism.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string VersionText { get; set; }

    [ObservableProperty]
    public partial string CliStatus { get; set; }

    [ObservableProperty]
    public partial string AstcencStatus { get; set; }

    [ObservableProperty]
    public partial string TexconvStatus { get; set; }

    [ObservableProperty]
    public partial string TempDirectory { get; set; }

    public SettingsViewModel()
    {
        VersionText = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "未知";
        UAssetCliRunner? runner = UAssetCliRunner.TryCreate();
        CliStatus = runner is not null ? "已找到 UAssetCLI" : "未找到 UAssetCLI（替换功能不可用）";
        AstcencStatus = runner?.HasAstcenc == true ? "已找到 astcenc" : "未找到 astcenc";
        TexconvStatus = runner?.HasTexconv == true ? "已找到 texconv" : "未找到 texconv";
        TempDirectory = Path.GetTempPath();
    }
}
