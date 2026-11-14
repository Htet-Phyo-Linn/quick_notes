using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;

namespace QuickNote.Infrastructure.Cloud
{
    public class CloudSyncEngine
    {
        private readonly string _notesRoot;
        private readonly ISettingsService _settingsService;
        private readonly SyncManifestManager _manifestManager;
        private readonly OneDriveAuthService _authService;
        private OneDriveApiClient? _apiClient;
        private string? _cachedAccessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;
        private readonly SemaphoreSlim _syncLock = new(1, 1);

        public event EventHandler<CloudSyncStatus>? SyncStatusChanged;

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_settingsService.Settings.OneDriveRefreshToken);
        public string? UserEmail => _settingsService.Settings.OneDriveUserEmail;
        public string? UserName => _settingsService.Settings.OneDriveUserName;
        public DateTime? LastSyncTime => _settingsService.Settings.LastCloudSyncTime;

        public CloudSyncEngine(string notesRoot, ISettingsService settingsService)
        {
            _notesRoot = notesRoot;
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _manifestManager = new SyncManifestManager(notesRoot);
            _authService = new OneDriveAuthService();

            if (IsAuthenticated)
            {
                InitializeApiClient();
            }
        }

        private void InitializeApiClient()
        {
            _apiClient = new OneDriveApiClient(GetOrRefreshAccessTokenAsync);
        }

        public string GetAuthorizationUrl(out string codeVerifier)
        {
            var clientId = !string.IsNullOrWhiteSpace(_settingsService.Settings.OneDriveClientId)
                ? _settingsService.Settings.OneDriveClientId
                : OneDriveAuthService.DefaultClientId;

            return _authService.BuildAuthorizationUrl(clientId, out codeVerifier);
        }

