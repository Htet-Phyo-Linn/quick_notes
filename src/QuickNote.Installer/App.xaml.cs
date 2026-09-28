using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace QuickNote.Installer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            InstallerEngine.Log($"OnStartup args: {string.Join(" ", e.Args)}");

            bool isUninstall = e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) || 
                                               a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) ||
                                               a.Equals("-uninstall", StringComparison.OrdinalIgnoreCase));
            
            bool isSilent = e.Args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase) || 
                                           a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                                           a.Equals("-silent", StringComparison.OrdinalIgnoreCase) ||
                                           a.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                                           a.Equals("-s", StringComparison.OrdinalIgnoreCase));

            if (isUninstall)
            {
                HandleUninstall(isSilent);
                return;
            }

            if (isSilent)
            {
                HandleSilentInstall();
                return;
            }

            InstallerEngine.Log("Opening MainWindow GUI...");
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private void HandleSilentInstall()
        {
            try
            {
                InstallerEngine.Log("Starting silent installation...");
                string targetDir = InstallerEngine.GetDefaultInstallDir();
                InstallerEngine.KillRunningProcesses();
                InstallerEngine.ExtractPayload(targetDir);

                string exePath = Path.Combine(targetDir, "QuickNote.exe");
                string iconPath = Path.Combine(targetDir, "app.ico");
                if (!File.Exists(iconPath)) iconPath = exePath;

                InstallerEngine.CreateShortcut(InstallerEngine.GetStartMenuShortcutPath(), exePath, targetDir, "QuickNote - Local-First Markdown Notes", iconPath);
                InstallerEngine.CreateShortcut(InstallerEngine.GetDesktopShortcutPath(), exePath, targetDir, "QuickNote - Local-First Markdown Notes", iconPath);
                InstallerEngine.RegisterInRegistry(targetDir);
                InstallerEngine.CopySelfAsUninstaller(targetDir);
                InstallerEngine.NotifyShell();
                InstallerEngine.Log("Silent installation finished successfully.");
            }
            catch (Exception ex)
            {
                InstallerEngine.Log($"Silent install error: {ex}");
            }
            finally
            {
                Shutdown(0);
            }
        }

        private void HandleUninstall(bool isSilent)
        {
            InstallerEngine.Log($"HandleUninstall starting (silent={isSilent})");
            if (!isSilent)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to completely remove QuickNote and all its shortcuts?",
                    "Uninstall QuickNote",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    Shutdown(0);
                    return;
                }
            }

            try
            {
                InstallerEngine.KillRunningProcesses();

                // Remove shortcuts
                try { File.Delete(InstallerEngine.GetStartMenuShortcutPath()); InstallerEngine.Log("Deleted start menu shortcut."); } catch { }
                try { File.Delete(InstallerEngine.GetDesktopShortcutPath()); InstallerEngine.Log("Deleted desktop shortcut."); } catch { }

                // Unregister
                InstallerEngine.UnregisterFromRegistry();

                // Notify shell so Windows Search removes it immediately
                InstallerEngine.NotifyShell();

                string currentExe = Environment.ProcessPath ?? "";
                string installDir = Path.GetDirectoryName(currentExe) ?? InstallerEngine.GetDefaultInstallDir();

                // If running from within installDir, schedule directory removal after this process exits
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 2 /nobreak > NUL & rd /s /q \"{installDir}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                InstallerEngine.Log($"Scheduled directory removal: {installDir}");

                if (!isSilent)
                {
                    MessageBox.Show(
                        "QuickNote has been successfully uninstalled from your computer.",
                        "Uninstall QuickNote",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                InstallerEngine.Log($"Uninstall error: {ex}");
                if (!isSilent)
                {
                    MessageBox.Show(
                        $"Error during uninstallation: {ex.Message}",
                        "Uninstall Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                Shutdown(0);
            }
        }
    }
}
