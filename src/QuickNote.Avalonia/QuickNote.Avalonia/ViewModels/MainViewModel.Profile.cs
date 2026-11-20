using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Utilities;

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
                bool isActive = !_settingsService.Settings.IsGoogleDriveSyncEnabled &&
                                string.Equals(Path.GetFullPath(activePath).TrimEnd('\\', '/'),
                                              Path.GetFullPath(targetPath).TrimEnd('\\', '/'),
                                              StringComparison.OrdinalIgnoreCase);

                var profile = new ProfileItemViewModel
                {
                    DisplayName = acc.AccountName,
                    AccountType = acc.IsBusiness ? "Work Profile (Commercial)" : "Personal OneDrive",
                    Path = targetPath,
                    IsBusiness = acc.IsBusiness,
                    IsLocal = false,
                    IsGoogleDrive = false,
                    IsActive = isActive,
                    OneDriveAccount = acc
                };
                AvailableProfiles.Add(profile);
            }

            // 2. Add Google Drive Cloud Storage (Requirement 2)
            var defaultGoogleDrivePath = !string.IsNullOrWhiteSpace(_settingsService.Settings.GoogleDriveNotesPath)
                && !_settingsService.Settings.GoogleDriveNotesPath.Contains("OneDrive", StringComparison.OrdinalIgnoreCase)
                ? _settingsService.Settings.GoogleDriveNotesPath
                : StoragePathHelper.GetDefaultGoogleDriveNotesPath();

            bool isGoogleActive = _settingsService.Settings.IsGoogleDriveSyncEnabled ||
                                  string.Equals(Path.GetFullPath(activePath).TrimEnd('\\', '/'),
                                                Path.GetFullPath(defaultGoogleDrivePath).TrimEnd('\\', '/'),
                                                StringComparison.OrdinalIgnoreCase);

            var googleEmail = _settingsService.Settings.GoogleDriveUserEmail;
            var googleDisplay = !string.IsNullOrWhiteSpace(googleEmail)
                ? $"Google Drive ({googleEmail})"
                : (_cloudSyncService.IsAuthenticated ? "Google Drive (Connected)" : "Google Drive");

            AvailableProfiles.Add(new ProfileItemViewModel
            {
                DisplayName = googleDisplay,
                AccountType = "Google Drive (Cloud Sync)",
                Path = defaultGoogleDrivePath,
                IsBusiness = false,
                IsLocal = false,
                IsGoogleDrive = true,
                IsActive = isGoogleActive,
                IsSignedIn = _cloudSyncService.IsAuthenticated,
                OneDriveAccount = null
            });

            // 3. Add Local Storage (Documents\QuickNote-Local)
            var localDocumentsPath = StoragePathHelper.GetDefaultLocalNotesPath();

            bool isLocalActive = !_settingsService.Settings.IsGoogleDriveSyncEnabled &&
                                 string.Equals(Path.GetFullPath(activePath).TrimEnd('\\', '/'),
                                               Path.GetFullPath(localDocumentsPath).TrimEnd('\\', '/'),
                                               StringComparison.OrdinalIgnoreCase);

            if (!AvailableProfiles.Any(p => string.Equals(Path.GetFullPath(p.Path).TrimEnd('\\', '/'),
                                                          Path.GetFullPath(localDocumentsPath).TrimEnd('\\', '/'),
                                                          StringComparison.OrdinalIgnoreCase)))
            {
                AvailableProfiles.Add(new ProfileItemViewModel
                {
                    DisplayName = "Local Storage (Documents)",
                    AccountType = "Local (Offline)",
                    Path = localDocumentsPath,
                    IsBusiness = false,
                    IsLocal = true,
                    IsGoogleDrive = false,
                    IsActive = isLocalActive,
                    OneDriveAccount = null
                });
            }

            SelectedProfile = AvailableProfiles.FirstOrDefault(p => p.IsActive) ?? AvailableProfiles.FirstOrDefault();
            MigrateNotesOnSwitch = false;
            ProfileDialogTabIndex = 1;
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
                bool isProviderChanging = (target.IsGoogleDrive && !_settingsService.Settings.IsGoogleDriveSyncEnabled) ||
                                          (target.OneDriveAccount != null && !_settingsService.Settings.IsOneDriveSyncEnabled) ||
                                          (target.IsLocal && (_settingsService.Settings.IsGoogleDriveSyncEnabled || _settingsService.Settings.IsOneDriveSyncEnabled));

                if (!isProviderChanging)
                {
                    // Already on this profile and provider
                    IsProfileDialogVisible = false;
                    return;
                }
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
                if (target.IsGoogleDrive)
                {
                    if (migrate && Directory.Exists(_notesRoot) &&
                        !string.Equals(Path.GetFullPath(_notesRoot).TrimEnd('\\', '/'),
                                       Path.GetFullPath(targetPath).TrimEnd('\\', '/'),
                                       StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Run(() => CopyDirectory(_notesRoot, targetPath));
                    }

                    _settingsService.Settings.IsGoogleDriveSyncEnabled = true;
                    _settingsService.Settings.IsOneDriveSyncEnabled = false;
                    _settingsService.Settings.GoogleDriveNotesPath = targetPath;
                    await _cloudSyncService.EnableGoogleDriveSyncAsync(targetPath, false, _notesRoot);
                    if (_cloudSyncService.IsAuthenticated)
                    {
                        _ = _cloudSyncService.TriggerSyncAsync();
                    }
                    else
                    {
                        OpenGoogleDriveLogin();
                    }
                }
                else if (target.OneDriveAccount != null)
                {
                    if (migrate && Directory.Exists(_notesRoot) &&
                        !string.Equals(Path.GetFullPath(_notesRoot).TrimEnd('\\', '/'),
                                       Path.GetFullPath(targetPath).TrimEnd('\\', '/'),
                                       StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Run(() => CopyDirectory(_notesRoot, targetPath));
                    }

                    _settingsService.Settings.IsGoogleDriveSyncEnabled = false;
                    await _cloudSyncService.EnableOneDriveSyncAsync(target.OneDriveAccount, false, _notesRoot);
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

                    _settingsService.Settings.IsGoogleDriveSyncEnabled = false;
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
                _backupService.UpdateNotesRoot(targetPath);

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

                if (subDir.Name.Equals("QuickNote", StringComparison.OrdinalIgnoreCase))
                {
                    // Prevent recursive nesting of QuickNote inside QuickNote.
                    // Copy children directly into targetDir instead of creating a nested QuickNote folder.
                    CopyDirectory(subDir.FullName, targetDir);
                    continue;
                }

                var nextTarget = target.CreateSubdirectory(subDir.Name);
                CopyDirectory(subDir.FullName, nextTarget.FullName);
            }
        }
    }
}