        public async Task<bool> SignInWithCodeAsync(string code, string codeVerifier)
        {
            try
            {
                var clientId = !string.IsNullOrWhiteSpace(_settingsService.Settings.OneDriveClientId)
                    ? _settingsService.Settings.OneDriveClientId
                    : OneDriveAuthService.DefaultClientId;

                var tokenResponse = await _authService.ExchangeCodeForTokenAsync(clientId, code, codeVerifier);
                _cachedAccessToken = tokenResponse.AccessToken;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);

                _settingsService.Settings.OneDriveRefreshToken = tokenResponse.RefreshToken;
                _settingsService.Settings.IsOneDriveSyncEnabled = true;

                // Fetch user profile
                var profile = await _authService.GetUserProfileAsync(tokenResponse.AccessToken);
                if (profile != null)
                {
                    _settingsService.Settings.OneDriveUserEmail = profile.EffectiveEmail;
                    _settingsService.Settings.OneDriveUserName = profile.DisplayName;
                    _settingsService.Settings.SelectedOneDriveAccountName = profile.DisplayName;
                }

                await _settingsService.SaveSettingsAsync();

                InitializeApiClient();

                // Trigger initial sync to discover or create QuickNote folder and fetch notes
                _ = Task.Run(async () => await PerformSyncAsync());

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CloudSyncEngine] Sign-in error: {ex.Message}");
                throw;
            }
        }

        public async Task SignOutAsync()
        {
            _settingsService.Settings.OneDriveRefreshToken = null;
            _settingsService.Settings.OneDriveUserEmail = null;
            _settingsService.Settings.OneDriveUserName = null;
            _settingsService.Settings.OneDriveFolderId = null;
            _settingsService.Settings.IsOneDriveSyncEnabled = false;
            await _settingsService.SaveSettingsAsync();

            _cachedAccessToken = null;
            _tokenExpiry = DateTime.MinValue;
            _apiClient = null;

            SyncStatusChanged?.Invoke(this, new CloudSyncStatus
            {
                State = CloudSyncState.Disabled,
                Message = "OneDrive disconnected"
            });
        }

        private async Task<string> GetOrRefreshAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _cachedAccessToken;
            }

            var refreshToken = _settingsService.Settings.OneDriveRefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new Exception("Not authenticated with Microsoft account");
            }

            var clientId = !string.IsNullOrWhiteSpace(_settingsService.Settings.OneDriveClientId)
                ? _settingsService.Settings.OneDriveClientId
                : OneDriveAuthService.DefaultClientId;

            var refreshed = await _authService.RefreshTokenAsync(clientId, refreshToken);
            _cachedAccessToken = refreshed.AccessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(refreshed.ExpiresIn - 60);

            if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            {
                _settingsService.Settings.OneDriveRefreshToken = refreshed.RefreshToken;
                await _settingsService.SaveSettingsAsync();
            }

            return _cachedAccessToken;
        }

        public async Task<SyncResult> PerformSyncAsync()
        {
            if (!IsAuthenticated)
            {
                return SyncResult.Fail("Not signed in to OneDrive");
            }

            if (!await _syncLock.WaitAsync(100))
            {
                return SyncResult.Ok(0, 0, 0, "Sync already in progress");
            }

            int uploaded = 0;
            int downloaded = 0;
            int conflicts = 0;

            try
            {
                SyncStatusChanged?.Invoke(this, new CloudSyncStatus
                {
                    State = CloudSyncState.Syncing,
                    Message = "Syncing with OneDrive...",
                    AccountName = UserEmail ?? "Connected"
                });

                if (_apiClient == null)
                {
                    InitializeApiClient();
                }

                // 1. Ensure QuickNote folder exists in OneDrive
                var folder = await _apiClient!.EnsureQuickNoteFolderAsync();
                _settingsService.Settings.OneDriveFolderId = folder.Id;

                // 2. Load sync manifest
                var manifest = await _manifestManager.LoadManifestAsync();
                manifest.QuickNoteFolderId = folder.Id;

                // 3. Scan local files for new / modified notes
                _manifestManager.ScanLocalChanges(_notesRoot);

                // 4. List remote items in OneDrive QuickNote folder
                var remoteItems = await _apiClient.ListFolderItemsAsync(folder.Id);
                var remoteFileMap = remoteItems
                    .Where(i => !i.IsFolder && i.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);

                // 5. Process remote files (Initial fetch & Remote deltas)
                foreach (var remote in remoteFileMap.Values)
                {
                    var relPath = remote.Name;
                    var localFullPath = Path.Combine(_notesRoot, relPath);
                    bool localExists = File.Exists(localFullPath);

                    if (!localExists)
                    {
                        // One-time fetch: file is on cloud, not local -> Download it
                        try
                        {
                            var content = await _apiClient.DownloadFileContentAsync(remote.Id);
                            var dir = Path.GetDirectoryName(localFullPath);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            await File.WriteAllTextAsync(localFullPath, content);
                            File.SetLastWriteTimeUtc(localFullPath, remote.LastModifiedDateTime.UtcDateTime);

                            var hash = SyncManifestManager.ComputeFileHash(localFullPath);
                            _manifestManager.RecordSuccessfulDownload(relPath, remote.Id, remote.ETag, remote.LastModifiedDateTime.UtcDateTime, hash, remote.LastModifiedDateTime.UtcDateTime);
                            downloaded++;
                        }
                        catch (Exception dlEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CloudSyncEngine] Failed downloading {relPath}: {dlEx.Message}");
                        }
                    }
                    else
                    {
                        // File exists both locally and remotely
                        manifest.Entries.TryGetValue(relPath, out var entry);
                        bool localModified = entry?.Status == SyncStatus.PendingUpload;
                        bool remoteModified = entry == null || !string.Equals(entry.RemoteETag, remote.ETag, StringComparison.OrdinalIgnoreCase);

                        if (localModified && remoteModified)
                        {
                            // Both modified! Safe conflict resolution: save remote copy as conflict file, keep local
                            try
                            {
                                var remoteContent = await _apiClient.DownloadFileContentAsync(remote.Id);
                                SyncManifestManager.CreateConflictCopy(localFullPath, remoteContent, remote.LastModifiedDateTime.UtcDateTime);
                                conflicts++;

                                // Now upload our local version to replace the primary file
                                var localContent = await File.ReadAllTextAsync(localFullPath);
                                var updatedRemote = await _apiClient.UploadFileContentAsync(folder.Id, remote.Name, localContent);
                                var localHash = SyncManifestManager.ComputeFileHash(localFullPath);
                                var localModTime = File.GetLastWriteTimeUtc(localFullPath);

                                _manifestManager.RecordSuccessfulUpload(relPath, updatedRemote.Id, updatedRemote.ETag, updatedRemote.LastModifiedDateTime.UtcDateTime, localHash, localModTime);
                                uploaded++;
                            }
                            catch (Exception cEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[CloudSyncEngine] Conflict handling error for {relPath}: {cEx.Message}");
                            }
                        }
                        else if (localModified)
                        {
                            // Local edited, remote untouched -> Upload to cloud
                            try
                            {
                                var localContent = await File.ReadAllTextAsync(localFullPath);
                                var updatedRemote = await _apiClient.UploadFileContentAsync(folder.Id, remote.Name, localContent);
                                var localHash = SyncManifestManager.ComputeFileHash(localFullPath);
                                var localModTime = File.GetLastWriteTimeUtc(localFullPath);

                                _manifestManager.RecordSuccessfulUpload(relPath, updatedRemote.Id, updatedRemote.ETag, updatedRemote.LastModifiedDateTime.UtcDateTime, localHash, localModTime);
                                uploaded++;
                            }
                            catch (Exception upEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[CloudSyncEngine] Upload error for {relPath}: {upEx.Message}");
                            }
                        }
                        else if (remoteModified)
                        {
                            // Remote edited on PC or elsewhere -> Download to phone
                            try
                            {
                                var content = await _apiClient.DownloadFileContentAsync(remote.Id);
                                await File.WriteAllTextAsync(localFullPath, content);
                                File.SetLastWriteTimeUtc(localFullPath, remote.LastModifiedDateTime.UtcDateTime);

                                var hash = SyncManifestManager.ComputeFileHash(localFullPath);
                                _manifestManager.RecordSuccessfulDownload(relPath, remote.Id, remote.ETag, remote.LastModifiedDateTime.UtcDateTime, hash, remote.LastModifiedDateTime.UtcDateTime);
                                downloaded++;
                            }
                            catch (Exception dlEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[CloudSyncEngine] Download error for {relPath}: {dlEx.Message}");
                            }
                        }
                    }
                }

                // 6. Upload any local files that do not exist remotely yet
                foreach (var entry in manifest.Entries.Values.ToList())
                {
                    if (entry.Status == SyncStatus.PendingUpload && !remoteFileMap.ContainsKey(entry.RelativePath))
                    {
                        var localFullPath = Path.Combine(_notesRoot, entry.RelativePath);
                        if (File.Exists(localFullPath))
                        {
                            try
                            {
                                var fileName = Path.GetFileName(entry.RelativePath);
                                var localContent = await File.ReadAllTextAsync(localFullPath);
                                var createdRemote = await _apiClient.UploadFileContentAsync(folder.Id, fileName, localContent);
                                var localHash = SyncManifestManager.ComputeFileHash(localFullPath);
                                var localModTime = File.GetLastWriteTimeUtc(localFullPath);

                                _manifestManager.RecordSuccessfulUpload(entry.RelativePath, createdRemote.Id, createdRemote.ETag, createdRemote.LastModifiedDateTime.UtcDateTime, localHash, localModTime);
                                uploaded++;
                            }
                            catch (Exception upEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[CloudSyncEngine] Upload error for new note {entry.RelativePath}: {upEx.Message}");
                            }
                        }
                    }
                }

                // 7. Save updated manifest and settings
                await _manifestManager.SaveManifestAsync();

                _settingsService.Settings.LastCloudSyncTime = DateTime.UtcNow;
                await _settingsService.SaveSettingsAsync();

                var statusMessage = $"OneDrive Synced (↑{uploaded} ↓{downloaded}{(conflicts > 0 ? $" ⚠️{conflicts}" : "")})";
                SyncStatusChanged?.Invoke(this, new CloudSyncStatus
                {
                    State = CloudSyncState.Synced,
                    Message = statusMessage,
                    AccountName = UserEmail ?? "Connected",
                    LastSyncTime = DateTime.Now
                });

                return SyncResult.Ok(uploaded, downloaded, conflicts, statusMessage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CloudSyncEngine] Sync error: {ex.Message}");
                SyncStatusChanged?.Invoke(this, new CloudSyncStatus
                {
                    State = CloudSyncState.Error,
                    Message = $"Sync failed: {ex.Message}",
                    AccountName = UserEmail
                });
                return SyncResult.Fail(ex.Message, ex);
            }
            finally
            {
                _syncLock.Release();
            }
        }
    }
}
