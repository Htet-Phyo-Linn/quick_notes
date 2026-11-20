using System;

namespace QuickNote.Avalonia
{
    public class AndroidSavedState
    {
        public string? ActiveNotePath { get; set; }
        public bool IsEditorOpen { get; set; }
        public string? SearchQuery { get; set; }
    }

    public static class AndroidPlatformBridge
    {
        public static event Action<int>? KeyboardHeightChanged;
        public static Func<bool>? HandleBackPress;
        public static event Action? NetworkConnected;
        public static event Action? AppPaused;

        // State preservation callbacks
        public static Func<string?>? GetCurrentNotePath;
        public static Func<bool>? GetIsEditorOpen;
        public static Func<string?>? GetSearchQuery;

        public static AndroidSavedState? RestoredState { get; set; }

        public static void NotifyKeyboardHeightChanged(int heightDp)
        {
            KeyboardHeightChanged?.Invoke(heightDp);
        }

        public static bool OnBackPressed()
        {
            return HandleBackPress?.Invoke() ?? false;
        }

        public static void NotifyNetworkConnected()
        {
            NetworkConnected?.Invoke();
        }

        public static void NotifyAppPaused()
        {
            AppPaused?.Invoke();
        }
    }
}
