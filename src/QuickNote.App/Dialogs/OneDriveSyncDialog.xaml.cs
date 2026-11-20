using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using QuickNote.Core.Interfaces;

namespace QuickNote.App.Dialogs
{
    public partial class OneDriveSyncDialog : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private readonly ICloudSyncService _cloudSyncService;
        private readonly ISettingsService _settingsService;
        private readonly string _currentNotesRoot;
        private readonly bool _isDarkMode;
        private List<OneDriveAccountInfo> _detectedAccounts = new List<OneDriveAccountInfo>();

        public bool SyncSettingsChanged { get; private set; }

        public OneDriveSyncDialog(
            ICloudSyncService cloudSyncService,
            ISettingsService settingsService,
            string currentNotesRoot,
            bool isDarkMode)
        {
            _cloudSyncService = cloudSyncService ?? throw new ArgumentNullException(nameof(cloudSyncService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _currentNotesRoot = currentNotesRoot ?? string.Empty;
            _isDarkMode = isDarkMode;

            InitializeComponent();

            SourceInitialized += (s, e) =>
            {
                try
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        int darkMode = isDarkMode ? 1 : 0;
                        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int)) != 0)
                        {
                            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
                        }
                    }
                }
                catch { }
            };

            ApplyTheme(isDarkMode);
            LoadStatusAndAccounts();
        }

        private void ApplyTheme(bool isDarkMode)
        {
            if (!isDarkMode)
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                HeaderTitle.Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                HeaderSub.Foreground = new SolidColorBrush(Color.FromRgb(0x78, 0x77, 0x74));

                StatusBanner.Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF5));
                StatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xE8));
                StatusTitle.Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                StatusDetail.Foreground = new SolidColorBrush(Color.FromRgb(0x78, 0x77, 0x74));

                SelectionBorder.Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF5));
                SelectionBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xE8));
                SelectionLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x78, 0x77, 0x74));
                AccountsListBox.Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));

                MigrateCheckbox.Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                DisableSyncButton.Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF5));
                DisableSyncButton.Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                DisableSyncButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xD9));

                CancelButton.Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF5));
                CancelButton.Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                CancelButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xD9));
            }
        }

        private void LoadStatusAndAccounts()
        {
            var status = _cloudSyncService.GetSyncStatus();
            if (status.State == CloudSyncState.Synced)
            {
                StatusTitle.Text = $"Status: Active ({status.AccountName})";
                StatusDetail.Text = $"Syncing folder: {status.ActiveNotesPath}";
                StatusDetail.ToolTip = status.ActiveNotesPath;
                DisableSyncButton.Visibility = Visibility.Visible;
                EnableSyncButton.Content = "Switch Account";
            }
            else
            {
                StatusTitle.Text = "Status: Sync Disabled";
                StatusDetail.Text = $"Current notes folder: {_currentNotesRoot}";
                StatusDetail.ToolTip = _currentNotesRoot;
                DisableSyncButton.Visibility = Visibility.Collapsed;
                EnableSyncButton.Content = "Enable Sync";
            }

            _detectedAccounts = _cloudSyncService.DetectOneDriveAccounts();
            AccountsListBox.Items.Clear();

            var textPrimaryBrush = _isDarkMode 
                ? new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6))
                : new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
            var textMutedBrush = _isDarkMode 
                ? new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C))
                : new SolidColorBrush(Color.FromRgb(0x78, 0x77, 0x74));
            var iconBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));

            foreach (var acc in _detectedAccounts)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Vector Icon
                var geomKey = acc.IsBusiness ? "IconBuildingGeometry" : "IconUserGeometry";
                var path = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource(geomKey),
                    Fill = iconBrush
                };
                var viewbox = new Viewbox
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 2, 10, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = path
                };
                Grid.SetColumn(viewbox, 0);

                // Text Info StackPanel
                var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var nameBlock = new TextBlock
                {
                    Text = acc.AccountName,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    Foreground = textPrimaryBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    ToolTip = acc.AccountName
                };
                var pathBlock = new TextBlock
                {
                    Text = acc.DefaultNotesPath,
                    FontSize = 11,
                    Foreground = textMutedBrush,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    ToolTip = acc.DefaultNotesPath
                };
                infoPanel.Children.Add(nameBlock);
                infoPanel.Children.Add(pathBlock);
                Grid.SetColumn(infoPanel, 1);

                grid.Children.Add(viewbox);
                grid.Children.Add(infoPanel);

                var item = new ListBoxItem
                {
                    Content = grid,
                    Tag = acc,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };

                if (!_settingsService.Settings.IsGoogleDriveSyncEnabled &&
                    string.Equals(_settingsService.Settings.OneDriveNotesPath, acc.DefaultNotesPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.IsSelected = true;
                }

                AccountsListBox.Items.Add(item);
            }

            // Add Google Drive Account Option (Requirement 2)
            var googleDrivePath = !string.IsNullOrWhiteSpace(_settingsService.Settings.GoogleDriveNotesPath)
                ? _settingsService.Settings.GoogleDriveNotesPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QuickNote-GoogleDrive");

            var googleEmail = _settingsService.Settings.GoogleDriveUserEmail;
            var googleDisplay = !string.IsNullOrWhiteSpace(googleEmail)
                ? $"Google Drive ({googleEmail})"
                : (_cloudSyncService.IsAuthenticated ? "Google Drive (Connected)" : "Google Drive (Cloud Sync)");

            var gGrid = new Grid();
            gGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            gGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var gPath = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconCloudGeometry"),
                Fill = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8))
            };
            var gViewbox = new Viewbox
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 2, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = gPath
            };
            Grid.SetColumn(gViewbox, 0);

            var gInfoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var gNameBlock = new TextBlock
            {
                Text = googleDisplay,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = textPrimaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                ToolTip = googleDisplay
            };
            var gPathBlock = new TextBlock
            {
                Text = googleDrivePath,
                FontSize = 11,
                Foreground = textMutedBrush,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                ToolTip = googleDrivePath
            };
            gInfoPanel.Children.Add(gNameBlock);
            gInfoPanel.Children.Add(gPathBlock);
            Grid.SetColumn(gInfoPanel, 1);

            gGrid.Children.Add(gViewbox);
            gGrid.Children.Add(gInfoPanel);

            var gItem = new ListBoxItem
            {
                Content = gGrid,
                Tag = "GOOGLE_DRIVE",
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            if (_settingsService.Settings.IsGoogleDriveSyncEnabled)
            {
                gItem.IsSelected = true;
            }

            AccountsListBox.Items.Add(gItem);

            if (AccountsListBox.SelectedItem == null && AccountsListBox.Items.Count > 0)
            {
                AccountsListBox.SelectedIndex = 0;
            }
        }

        private async void EnableSyncButton_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsListBox.SelectedItem is ListBoxItem selectedItem)
            {
                bool migrate = MigrateCheckbox.IsChecked == true;
                if (selectedItem.Tag is string tag && tag == "GOOGLE_DRIVE")
                {
                    EnableSyncButton.IsEnabled = false;
                    EnableSyncButton.Content = "Configuring...";
                    try
                    {
                        var googleDrivePath = !string.IsNullOrWhiteSpace(_settingsService.Settings.GoogleDriveNotesPath)
                            ? _settingsService.Settings.GoogleDriveNotesPath
                            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QuickNote-GoogleDrive");

                        var success = await _cloudSyncService.EnableGoogleDriveSyncAsync(googleDrivePath, migrate, _currentNotesRoot);
                        if (success)
                        {
                            SyncSettingsChanged = true;
                            if (_cloudSyncService.IsAuthenticated)
                            {
                                _ = _cloudSyncService.TriggerSyncAsync();
                            }
                            ConfirmDialog.ShowMessage(
                                this,
                                "Google Drive Cloud Sync",
                                migrate
                                    ? "Google Drive sync is enabled!\n\nExisting notes and attachments have been copied to:\n" + googleDrivePath
                                    : "Google Drive sync is enabled!\n\nSynchronized with:\n" + googleDrivePath,
                                "OK",
                                _isDarkMode);
                            DialogResult = true;
                            Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        ConfirmDialog.ShowMessage(this, "Sync Error", $"Failed to configure Google Drive sync:\n{ex.Message}", "OK", _isDarkMode);
                    }
                    finally
                    {
                        EnableSyncButton.IsEnabled = true;
                        EnableSyncButton.Content = "Enable Sync";
                    }
                    return;
                }

                if (selectedItem.Tag is OneDriveAccountInfo account)
                {
                    EnableSyncButton.IsEnabled = false;
                    EnableSyncButton.Content = "Configuring...";

                    try
                    {
                        _settingsService.Settings.IsGoogleDriveSyncEnabled = false;
                        var success = await _cloudSyncService.EnableOneDriveSyncAsync(account, migrate, _currentNotesRoot);
                        if (success)
                        {
                            SyncSettingsChanged = true;
                            ConfirmDialog.ShowMessage(
                                this,
                                "OneDrive Cloud Sync",
                                $"OneDrive Synchronization has been enabled!\n\nYour notes are now synchronized with:\n{account.DefaultNotesPath}",
                                "OK",
                                _isDarkMode);
                            DialogResult = true;
                            Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        ConfirmDialog.ShowMessage(this, "Sync Error", $"Failed to configure OneDrive sync:\n{ex.Message}", "OK", _isDarkMode);
                    }
                    finally
                    {
                        EnableSyncButton.IsEnabled = true;
                        EnableSyncButton.Content = "Enable Sync";
                    }
                    return;
                }
            }

            ConfirmDialog.ShowMessage(this, "Select Account", "Please select a storage account or profile from the list.", "OK", _isDarkMode);
        }

        private async void DisableSyncButton_Click(object sender, RoutedEventArgs e)
        {
            var confirm = ConfirmDialog.ShowConfirm(
                this,
                "Disconnect Cloud Sync",
                "Are you sure you want to disconnect cloud synchronization?\n\nYour notes will switch back to the local Documents\\QuickNote directory.",
                "Disconnect",
                "Cancel",
                isDanger: true,
                isDarkMode: _isDarkMode);

            if (confirm)
            {
                _settingsService.Settings.IsGoogleDriveSyncEnabled = false;
                var defaultDocs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QuickNote");
                await _cloudSyncService.DisableOneDriveSyncAsync(defaultDocs);
                SyncSettingsChanged = true;
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
