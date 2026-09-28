using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace QuickNote.App.Dialogs
{
    public partial class FolderPickerDialog : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private readonly List<(string DisplayName, string FullPath)> _folderOptions;
        private readonly bool _isDarkMode;

        public string SelectedFolderPath { get; private set; } = string.Empty;

        public FolderPickerDialog(string title, string instruction, string actionButtonText, List<(string DisplayName, string FullPath)> folderOptions, bool isDark = true)
        {
            _isDarkMode = isDark;
            InitializeComponent();
            Title = title;
            InstructionLabel.Text = instruction;
            ActionButton.Content = actionButtonText;
            _folderOptions = folderOptions ?? new List<(string DisplayName, string FullPath)>();

            foreach (var opt in _folderOptions)
            {
                FoldersListBox.Items.Add(opt.DisplayName);
            }

            if (FoldersListBox.Items.Count > 0)
            {
                FoldersListBox.SelectedIndex = 0;
            }

            if (!isDark)
            {
                Background = System.Windows.Media.Brushes.White;
                InstructionLabel.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x37, 0x35, 0x2F));
                FoldersListBox.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x37, 0x35, 0x2F));
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

        private void FoldersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Confirm();
        }

        private void Confirm()
        {
            int index = FoldersListBox.SelectedIndex;
            if (index >= 0 && index < _folderOptions.Count)
            {
                SelectedFolderPath = _folderOptions[index].FullPath;
                DialogResult = true;
                Close();
            }
            else
            {
                ConfirmDialog.ShowMessage(this, "Select Folder", "Please select a destination folder.", "OK", _isDarkMode);
            }
        }
    }
}
