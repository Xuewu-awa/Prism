using Avalonia.Controls;
using Avalonia.Input;
using Prism.Desktop.ViewModels;

namespace Prism.Desktop.Views;

public partial class FileBrowserView : UserControl
{
    public FileBrowserView()
    {
        InitializeComponent();
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ActivateCommand.Execute(null);
        }
    }
}
