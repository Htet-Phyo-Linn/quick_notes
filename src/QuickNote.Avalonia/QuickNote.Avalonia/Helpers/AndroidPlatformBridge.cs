using System;

namespace QuickNote.Avalonia
{
    public static class AndroidPlatformBridge
    {
        public static event Action<int>? KeyboardHeightChanged;
        public static Func<bool>? HandleBackPress;

        public static void NotifyKeyboardHeightChanged(int heightDp)
        {
            KeyboardHeightChanged?.Invoke(heightDp);
        }

        public static bool OnBackPressed()
        {
            return HandleBackPress?.Invoke() ?? false;
        }
    }
}
