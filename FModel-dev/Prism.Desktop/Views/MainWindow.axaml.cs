using Avalonia.Controls;
using Prism.Desktop.ViewModels;

namespace Prism.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.TopLevel = this;
                vm.WindowWidth = Width;
            }
        };
        SizeChanged += (_, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.WindowWidth = e.NewSize.Width;
            }
        };
    }
}
