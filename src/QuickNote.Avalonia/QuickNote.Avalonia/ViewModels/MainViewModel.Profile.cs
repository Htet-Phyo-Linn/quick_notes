using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using QuickNote.Core.Interfaces;

namespace QuickNote.Avalonia.ViewModels
{
    // Profile switching (Work Profile OneDrive vs Personal OneDrive vs Local storage) and notes migration.
    public partial class MainViewModel
    {
        [RelayCommand]
        public void ShowProfileDialog()
        {
            AvailableProfiles.Clear();

            // 1. Detect OneDrive accounts (Work/Business vs Personal)
            var detected = _cloudSyncService.DetectOneDriveAccounts();
            var activePath = _notesRoot;

            foreach (var acc in detected)
            {
                var targetPath = acc.DefaultNotesPath;
                bool isActive = string.Equals(Path.GetFullPath(activePath).TrimEnd('\\', '/'),
                                              Path.GetFullPath(targetPath).TrimEnd('\\', '/'),
                                              StringComparison.OrdinalIgnoreCase);

                var profile = new ProfileItemViewModel
                {
                    DisplayName = acc.AccountName,
                    AccountType = acc.IsBusiness ? "Work Profile (Commercial)" : "Personal OneDrive",
                    Path = targetPath,
                    IsBusiness = acc.IsBusiness,
                    IsLocal = false,
                    IsActive = isActive,
                    OneDriveAccount = acc
                };
                AvailableProfiles.Add(profile);
            }

            // 2. Add Local Storage (My Documents\QuickNote)
            var localDocumentsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "QuickNote");

            bool isLocalActive = string.Equals(Path.GetFullPath(activePath).TrimEnd('\\', '/'),
                                               Path.GetFullPath(localDocumentsPath).TrimEnd('\\', '/'),
                                               StringComparison.OrdinalIgnoreCase);

            if (!AvailableProfiles.Any(p => string.Equals(Path.GetFullPath(p.Path).TrimEnd('\\', '/'),
                                                          Path.GetFullPath(localDocumentsPath).TrimEnd('\\', '/'),
                                                          StringComparison.OrdinalIgnoreCase)))
            {
                AvailableProfiles.Add(new ProfileItemViewModel
                {
                    DisplayName = "Local Storage (My Documents)",
                    AccountType = "Local (Offline)",
                    Path = localDocumentsPath,
                    IsBusiness = false,
                    IsLocal = true,
                    IsActive = isLocalActive,
                    OneDriveAccount = null
                });
            }

            SelectedProfile = AvailableProfiles.FirstOrDefault(p => p.IsActive) ?? AvailableProfiles.FirstOrDefault();
            MigrateNotesOnSwitch = false;
            IsProfileDialogVisible = true;
        }

        [RelayCommand]
        public void CancelProfileDialog()
        {
            IsProfileDialogVisible = false;
        }

        [RelayCommand]
        public async Task ConfirmProfileSwitch()
        {
            if (SelectedProfile == null)
            {
                IsProfileDialogVisible = false;
                return;
            }

            var target = SelectedProfile;
            var targetPath = target.Path;
            bool migrate = MigrateNotesOnSwitch;

            if (string.Equals(Path.GetFullPath(_notesRoot).TrimEnd('\\', '/'),
                              Path.GetFullPath(targetPath).TrimEnd('\\', '/'),
                              StringComparison.OrdinalIgnoreCase))
            {
                // Already on this profile
                IsProfileDialogVisible = false;
                return;
            }

            IsProfileDialogVisible = false;
            StatusText = $"Switching storage profile to: {target.DisplayName}...";

            try
            {
                // 1. Save any active unsaved note
                if (_isModified && CurrentNote != null)
                {
                    await SaveNoteAsync();
                }

                // 2. Switch Cloud Sync / Local storage & migrate if requested
                if (target.OneDriveAccount != null)
                {
                    await _cloudSyncService.EnableOneDriveSyncAsync(target.OneDriveAccount, migrate, _notesRoot);
                }
                else
                {
                    if (migrate && Directory.Exists(_notesRoot) &&
                        !string.Equals(Path.GetFullPath(_notesRoot).TrimEnd('\\', '/'),
                                       Path.GetFullPath(targetPath).TrimEnd('\\', '/'),
                                       StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Run(() => CopyDirectory(_notesRoot, targetPath));
                    }
                    await _cloudSyncService.DisableOneDriveSyncAsync(targetPath);
                }

                // 3. Update notesRoot in ViewModel and Settings
                _notesRoot = targetPath;
                _settingsService.Settings.NotesRoot = targetPath;
                await _settingsService.SaveSettingsAsync();

                // 4. Update core repositories and services
                _noteRepository.UpdateNotesRoot(targetPath);
                _fileWatcher.UpdateWatchDirectory(targetPath);
                _trashBinService.UpdateNotesRoot(targetPath);

                // 5. Clear active tab & reload workspace
                OpenTabs.Clear();
                ActiveTab = null;
                CurrentNote = null;
                EditorContent = string.Empty;
                NoteTitle = string.Empty;

                OnPropertyChanged(nameof(NotesRoot));
                await InitializeAsync();
                StatusText = $"Switched profile to: {target.DisplayName}";
            }
            catch (Exception ex)
            {
                StatusText = $"Failed to switch profile: {ex.Message}";
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            var source = new DirectoryInfo(sourceDir);
            var target = new DirectoryInfo(targetDir);
            Directory.CreateDirectory(target.FullName);

            foreach (var file in source.GetFiles())
            {
                if (file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                    file.Name.EndsWith(".draft", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (file.Name.StartsWith(".") && !string.Equals(source.Name, ".assets", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dest = Path.Combine(target.FullName, file.Name);
                if (!File.Exists(dest) || file.LastWriteTimeUtc > File.GetLastWriteTimeUtc(dest))
                {
                    file.CopyTo(dest, true);
                }
            }

            foreach (var subDir in source.GetDirectories())
            {
                if (subDir.Name.Equals(".trash", StringComparison.OrdinalIgnoreCase) ||
                    subDir.Name.Equals(".recovery", StringComparison.OrdinalIgnoreCase) ||
                    subDir.Name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    subDir.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    subDir.Name.Equals("obj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var nextTarget = target.CreateSubdirectory(subDir.Name);
                CopyDirectory(subDir.FullName, nextTarget.FullName);
            }
        }
    }
}
