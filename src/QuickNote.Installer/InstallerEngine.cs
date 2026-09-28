using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace QuickNote.Installer
{
    public static class InstallerEngine
    {
        private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "quicknote_setup.log");

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0;

        public static string GetDefaultInstallDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "QuickNote");
        }

        public static string GetStartMenuShortcutPath()
        {
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            return Path.Combine(startMenu, "QuickNote.lnk");
        }

        public static string GetDesktopShortcutPath()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Path.Combine(desktop, "QuickNote.lnk");
        }

        public static void ExtractPayload(string targetDir, Action<int, string>? progress = null)
        {
            Log($"ExtractPayload starting to: {targetDir}");
            Directory.CreateDirectory(targetDir);

            var assembly = typeof(InstallerEngine).Assembly;
            var resourceNames = assembly.GetManifestResourceNames();
            Log($"Manifest resources found: {string.Join(", ", resourceNames)}");

            using var stream = assembly.GetManifestResourceStream("QuickNote.Installer.Resources.payload.zip");
            if (stream == null)
            {
                throw new FileNotFoundException($"Embedded payload.zip was not found. Available: {string.Join(", ", resourceNames)}");
            }

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            int total = archive.Entries.Count;
            int current = 0;

            foreach (var entry in archive.Entries)
            {
                current++;
                if (string.IsNullOrEmpty(entry.Name)) // Directory entry
                {
                    continue;
                }

                string destinationPath = Path.Combine(targetDir, entry.FullName);
                string? dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                entry.ExtractToFile(destinationPath, overwrite: true);
                int pct = (int)((double)current / total * 100);
                progress?.Invoke(pct, $"Extracting {entry.Name}...");
            }
            Log($"ExtractPayload completed. Extracted {total} entries.");
        }

        public static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description, string iconPath)
        {
            try
            {
                Log($"CreateShortcut: {shortcutPath} -> {targetPath}");
                string? dir = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = workingDir;
                    shortcut.Description = description;
                    shortcut.IconLocation = $"{iconPath},0";
                    shortcut.Save();
                    Log($"Shortcut saved successfully: {shortcutPath}");
                }
                else
                {
                    Log("WScript.Shell ProgID was not found.");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to create shortcut at {shortcutPath}: {ex}");
            }
        }

        public static void RegisterInRegistry(string installDir)
        {
            try
            {
                Log($"RegisterInRegistry starting for: {installDir}");
                string exePath = Path.Combine(installDir, "QuickNote.exe");
                string uninstallerPath = Path.Combine(installDir, "Uninstall.exe");

                // App Paths: allows launching via Win+R "QuickNote.exe" or "QuickNote"
                using (var appPathKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\QuickNote.exe"))
                {
                    appPathKey.SetValue(null, exePath);
                    appPathKey.SetValue("Path", installDir);
                }

                // Add or Remove Programs registration
                using (var uninstallKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickNote"))
                {
                    uninstallKey.SetValue("DisplayName", "QuickNote");
                    uninstallKey.SetValue("DisplayVersion", "1.0.0");
                    uninstallKey.SetValue("Publisher", "QuickNote");
                    uninstallKey.SetValue("DisplayIcon", $"{exePath},0");
                    uninstallKey.SetValue("InstallLocation", installDir);
                    uninstallKey.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
                    uninstallKey.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall --silent");
                    uninstallKey.SetValue("EstimatedSize", 3000, RegistryValueKind.DWord);
                    uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    uninstallKey.SetValue("HelpLink", "https://github.com/");
                    uninstallKey.SetValue("URLInfoAbout", "https://github.com/");
                }
                Log("Registry keys created successfully.");
            }
            catch (Exception ex)
            {
                Log($"Failed to register in registry: {ex}");
            }
        }

        public static void UnregisterFromRegistry()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickNote", false);
            }
            catch { }

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\App Paths\QuickNote.exe", false);
            }
            catch { }
            Log("Registry unregistration completed.");
        }

        public static void CopySelfAsUninstaller(string installDir)
        {
            try
            {
                string currentExe = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
                {
                    string dest = Path.Combine(installDir, "Uninstall.exe");
                    if (!string.Equals(currentExe, dest, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(currentExe, dest, true);
                        Log($"Copied self as uninstaller: {dest}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to copy uninstaller: {ex}");
            }
        }

        public static void NotifyShell()
        {
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                Log("SHChangeNotify called.");
            }
            catch (Exception ex)
            {
                Log($"SHChangeNotify error: {ex}");
            }
        }

        public static void KillRunningProcesses()
        {
            try
            {
                var names = new[] { "QuickNote", "QuickNote.Desktop", "QuickNote.App" };
                foreach (var name in names)
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { p.Kill(); p.WaitForExit(2000); Log($"Killed process {p.Id} ({name})"); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
