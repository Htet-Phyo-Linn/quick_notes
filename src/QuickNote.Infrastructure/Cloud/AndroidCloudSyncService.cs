using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Core.Utilities;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.Infrastructure.Cloud
{
    public class AndroidCloudSyncService : ICloudSyncService
    {
        private readonly ISettingsService _settingsService;
        private readonly INoteRepository _noteRepository;
        private readonly IFileWatcher _fileWatcher;
        private readonly GoogleCloudSyncEngine _syncEngine;
        private CloudSyncStatus _currentStatus = new();

        public event EventHandler<CloudSyncStatus>? SyncStatusChanged;

        public bool IsAuthenticated => _syncEngine.IsAuthenticated;
        public bool IsSyncing => _syncEngine.IsSyncing;
        public string? UserEmail => _syncEngine.UserEmail;
        public string? UserName => _syncEngine.UserName;
        public DateTime? LastSyncTime => _syncEngine.LastSyncTime;

        public AndroidCloudSyncService(
            ISettingsService settingsService,
            INoteRepository noteRepository,
            IFileWatcher fileWatcher)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _noteRepository = noteRepository ?? throw new ArgumentNullException(nameof(noteRepository));
            _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));

            _syncEngine = new GoogleCloudSyncEngine(_noteRepository.NotesRoot, _settingsService);
            _syncEngine.SyncStatusChanged += OnEngineSyncStatusChanged;

            RefreshStatus();
        }

        private void OnEngineSyncStatusChanged(object? sender, CloudSyncStatus e)
        {
            _currentStatus = e;
            SyncStatusChanged?.Invoke(this, e);
        }

        public string GetAuthorizationUrl(out string codeVerifier)
        {
            return _syncEngine.GetAuthorizationUrl(out codeVerifier);
        }

        public async Task<bool> CompleteSignInAsync(string code, string codeVerifier)
        {
            var success = await _syncEngine.SignInWithCodeAsync(code, codeVerifier);
            RefreshStatus();
            return success;
        }

        public async Task SignOutAsync()
        {
            await _syncEngine.SignOutAsync();
            RefreshStatus();
        }

        public async Task<SyncResult> TriggerSyncAsync()
        {
            if (!_syncEngine.IsAuthenticated)
            {
                return SyncResult.Fail("Not signed in to Google Drive");
            }
            return await _syncEngine.PerformSyncAsync();
        }

        public List<OneDriveAccountInfo> DetectOneDriveAccounts()
        {
            var accounts = new List<OneDriveAccountInfo>();
            if (_syncEngine.IsAuthenticated)
            {
                accounts.Add(new OneDriveAccountInfo
                {
                    AccountName = _syncEngine.UserEmail ?? "Connected Google Account",
                    UserFolder = _noteRepository.NotesRoot,
                    IsBusiness = false
                });
            }
            return accounts;
        }

        public async Task<bool> EnableOneDriveSyncAsync(OneDriveAccountInfo account, bool migrateExistingNotes, string currentNotesRoot)
        {
            _settingsService.Settings.IsGoogleDriveSyncEnabled = true;
            await _settingsService.SaveSettingsAsync();
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
                await Task.Run(() => CopyDirectory(currentNotesRoot, targetPath));
            }

            _settingsService.Settings.IsGoogleDriveSyncEnabled = true;
            _settingsService.Settings.GoogleDriveNotesPath = targetPath;
            _settingsService.Settings.NotesRoot = targetPath;
            await _settingsService.SaveSettingsAsync();

            _noteRepository.UpdateNotesRoot(targetPath);
            _fileWatcher.UpdateWatchDirectory(targetPath);

            RefreshStatus();
            return true;
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
                    continue;

                if (file.Name.StartsWith(".") && !string.Equals(source.Name, ".assets", StringComparison.OrdinalIgnoreCase))
                    continue;

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
                    continue;

                var nextTarget = target.CreateSubdirectory(subDir.Name);
                CopyDirectory(subDir.FullName, nextTarget.FullName);
            }
        }

        public async Task<bool> DisableOneDriveSyncAsync(string localDocumentsNotesRoot)
        {
            await SignOutAsync();
            return true;
        }

        public CloudSyncStatus GetSyncStatus()
        {
            RefreshStatus(raiseEvent: false);
            return _currentStatus;
        }

        private void RefreshStatus(bool raiseEvent = true)
        {
            CloudSyncStatus newStatus;

            if (!_syncEngine.IsAuthenticated)
            {
                newStatus = new CloudSyncStatus
                {
                    State = CloudSyncState.Disabled,
                    Message = "Google Drive: Connect",
                    ActiveNotesPath = _noteRepository.NotesRoot,
                    AccountName = null
                };
            }
            else
            {
                newStatus = new CloudSyncStatus
                {
                    State = CloudSyncState.Synced,
                    Message = $"Google Drive: {_syncEngine.UserEmail ?? "Connected"}",
                    ActiveNotesPath = _noteRepository.NotesRoot,
                    AccountName = _syncEngine.UserEmail,
                    LastSyncTime = _syncEngine.LastSyncTime
                };
            }

            bool changed = _currentStatus.State != newStatus.State ||
                           _currentStatus.Message != newStatus.Message;

            _currentStatus = newStatus;

            if (changed && raiseEvent)
            {
                SyncStatusChanged?.Invoke(this, _currentStatus);
            }
        }
    }
}
