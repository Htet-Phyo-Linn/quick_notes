using Android.App;
using Android.Content.PM;
using Android.Views;
using Avalonia.Android;

namespace QuickNote.Avalonia.Android;

[Activity(
    Label = "QuickNote",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateHidden)]
public class MainActivity : AvaloniaMainActivity
{
}
