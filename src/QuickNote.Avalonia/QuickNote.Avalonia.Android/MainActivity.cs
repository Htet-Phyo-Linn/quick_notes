using System;
using System.IO;
using Android.App;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Util;
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
    private const string Tag = "QuickNoteCrashHandler";
    private int _lastKeyboardHeightDp = -1;
    private static bool _exceptionHandlersInstalled = false;
    private ConnectivityManager? _connectivityManager;
    private NetworkStatusCallback? _networkCallback;

    private void RegisterNetworkCallback()
    {
        try
        {
            _connectivityManager = (ConnectivityManager?)GetSystemService(ConnectivityService);
            if (_connectivityManager != null)
            {
                var req = new NetworkRequest.Builder()
                    .AddCapability(NetCapability.Internet)
                    .Build();
                if (req != null)
                {
                    _networkCallback = new NetworkStatusCallback();
                    _connectivityManager.RegisterNetworkCallback(req, _networkCallback);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"Could not register network callback: {ex.Message}");
        }
    }

    private void UnregisterNetworkCallback()
    {
        try
        {
            if (_connectivityManager != null && _networkCallback != null)
            {
                _connectivityManager.UnregisterNetworkCallback(_networkCallback);
                _networkCallback = null;
            }
        }
        catch { }
    }

    protected override void OnPause()
    {
        base.OnPause();
        AndroidPlatformBridge.NotifyAppPaused();
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        try
        {
            var notePath = AndroidPlatformBridge.GetCurrentNotePath?.Invoke();
            bool isEditorOpen = AndroidPlatformBridge.GetIsEditorOpen?.Invoke() ?? false;
            var query = AndroidPlatformBridge.GetSearchQuery?.Invoke();

            if (!string.IsNullOrEmpty(notePath)) outState.PutString("saved_active_note_path", notePath);
            outState.PutBoolean("saved_is_editor_open", isEditorOpen);
            if (!string.IsNullOrEmpty(query)) outState.PutString("saved_search_query", query);
        }
        catch { }
    }

    protected override void OnDestroy()
    {
        UnregisterNetworkCallback();
        base.OnDestroy();
    }

    private class NetworkStatusCallback : ConnectivityManager.NetworkCallback
    {
        public override void OnAvailable(Network network)
        {
            base.OnAvailable(network);
            AndroidPlatformBridge.NotifyNetworkConnected();
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        InstallGlobalExceptionHandlers();
        base.OnCreate(savedInstanceState);

        if (savedInstanceState != null)
        {
            AndroidPlatformBridge.RestoredState = new AndroidSavedState
            {
                ActiveNotePath = savedInstanceState.GetString("saved_active_note_path"),
                IsEditorOpen = savedInstanceState.GetBoolean("saved_is_editor_open", false),
                SearchQuery = savedInstanceState.GetString("saved_search_query")
            };
        }

        Window?.DecorView?.ViewTreeObserver?.AddOnGlobalLayoutListener(this);

        try
        {
            OnBackPressedDispatcher.AddCallback(this, new BackPressCallback(this));
        }
        catch { }

        RegisterNetworkCallback();

        // Configure native webviews once after initial window inflation
        Window?.DecorView?.Post(() =>
        {
            if (Window?.DecorView != null)
            {
                ConfigureWebViews(Window.DecorView);
            }
        });
    }

    private void InstallGlobalExceptionHandlers()
    {
        if (_exceptionHandlersInstalled) return;
        _exceptionHandlersInstalled = true;

        AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
        {
            var ex = e.Exception;
            LogCrash("AndroidEnvironment.UnhandledExceptionRaiser", ex);
            // Prevent instant SIGABRT if it's a non-fatal platform or UI layout hiccup
            if (ex is PlatformNotSupportedException || ex is InvalidOperationException || ex is IOException)
            {
                e.Handled = true;
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogCrash("AppDomain.UnhandledException", ex);
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private void LogCrash(string source, Exception? ex)
    {
        if (ex == null) return;
        string logMsg = $"[{DateTime.UtcNow:O}] [{source}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n";
        Log.Error(Tag, logMsg);

        try
        {
            var filesDir = ApplicationContext?.FilesDir?.AbsolutePath;
            if (!string.IsNullOrEmpty(filesDir))
            {
                var crashFile = Path.Combine(filesDir, "crash.log");
                File.AppendAllText(crashFile, logMsg + "\n" + new string('-', 60) + "\n");
            }
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

#pragma warning disable CS0809, CA1416, CA1422
    [System.Obsolete]
    public override void OnBackPressed()
    {
        if (AndroidPlatformBridge.OnBackPressed())
        {
            return;
        }
        base.OnBackPressed();
    }
#pragma warning restore CS0809, CA1416, CA1422

    public void OnGlobalLayout()
    {
        if (Window?.DecorView != null)
        {
            // Note: ConfigureWebViews removed from OnGlobalLayout to eliminate massive per-frame tree traversal overhead

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
