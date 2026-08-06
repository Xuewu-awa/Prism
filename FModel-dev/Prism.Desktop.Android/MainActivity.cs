using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using System.Threading.Tasks;

namespace Prism.Desktop.Android;

[Activity(
    Label = "Prism",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;
    }

    protected override void OnResume()
    {
        base.OnResume();
        // App 初始化完成后注入原生确认对话框（合并冲突询问）
        if (global::Avalonia.Application.Current is Prism.Desktop.App app
            && app.MainVm is not null
            && app.MainVm.NativeConfirmAsync is null)
        {
            app.MainVm.NativeConfirmAsync = ShowConfirmDialogAsync;
        }
    }

    protected override void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }

        base.OnDestroy();
    }

    private Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        RunOnUiThread(() =>
        {
            new AlertDialog.Builder(this)
                .SetTitle(title)
                .SetMessage(message)
                .SetPositiveButton("确定", (_, _) => tcs.TrySetResult(true))
                .SetNegativeButton("取消", (_, _) => tcs.TrySetResult(false))
                .SetCancelable(false)
                .Show();
        });
        return tcs.Task;
    }

    /// <summary>
    /// 返回键按页面层级返回：子目录→上级目录，其他页面→主页，主页才退出。
    /// </summary>
    public override void OnBackPressed()
    {
        if (global::Avalonia.Application.Current is Prism.Desktop.App app
            && app.MainVm?.HandleBack() == true)
        {
            return;
        }

        base.OnBackPressed();
    }
}
