using System.ComponentModel;
using Avalonia.Controls;
using Prism.Desktop.ViewModels;

namespace Prism.Desktop.Views;

public partial class WorkspaceView : UserControl
{
    private MainViewModel? _vm;

    public WorkspaceView()
    {
        InitializeComponent();
        Console.WriteLine($"[ws] constructed, DataContext={DataContext?.GetType().Name}");
        // 双保险：布局容器自身宽度变化时也刷新响应式状态
        SizeChanged += (_, e) =>
        {
            Console.WriteLine($"[ws] SizeChanged {e.NewSize.Width:F0}x{e.NewSize.Height:F0} DataContext={DataContext?.GetType().Name}");
            if (DataContext is MainViewModel vm)
            {
                vm.WindowWidth = e.NewSize.Width;
            }
        };
        DataContextChanged += (_, _) =>
        {
            Console.WriteLine($"[ws] DataContextChanged -> {DataContext?.GetType().Name}");
            if (_vm is not null)
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
            }

            _vm = DataContext as MainViewModel;
            if (_vm is not null)
            {
                _vm.PropertyChanged += OnVmPropertyChanged;
                ApplyLayout(_vm);
            }
            else
            {
                Console.WriteLine("[ws] !! DataContext is not MainViewModel");
            }
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        Console.WriteLine($"[ws] VM.PropertyChanged: {e.PropertyName}");
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsLandscape):
            case nameof(MainViewModel.WindowWidth):
                ApplyLayout(_vm);
                break;
            case nameof(MainViewModel.HasPreview):
                PortraitDrawer.IsVisible = _vm.HasPreview;
                UpdateDebugText();
                break;
        }
    }

    /// <summary>直接以代码控制布局切换（不依赖 XAML 绑定，避免绑定未刷新问题）。</summary>
    private void ApplyLayout(MainViewModel vm)
    {
        LandscapeLayout.IsVisible = vm.IsLandscape;
        PortraitLayout.IsVisible = !vm.IsLandscape;
        PortraitDrawer.IsVisible = vm.HasPreview;
        UpdateDebugText();
        Console.WriteLine($"[ws] ApplyLayout landscape={vm.IsLandscape} L={LandscapeLayout.IsVisible} P={PortraitLayout.IsVisible} D={PortraitDrawer.IsVisible}");
    }

    private void UpdateDebugText()
    {
        if (_vm is null)
        {
            return;
        }

        DebugText.Text = $"DBG width={_vm.WindowWidth:F0} landscape={_vm.IsLandscape} hasPreview={_vm.HasPreview}";
    }
}
