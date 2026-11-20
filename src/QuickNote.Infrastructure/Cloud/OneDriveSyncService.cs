using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
#if !ANDROID
using Microsoft.Win32;
#endif
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Core.Utilities;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.Infrastructure.Cloud
{
    public class OneDriveSyncService : ICloudSyncService
    {
        private readonly ISettingsService _settingsService;
        private readonly INoteRepository _noteRepository;
        private readonly IFileWatcher _fileWatcher;
        private readonly GoogleCloudSyncEngine _googleSyncEngine;
        private CloudSyncStatus _currentStatus = new CloudSyncStatus();

        public event EventHandler<CloudSyncStatus>? SyncStatusChanged;

        public bool IsAuthenticated => _settingsService.Settings.IsGoogleDriveSyncEnabled
            ? _googleSyncEngine.IsAuthenticated
            : true;

        public bool IsSyncing => _googleSyncEngine?.IsSyncing ?? false;

        public string? UserEmail => _settingsService.Settings.IsGoogleDriveSyncEnabled
            ? _googleSyncEngine.UserEmail
            : _settingsService.Settings.SelectedOneDriveAccountName;

        public string? UserName => _settingsService.Settings.IsGoogleDriveSyncEnabled
            ? _googleSyncEngine.UserName
            : null;

        public DateTime? LastSyncTime => _settingsService.Settings.IsGoogleDriveSyncEnabled
            ? _googleSyncEngine.LastSyncTime
            : _settingsService.Settings.LastCloudSyncTime;

        public OneDriveSyncService(
            ISettingsService settingsService,
            INoteRepository noteRepository,
            IFileWatcher fileWatcher)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _noteRepository = noteRepository ?? throw new ArgumentNullException(nameof(noteRepository));
            _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));

            _googleSyncEngine = new GoogleCloudSyncEngine(_noteRepository.NotesRoot, _settingsService);
            if (_settingsService.Settings.IsGoogleDriveSyncEnabled &&
                !string.IsNullOrWhiteSpace(_settingsService.Settings.GoogleDriveNotesPath) &&
                Directory.Exists(_settingsService.Settings.GoogleDriveNotesPath))
            {
                _googleSyncEngine.UpdateNotesRoot(_settingsService.Settings.GoogleDriveNotesPath);
            }
            _googleSyncEngine.SyncStatusChanged += (s, e) =>
            {
                if (_settingsService.Settings.IsGoogleDriveSyncEnabled)
                {
                    _currentStatus = e;
                    SyncStatusChanged?.Invoke(this, e);
                }
            };

            RefreshStatus();
        }

        public string GetAuthorizationUrl(out string codeVerifier)
        {
            return _googleSyncEngine.GetAuthorizationUrl(out codeVerifier);
        }

        public async Task<bool> CompleteSignInAsync(string code, string codeVerifier)
        {
            var success = await _googleSyncEngine.SignInWithCodeAsync(code, codeVerifier);
            RefreshStatus();
            return success;
        }

        public async Task SignOutAsync()
        {
            await _googleSyncEngine.SignOutAsync();
            RefreshStatus();
        }

        public async Task<SyncResult> TriggerSyncAsync()
        {
            if (_settingsService.Settings.IsGoogleDriveSyncEnabled)
            {
                return await _googleSyncEngine.PerformSyncAsync();
            }
            return SyncResult.Ok(0, 0, 0);
        }

        public List<OneDriveAccountInfo> DetectOneDriveAccounts()
        {
            var accounts = new List<OneDriveAccountInfo>();
            var registeredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

#if !ANDROID
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    // Scan Registry: HKCU\Software\Microsoft\OneDrive\Accounts
                    using var accountsKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
                    if (accountsKey != null)
                    {
                        foreach (var subKeyName in accountsKey.GetSubKeyNames())
                        {
                            using var subKey = accountsKey.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            var userFolder = subKey.GetValue("UserFolder") as string;
                            if (string.IsNullOrWhiteSpace(userFolder) || !Directory.Exists(userFolder)) continue;
                            if (registeredFolders.Contains(userFolder)) continue;

                            var displayName = subKey.GetValue("DisplayName") as string;
                            bool isBusiness = subKeyName.StartsWith("Business", StringComparison.OrdinalIgnoreCase);

                            if (string.IsNullOrWhiteSpace(displayName))
                            {
                                displayName = isBusiness ? Path.GetFileName(userFolder) : "OneDrive - Personal";
                            }

                            accounts.Add(new OneDriveAccountInfo
                            {
                                AccountName = displayName,
                                UserFolder = userFolder,
                                IsBusiness = isBusiness
                            });
                            registeredFolders.Add(userFolder);
                        }
                    }
                }
                catch { }
            }
