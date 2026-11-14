using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Webkit;
using AndroidX.Activity;
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
    private int _lastKeyboardHeightDp = -1;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.DecorView?.ViewTreeObserver?.AddOnGlobalLayoutListener(this);

        try
        {
            OnBackPressedDispatcher.AddCallback(this, new BackPressCallback(this));
        }
        catch { }
    }

    private class BackPressCallback : OnBackPressedCallback
    {
        private readonly MainActivity _activity;
        public BackPressCallback(MainActivity activity) : base(true)
        {
            _activity = activity;
        }

        public override void HandleOnBackPressed()
        {
            if (AndroidPlatformBridge.OnBackPressed())
            {
                return;
            }

            // If not handled by app, disable callback and invoke system back
            Enabled = false;
            _activity.OnBackPressedDispatcher.OnBackPressed();
            Enabled = true;
        }
    }

    [System.Obsolete]
    public override void OnBackPressed()
    {
        if (AndroidPlatformBridge.OnBackPressed())
        {
            return;
        }
        base.OnBackPressed();
    }

    public void OnGlobalLayout()
    {
        if (Window?.DecorView != null)
        {
            ConfigureWebViews(Window.DecorView);

            try
            {
                var rect = new global::Android.Graphics.Rect();
                Window.DecorView.GetWindowVisibleDisplayFrame(rect);
                int screenHeight = Window.DecorView.RootView?.Height ?? 0;
                int keypadHeight = screenHeight - rect.Bottom;
                float density = Resources?.DisplayMetrics?.Density ?? 1.0f;
                int keyboardHeightDp = keypadHeight > (screenHeight * 0.15) ? (int)(keypadHeight / density) : 0;
                if (keyboardHeightDp != _lastKeyboardHeightDp)
                {
                    _lastKeyboardHeightDp = keyboardHeightDp;
                    AndroidPlatformBridge.NotifyKeyboardHeightChanged(keyboardHeightDp);
                }
            }
            catch { }
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
