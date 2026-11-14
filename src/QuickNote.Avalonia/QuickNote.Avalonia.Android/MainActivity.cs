using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Avalonia.Android;

namespace QuickNote.Avalonia.Android;

[Activity(
    Label = "QuickNote",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateHidden)]
public class MainActivity : AvaloniaMainActivity, ViewTreeObserver.IOnGlobalLayoutListener
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.DecorView?.ViewTreeObserver?.AddOnGlobalLayoutListener(this);
    }

    public void OnGlobalLayout()
    {
        if (Window?.DecorView != null)
        {
            ConfigureWebViews(Window.DecorView);
        }
    }

    private void ConfigureWebViews(View view)
    {
        if (view is global::Android.Webkit.WebView wv)
        {
            try
            {
                var s = wv.Settings;
                if (!s.AllowFileAccess) s.AllowFileAccess = true;
                if (!s.AllowContentAccess) s.AllowContentAccess = true;
                if (!s.AllowFileAccessFromFileURLs) s.AllowFileAccessFromFileURLs = true;
                if (!s.AllowUniversalAccessFromFileURLs) s.AllowUniversalAccessFromFileURLs = true;
                if (!s.JavaScriptEnabled) s.JavaScriptEnabled = true;
                if (!s.DomStorageEnabled) s.DomStorageEnabled = true;
                if (!s.DatabaseEnabled) s.DatabaseEnabled = true;
            }
            catch { }
        }
        else if (view is ViewGroup vg)
        {
            for (int i = 0; i < vg.ChildCount; i++)
            {
                var child = vg.GetChildAt(i);
                if (child != null) ConfigureWebViews(child);
            }
        }
    }
}
