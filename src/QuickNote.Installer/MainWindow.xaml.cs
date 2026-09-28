using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace QuickNote.Installer
{
    public partial class MainWindow : Window
    {
        private bool _isFinished = false;

        public MainWindow()
        {
            InitializeComponent();
            TxtDestination.Text = InstallerEngine.GetDefaultInstallDir();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select QuickNote Installation Directory",
                    InitialDirectory = TxtDestination.Text
                };

                if (dialog.ShowDialog() == true)
                {
                    TxtDestination.Text = Path.Combine(dialog.FolderName, "QuickNote");
                }
            }
            catch
            {
                // Fallback if dialog is unavailable
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_isFinished)
            {
                if (ChkLaunchAfter.IsChecked == true)
                {
                    string targetDir = TxtDestination.Text.Trim();
                    string exePath = Path.Combine(targetDir, "QuickNote.exe");
                    if (File.Exists(exePath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exePath,
                            WorkingDirectory = targetDir,
                            UseShellExecute = true
                        });
                    }
                }

                Close();
                return;
            }

            string installDir = TxtDestination.Text.Trim();
            if (string.IsNullOrWhiteSpace(installDir))
            {
                MessageBox.Show("Please specify a valid installation directory.", "Invalid Directory", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool createStartMenu = ChkStartMenu.IsChecked == true;
            bool createDesktop = ChkDesktop.IsChecked == true;

            BtnInstall.IsEnabled = false;
            BtnCancel.IsEnabled = false;
            BtnBrowse.IsEnabled = false;
            TxtDestination.IsEnabled = false;
            ChkStartMenu.IsEnabled = false;
            ChkDesktop.IsEnabled = false;
            ChkLaunchAfter.IsEnabled = false;

            PanelProgress.Visibility = Visibility.Visible;
            ProgressBarStatus.Value = 0;
            TxtStatus.Text = "Preparing installation...";

            try
            {
                await Task.Run(() =>
                {
                    InstallerEngine.KillRunningProcesses();

                    Dispatcher.Invoke(() => TxtStatus.Text = "Extracting application files...");
                    InstallerEngine.ExtractPayload(installDir, (pct, status) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ProgressBarStatus.Value = pct;
                            TxtStatus.Text = status;
                        });
                    });

                    string exePath = Path.Combine(installDir, "QuickNote.exe");
                    string iconPath = Path.Combine(installDir, "app.ico");
                    if (!File.Exists(iconPath)) iconPath = exePath;

                    if (createStartMenu)
                    {
                        Dispatcher.Invoke(() => TxtStatus.Text = "Creating Start Menu shortcut for Windows Search...");
                        InstallerEngine.CreateShortcut(
                            InstallerEngine.GetStartMenuShortcutPath(),
                            exePath,
                            installDir,
                            "QuickNote - Local-First Markdown Notes",
                            iconPath);
                    }

                    if (createDesktop)
                    {
                        Dispatcher.Invoke(() => TxtStatus.Text = "Creating Desktop shortcut...");
                        InstallerEngine.CreateShortcut(
                            InstallerEngine.GetDesktopShortcutPath(),
                            exePath,
                            installDir,
                            "QuickNote - Local-First Markdown Notes",
                            iconPath);
                    }

                    Dispatcher.Invoke(() => TxtStatus.Text = "Registering application in Windows...");
                    InstallerEngine.RegisterInRegistry(installDir);
                    InstallerEngine.CopySelfAsUninstaller(installDir);

                    Dispatcher.Invoke(() => TxtStatus.Text = "Updating Windows Search & Shell cache...");
                    InstallerEngine.NotifyShell();
                });

                _isFinished = true;
                ProgressBarStatus.Value = 100;
                TxtStatus.Text = "Installation finished successfully! QuickNote is ready.";
                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald green

                BtnInstall.Content = "Finish";
                BtnInstall.IsEnabled = true;
                BtnCancel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ProgressBarStatus.Value = 0;
                TxtStatus.Text = $"Installation failed: {ex.Message}";
                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)); // Red
                BtnCancel.IsEnabled = true;
                BtnInstall.IsEnabled = true;
                MessageBox.Show($"Failed to install QuickNote: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
