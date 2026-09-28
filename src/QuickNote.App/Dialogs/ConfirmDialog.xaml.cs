using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickNote.App.Dialogs
{
    public partial class ConfirmDialog : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public bool Result { get; private set; }

        public ConfirmDialog(
            string title, 
            string message, 
            string confirmText = "Confirm", 
            string cancelText = "Cancel",
            bool isDanger = true, 
            bool isDarkMode = true,
            bool isConfirmOnly = false)
        {
            InitializeComponent();

            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;
            ConfirmBtn.Content = confirmText;
            CancelBtn.Content = cancelText;

            if (isConfirmOnly)
            {
                CancelBtn.Visibility = Visibility.Collapsed;
            }

            SourceInitialized += (s, e) =>
            {
                UpdateTitleBarTheme(isDarkMode);
            };

            ApplyTheme(isDarkMode, isDanger, isConfirmOnly);
        }

        public void UpdateTitleBarTheme(bool isDark)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    int darkMode = isDark ? 1 : 0;
                    if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int)) != 0)
                    {
                        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
                    }
                }
            }
            catch { }
        }

        private void ApplyTheme(bool isDarkMode, bool isDanger, bool isConfirmOnly)
        {
            if (isDarkMode)
            {
                Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
                TitleText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                MessageText.Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
                CancelBtn.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                CancelBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
                CancelBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38));
            }
            else
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                TitleText.Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                MessageText.Foreground = new SolidColorBrush(Color.FromRgb(0x78, 0x77, 0x74));
                CancelBtn.Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF5));
                CancelBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                CancelBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xDC));
            }

            if (isDanger)
            {
                ConfirmBtn.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                ConfirmBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
                ConfirmBtn.Foreground = Brushes.White;
                IconContainer.Background = isDarkMode 
                    ? new SolidColorBrush(Color.FromRgb(0x45, 0x1A, 0x1A))
                    : new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2));
                DialogIconPath.Data = (Geometry)FindResource("IconWarningGeometry");
                DialogIconPath.Stroke = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            }
            else
            {
                ConfirmBtn.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                ConfirmBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
                ConfirmBtn.Foreground = Brushes.White;
                IconContainer.Background = isDarkMode 
                    ? new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B))
                    : new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF));
                DialogIconPath.Data = (Geometry)FindResource("IconInfoGeometry");
                DialogIconPath.Stroke = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            DialogResult = false;
            Close();
        }

        public static bool ShowConfirm(
            Window? owner, 
            string title, 
            string message, 
            string confirmText = "Delete", 
            string cancelText = "Cancel", 
            bool isDanger = true, 
            bool isDarkMode = true)
        {
            var dlg = new ConfirmDialog(title, message, confirmText, cancelText, isDanger, isDarkMode, isConfirmOnly: false);
            if (owner != null) dlg.Owner = owner;
            return dlg.ShowDialog() == true;
        }

        public static void ShowMessage(
            Window? owner, 
            string title, 
            string message, 
            string buttonText = "OK", 
            bool isDarkMode = true)
        {
            var dlg = new ConfirmDialog(title, message, confirmText: buttonText, cancelText: string.Empty, isDanger: false, isDarkMode: isDarkMode, isConfirmOnly: true);
            if (owner != null) dlg.Owner = owner;
            dlg.ShowDialog();
        }
    }
}