#endif

            // Fallback: Check environment variables
            var envBusiness = Environment.GetEnvironmentVariable("OneDriveCommercial");
            if (!string.IsNullOrWhiteSpace(envBusiness) && Directory.Exists(envBusiness) && !registeredFolders.Contains(envBusiness))
            {
                accounts.Add(new OneDriveAccountInfo
                {
                    AccountName = Path.GetFileName(envBusiness),
                    UserFolder = envBusiness,
                    IsBusiness = true
                });
                registeredFolders.Add(envBusiness);
            }

            var envPersonal = Environment.GetEnvironmentVariable("OneDriveConsumer");
            if (!string.IsNullOrWhiteSpace(envPersonal) && Directory.Exists(envPersonal) && !registeredFolders.Contains(envPersonal))
            {
                accounts.Add(new OneDriveAccountInfo
                {
                    AccountName = "OneDrive - Personal",
                    UserFolder = envPersonal,
                    IsBusiness = false
                });
                registeredFolders.Add(envPersonal);
            }

            var envDefault = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(envDefault) && Directory.Exists(envDefault) && !registeredFolders.Contains(envDefault))
            {
                accounts.Add(new OneDriveAccountInfo
                {
                    AccountName = Path.GetFileName(envDefault),
                    UserFolder = envDefault,
                    IsBusiness = envDefault.Contains(" - ")
                });
                registeredFolders.Add(envDefault);
            }

            // Disk scan fallback: scan UserProfile directory for any "OneDrive*" directories
            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(userProfile) && Directory.Exists(userProfile))
                {
                    foreach (var dir in Directory.GetDirectories(userProfile, "OneDrive*"))
                    {
                        if (!registeredFolders.Contains(dir))
                        {
                            var folderName = Path.GetFileName(dir);
                            bool isBusiness = folderName.Contains(" - ");
                            accounts.Add(new OneDriveAccountInfo
                            {
                                AccountName = isBusiness ? folderName : "OneDrive - Personal",
                                UserFolder = dir,
                                IsBusiness = isBusiness
                            });
                            registeredFolders.Add(dir);
                        }
                    }
                }
            }
            catch { }

            return accounts;
        }

        public async Task<bool> EnableOneDriveSyncAsync(OneDriveAccountInfo account, bool migrateExistingNotes, string currentNotesRoot)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.UserFolder))
            {
                return false;
            }

            var oneDriveNotesPath = account.DefaultNotesPath;
            if (!Directory.Exists(oneDriveNotesPath))
            {
                Directory.CreateDirectory(oneDriveNotesPath);
            }

            // Migrate existing notes if requested and source exists
            if (migrateExistingNotes && Directory.Exists(currentNotesRoot) &&
                !string.Equals(currentNotesRoot, oneDriveNotesPath, StringComparison.OrdinalIgnoreCase))
            {
                await CopyDirectoryAsync(currentNotesRoot, oneDriveNotesPath);
            }

            // Update app settings
            _settingsService.Settings.IsOneDriveSyncEnabled = true;
            _settingsService.Settings.OneDriveNotesPath = oneDriveNotesPath;
            _settingsService.Settings.SelectedOneDriveAccountName = account.AccountName;
            _settingsService.Settings.NotesRoot = oneDriveNotesPath;
            await _settingsService.SaveSettingsAsync();

            // Re-point repository and watcher
            _noteRepository.UpdateNotesRoot(oneDriveNotesPath);
            _fileWatcher.UpdateWatchDirectory(oneDriveNotesPath);

            RefreshStatus();
            return true;
        }

        public async Task<bool> DisableOneDriveSyncAsync(string localDocumentsNotesRoot)
        {
            if (string.IsNullOrWhiteSpace(localDocumentsNotesRoot))
            {
                localDocumentsNotesRoot = StoragePathHelper.GetDefaultLocalNotesPath();
            }

            if (!Directory.Exists(localDocumentsNotesRoot))
            {
                Directory.CreateDirectory(localDocumentsNotesRoot);
            }

            _settingsService.Settings.IsOneDriveSyncEnabled = false;
            _settingsService.Settings.NotesRoot = localDocumentsNotesRoot;
            await _settingsService.SaveSettingsAsync();

            _noteRepository.UpdateNotesRoot(localDocumentsNotesRoot);
            _fileWatcher.UpdateWatchDirectory(localDocumentsNotesRoot);

            RefreshStatus();
            return true;
        }

        public async Task<bool> EnableGoogleDriveSyncAsync(string targetPath, bool migrateExistingNotes, string currentNotesRoot)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                targetPath = StoragePathHelper.GetDefaultGoogleDriveNotesPath();
            }

            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            if (migrateExistingNotes && Directory.Exists(currentNotesRoot) &&
                !string.Equals(Path.GetFullPath(currentNotesRoot).TrimEnd('\\', '/'),
                               Path.GetFullPath(targetPath).TrimEnd('\\', '/'),
                               StringComparison.OrdinalIgnoreCase))
            {
                await CopyDirectoryAsync(currentNotesRoot, targetPath);
            }

            _settingsService.Settings.IsGoogleDriveSyncEnabled = true;
            _settingsService.Settings.IsOneDriveSyncEnabled = false;
            _settingsService.Settings.GoogleDriveNotesPath = targetPath;
            _settingsService.Settings.NotesRoot = targetPath;
            await _settingsService.SaveSettingsAsync();

            _noteRepository.UpdateNotesRoot(targetPath);
            _fileWatcher.UpdateWatchDirectory(targetPath);
            _googleSyncEngine.UpdateNotesRoot(targetPath);

            RefreshStatus();
            return true;
        }

        public CloudSyncStatus GetSyncStatus()
        {
            RefreshStatus(raiseEvent: false);
            return _currentStatus;
        }

        private void RefreshStatus(bool raiseEvent = true)
        {
            bool isGoogle = _settingsService.Settings.IsGoogleDriveSyncEnabled;
            bool isEnabled = _settingsService.Settings.IsOneDriveSyncEnabled;
            string? activePath = _settingsService.Settings.OneDriveNotesPath;
            string? accountName = _settingsService.Settings.SelectedOneDriveAccountName;

            CloudSyncStatus newStatus;

            if (isGoogle)
            {
                var email = _settingsService.Settings.GoogleDriveUserEmail;
                var gPath = _settingsService.Settings.GoogleDriveNotesPath ?? _noteRepository.NotesRoot;
                newStatus = new CloudSyncStatus
                {
                    State = _googleSyncEngine.IsAuthenticated ? CloudSyncState.Synced : CloudSyncState.Warning,
                    Message = _googleSyncEngine.IsAuthenticated
                        ? $"Synced with Google Drive ({email ?? "Connected"})"
                        : "Google Drive: Sign in required",
                    ActiveNotesPath = gPath,
                    AccountName = email ?? "Google Drive",
                    LastSyncTime = _settingsService.Settings.LastGoogleDriveSyncTime
                };
            }
            else if (!isEnabled || string.IsNullOrWhiteSpace(activePath) || !Directory.Exists(activePath))
            {
                newStatus = new CloudSyncStatus
                {
                    State = CloudSyncState.Disabled,
                    Message = "Sync disabled",
                    ActiveNotesPath = null,
                    AccountName = null
                };
            }
            else
            {
                bool isOneDriveRunning = false;
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        isOneDriveRunning = Process.GetProcessesByName("OneDrive").Length > 0;
                    }
                }
                catch { }
                newStatus = new CloudSyncStatus
                {
                    State = isOneDriveRunning ? CloudSyncState.Synced : CloudSyncState.Warning,
                    Message = isOneDriveRunning ? $"Synced with {accountName ?? "OneDrive"}" : "OneDrive app paused/offline",
                    ActiveNotesPath = activePath,
                    AccountName = accountName,
                    LastSyncTime = DateTime.Now
                };
            }

            bool changed = _currentStatus.State != newStatus.State ||
                          _currentStatus.Message != newStatus.Message ||
                          _currentStatus.ActiveNotesPath != newStatus.ActiveNotesPath;

            _currentStatus = newStatus;

            if (changed && raiseEvent)
            {
                SyncStatusChanged?.Invoke(this, _currentStatus);
            }
        }

        private static async Task CopyDirectoryAsync(string sourceDir, string targetDir)
        {
            await Task.Run(() =>
            {
                var source = new DirectoryInfo(sourceDir);
                var target = new DirectoryInfo(targetDir);

                CopyAll(source, target);
            });
        }

        private static void CopyAll(DirectoryInfo source, DirectoryInfo target)
        {
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

                var destFile = Path.Combine(target.FullName, file.Name);
                if (!File.Exists(destFile) || file.LastWriteTimeUtc > File.GetLastWriteTimeUtc(destFile))
                {
                    file.CopyTo(destFile, true);
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

                var nextTargetSubDir = target.CreateSubdirectory(subDir.Name);
                CopyAll(subDir, nextTargetSubDir);
            }
        }
    }
}
