using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using QuickNote.Core.Interfaces;

namespace QuickNote.App.Dialogs
{
    public partial class TrashBinDialog : Window
    {
        private readonly ITrashBinService _trashBinService;
        private readonly bool _isDarkMode;
        public bool HasRestoredItems { get; private set; }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

        public TrashBinDialog(ITrashBinService trashBinService, bool isDarkMode)
        {
            _trashBinService = trashBinService ?? throw new ArgumentNullException(nameof(trashBinService));
            _isDarkMode = isDarkMode;

            InitializeComponent();
            ApplyTheme(isDarkMode);
            SourceInitialized += (s, e) => UpdateTitleBarTheme(isDarkMode);
            LoadItems();
        }

        private void UpdateTitleBarTheme(bool isDark)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    int v = isDark ? 1 : 0;
                    if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int)) != 0)
                        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref v, sizeof(int));
                }
            }
            catch { }
        }

        private void ApplyTheme(bool isDark)
        {
            if (isDark)
            {
                Resources["NotionSidebarBg"]     = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
                Resources["NotionSearchBg"]       = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                Resources["NotionBorder"]         = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2C));
                Resources["NotionTextPrimary"]    = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
                Resources["NotionTextSecondary"]  = new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C));
                Resources["NotionTextMuted"]      = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                Resources["NotionButtonBg"]       = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                Resources["NotionButtonBorder"]   = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38));
                Resources["NotionButtonText"]     = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
                Resources["NotionItemHover"]      = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
                Resources["NotionItemSelected"]   = new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0x6E));
                Resources["NotionScrollbarThumb"] = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x3E));
            }
            else
            {
                Resources["NotionSidebarBg"]     = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF5));
                Resources["NotionSearchBg"]       = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xEE));
                Resources["NotionBorder"]         = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xE8));
                Resources["NotionTextPrimary"]    = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                Resources["NotionTextSecondary"]  = new SolidColorBrush(Color.FromRgb(0x78, 0x77, 0x74));
                Resources["NotionTextMuted"]      = new SolidColorBrush(Color.FromRgb(0x9B, 0x9A, 0x97));
                Resources["NotionButtonBg"]       = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                Resources["NotionButtonBorder"]   = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xD9));
                Resources["NotionButtonText"]     = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                Resources["NotionItemHover"]      = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEB));
                Resources["NotionItemSelected"]   = new SolidColorBrush(Color.FromRgb(0xC8, 0xDC, 0xF5));
                Resources["NotionScrollbarThumb"] = new SolidColorBrush(Color.FromRgb(0xD3, 0xD1, 0xCB));
            }
        }

        private async void LoadItems()
        {
            try
            {
                var items = (await _trashBinService.GetTrashItemsAsync()).ToList();
                TrashListBox.ItemsSource = items;

                if (items.Count == 0)
                {
                    EmptyLabel.Visibility = Visibility.Visible;
                    TrashListBox.Visibility = Visibility.Collapsed;
                    EmptyTrashButton.IsEnabled = false;
                }
                else
                {
                    EmptyLabel.Visibility = Visibility.Collapsed;
                    TrashListBox.Visibility = Visibility.Visible;
                    EmptyTrashButton.IsEnabled = true;
                }
            }
            catch { }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>Gets the TrashItem from the ContextMenu sender's DataContext.</summary>
        private static TrashItem? ItemFromContext(object sender)
        {
            if (sender is MenuItem mi)
            {
                // ContextMenu.PlacementTarget is the Border; its DataContext is the TrashItem
                if (mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement fe)
                    return fe.DataContext as TrashItem;
            }
            return null;
        }

        /// <summary>Gets the ListBoxItem container for a given TrashItem.</summary>
        private ListBoxItem? ContainerFor(TrashItem item)
            => TrashListBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;

        // ── Context-menu handlers ───────────────────────────────────────────────

        private void CtxSelect_Click(object sender, RoutedEventArgs e)
        {
            var item = ItemFromContext(sender);
            if (item == null) return;
            var container = ContainerFor(item);
            if (container != null)
                container.IsSelected = !container.IsSelected;
        }

        private async void CtxRestore_Click(object sender, RoutedEventArgs e)
        {
            var item = ItemFromContext(sender);
            if (item == null) return;
            bool ok = await _trashBinService.RestoreItemAsync(item);
            if (ok) HasRestoredItems = true;
            else ConfirmDialog.ShowMessage(this, "Restore Failed", "Could not restore the selected item.", "OK", _isDarkMode);
            LoadItems();
        }

        private async void CtxDelete_Click(object sender, RoutedEventArgs e)
        {
            var item = ItemFromContext(sender);
            if (item == null) return;
            bool confirm = ConfirmDialog.ShowConfirm(this, "Delete Permanently",
                $"Permanently delete '{item.Title}'?", "Delete", "Cancel",
                isDanger: true, isDarkMode: _isDarkMode);
            if (confirm)
            {
                await _trashBinService.PermanentDeleteAsync(item);
                LoadItems();
            }
        }

        // ── Footer bulk handlers ────────────────────────────────────────────────

        private async void EmptyTrashButton_Click(object sender, RoutedEventArgs e)
        {
            bool confirm = ConfirmDialog.ShowConfirm(this, "Empty Trash Bin",
                "Permanently delete ALL items in Trash?",
                "Empty Trash", "Cancel", isDanger: true, isDarkMode: _isDarkMode);
            if (confirm)
            {
                await _trashBinService.EmptyTrashAsync();
                LoadItems();
            }
        }

        private async void RestoreSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = TrashListBox.SelectedItems.Cast<TrashItem>().ToList();
            if (selected.Count == 0)
            {
                ConfirmDialog.ShowMessage(this, "No Selection",
                    "Check one or more items to restore.", "OK", _isDarkMode);
                return;
            }
            int ok = 0;
            foreach (var item in selected)
                if (await _trashBinService.RestoreItemAsync(item)) ok++;
            if (ok > 0) HasRestoredItems = true;
            LoadItems();
        }

        private async void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = TrashListBox.SelectedItems.Cast<TrashItem>().ToList();
            if (selected.Count == 0)
            {
                ConfirmDialog.ShowMessage(this, "No Selection",
                    "Check one or more items to delete.", "OK", _isDarkMode);
                return;
            }
            string msg = selected.Count == 1
                ? $"Permanently delete '{selected[0].Title}'?"
                : $"Permanently delete {selected.Count} selected items?";
            bool confirm = ConfirmDialog.ShowConfirm(this, "Delete Permanently",
                msg, "Delete", "Cancel", isDanger: true, isDarkMode: _isDarkMode);
            if (confirm)
            {
                foreach (var item in selected)
                    await _trashBinService.PermanentDeleteAsync(item);
                LoadItems();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = HasRestoredItems;
            Close();
        }
    }
}
