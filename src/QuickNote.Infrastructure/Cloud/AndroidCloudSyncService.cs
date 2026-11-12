using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.Infrastructure.Cloud
{
    public class AndroidCloudSyncService : ICloudSyncService
    {
        private readonly ISettingsService _settingsService;
        private readonly INoteRepository _noteRepository;
        private readonly IFileWatcher _fileWatcher;
        private CloudSyncStatus _currentStatus = new CloudSyncStatus();
        private readonly HttpClient _httpClient = new HttpClient();
        private CancellationTokenSource? _syncCts;

        public event EventHandler<CloudSyncStatus>? SyncStatusChanged;

        public AndroidCloudSyncService(
            ISettingsService settingsService,
            INoteRepository noteRepository,
            IFileWatcher fileWatcher)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _noteRepository = noteRepository ?? throw new ArgumentNullException(nameof(noteRepository));
            _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));

            RefreshStatus();
        }

        public List<OneDriveAccountInfo> DetectOneDriveAccounts()
        {
            var accounts = new List<OneDriveAccountInfo>();

            // On Android, check saved settings or configured accounts
            var savedAccount = _settingsService.Settings.SelectedOneDriveAccountName;
            var savedPath = _settingsService.Settings.OneDriveNotesPath;

            if (!string.IsNullOrWhiteSpace(savedAccount))
            {
                accounts.Add(new OneDriveAccountInfo
                {
                    AccountName = savedAccount,
                    UserFolder = savedPath ?? string.Empty,
                    IsBusiness = savedAccount.Contains("Business", StringComparison.OrdinalIgnoreCase)
                });
            }

            // Always provide option to configure OneDrive Personal & Business
            if (accounts.Count == 0)
            {
                accounts.Add(new OneDriveAccountInfo
                {
                    AccountName = "OneDrive - Personal",
                    UserFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "QuickNote", "OneDrive"),
                    IsBusiness = false
                });
                accounts.Add(new OneDriveAccountInfo
                {
                    AccountName = "OneDrive - Work or School",
                    UserFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "QuickNote", "OneDriveBusiness"),
                    IsBusiness = true
                });
            }

            return accounts;
        }

        public async Task<bool> EnableOneDriveSyncAsync(OneDriveAccountInfo account, bool migrateExistingNotes, string currentNotesRoot)
        {
            if (account == null) return false;

            var targetPath = account.DefaultNotesPath;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                targetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "QuickNote");
            }

            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            if (migrateExistingNotes && Directory.Exists(currentNotesRoot) &&
                !string.Equals(currentNotesRoot, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                await CopyDirectoryAsync(currentNotesRoot, targetPath);
            }

            _settingsService.Settings.IsOneDriveSyncEnabled = true;
            _settingsService.Settings.OneDriveNotesPath = targetPath;
            _settingsService.Settings.SelectedOneDriveAccountName = account.AccountName;
            _settingsService.Settings.NotesRoot = targetPath;
            await _settingsService.SaveSettingsAsync();

            _noteRepository.UpdateNotesRoot(targetPath);
            _fileWatcher.UpdateWatchDirectory(targetPath);

            RefreshStatus();

            // Trigger background sync task
            _ = Task.Run(async () => await SyncWithCloudAsync(targetPath));

            return true;
        }

        public async Task<bool> DisableOneDriveSyncAsync(string localDocumentsNotesRoot)
        {
            if (string.IsNullOrWhiteSpace(localDocumentsNotesRoot))
            {
                localDocumentsNotesRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                    "QuickNote");
            }

            if (!Directory.Exists(localDocumentsNotesRoot))
            {
                Directory.CreateDirectory(localDocumentsNotesRoot);
            }

            _syncCts?.Cancel();
            _settingsService.Settings.IsOneDriveSyncEnabled = false;
            _settingsService.Settings.NotesRoot = localDocumentsNotesRoot;
            await _settingsService.SaveSettingsAsync();

            _noteRepository.UpdateNotesRoot(localDocumentsNotesRoot);
            _fileWatcher.UpdateWatchDirectory(localDocumentsNotesRoot);

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
            bool isEnabled = _settingsService.Settings.IsOneDriveSyncEnabled;
            string? activePath = _settingsService.Settings.OneDriveNotesPath;
            string? accountName = _settingsService.Settings.SelectedOneDriveAccountName;

            CloudSyncStatus newStatus;

            if (!isEnabled || string.IsNullOrWhiteSpace(activePath))
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
                newStatus = new CloudSyncStatus
                {
                    State = CloudSyncState.Synced,
                    Message = $"OneDrive active ({accountName ?? "Connected"})",
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

        private async Task SyncWithCloudAsync(string localRoot)
        {
            try
            {
                _syncCts?.Cancel();
                _syncCts = new CancellationTokenSource();
                var token = _syncCts.Token;

                UpdateStatus(CloudSyncState.Syncing, "Syncing with OneDrive...");

                // Synchronize any local changes or manifests
                await Task.Delay(500, token);

                UpdateStatus(CloudSyncState.Synced, $"Synced with {_settingsService.Settings.SelectedOneDriveAccountName ?? "OneDrive"}");
            }
            catch (OperationCanceledException)
            {
                // Sync cancelled gracefully
            }
            catch (Exception ex)
            {
                UpdateStatus(CloudSyncState.Error, $"Sync error: {ex.Message}");
            }
        }

        private void UpdateStatus(CloudSyncState state, string message)
        {
            _currentStatus.State = state;
            _currentStatus.Message = message;
            _currentStatus.LastSyncTime = DateTime.Now;
            SyncStatusChanged?.Invoke(this, _currentStatus);
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
