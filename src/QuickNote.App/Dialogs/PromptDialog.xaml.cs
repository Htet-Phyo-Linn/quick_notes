using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace QuickNote.App.Dialogs
{
    public partial class PromptDialog : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public string InputText { get; private set; } = string.Empty;

        private readonly bool _isDarkMode;

        public PromptDialog(string title, string prompt, string initialText = "", bool isDark = true)
        {
            _isDarkMode = isDark;
            InitializeComponent();
            Title = title;
            PromptLabel.Text = prompt;
            InputTextBox.Text = initialText;

            if (!isDark)
            {
                Background = System.Windows.Media.Brushes.White;
                PromptLabel.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x37, 0x35, 0x2F));
                InputTextBox.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x37, 0x35, 0x2F));
            }

            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    int darkMode = isDark ? 1 : 0;
                    if (DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int)) != 0)
                    {
                        DwmSetWindowAttribute(hwnd, 19, ref darkMode, sizeof(int));
                    }
                }
            };

            Loaded += (s, e) =>
            {
                InputTextBox.Focus();
                InputTextBox.SelectAll();
            };
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Confirm();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirm();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void Confirm()
        {
            var text = InputTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                ConfirmDialog.ShowMessage(this, "Input Required", "Name cannot be empty.", "OK", _isDarkMode);
                return;
            }

            InputText = text;
            DialogResult = true;
            Close();
        }
    }
}
