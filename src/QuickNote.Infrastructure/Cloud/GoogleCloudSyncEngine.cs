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
    public class GoogleCloudSyncEngine
    {
        private readonly string _notesRoot;
        private readonly ISettingsService _settingsService;
        private readonly SyncManifestManager _manifestManager;
        private readonly GoogleDriveAuthService _authService;
        private GoogleDriveApiClient? _apiClient;
        private string? _cachedAccessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;
        private readonly SemaphoreSlim _syncLock = new(1, 1);

        public event EventHandler<CloudSyncStatus>? SyncStatusChanged;

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_settingsService.Settings.GoogleDriveRefreshToken);
        public string? UserEmail => _settingsService.Settings.GoogleDriveUserEmail;
        public string? UserName => _settingsService.Settings.GoogleDriveUserName;
        public DateTime? LastSyncTime => _settingsService.Settings.LastGoogleDriveSyncTime;

        public GoogleCloudSyncEngine(string notesRoot, ISettingsService settingsService)
        {
            _notesRoot = notesRoot;
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _manifestManager = new SyncManifestManager(notesRoot);
            _authService = new GoogleDriveAuthService();

            if (IsAuthenticated)
            {
                InitializeApiClient();
            }
        }

        private void InitializeApiClient()
        {
            _apiClient = new GoogleDriveApiClient(GetOrRefreshAccessTokenAsync);
        }

        public string GetAuthorizationUrl(out string codeVerifier)
        {
            var clientId = !string.IsNullOrWhiteSpace(_settingsService.Settings.GoogleDriveClientId)
                ? _settingsService.Settings.GoogleDriveClientId
                : GoogleDriveAuthService.DefaultClientId;

            return _authService.BuildAuthorizationUrl(clientId, out codeVerifier);
        }

        public async Task<bool> SignInWithCodeAsync(string code, string codeVerifier)
        {
            try
            {
                var clientId = !string.IsNullOrWhiteSpace(_settingsService.Settings.GoogleDriveClientId)
                    ? _settingsService.Settings.GoogleDriveClientId
                    : GoogleDriveAuthService.DefaultClientId;
                var clientSecret = !string.IsNullOrWhiteSpace(_settingsService.Settings.GoogleDriveClientSecret)
                    ? _settingsService.Settings.GoogleDriveClientSecret
                    : GoogleDriveAuthService.DefaultClientSecret;

                var tokenResponse = await _authService.ExchangeCodeForTokenAsync(clientId, clientSecret, code, codeVerifier);
                _cachedAccessToken = tokenResponse.AccessToken;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);

                if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
                {
                    _settingsService.Settings.GoogleDriveRefreshToken = tokenResponse.RefreshToken;
                }
                _settingsService.Settings.IsGoogleDriveSyncEnabled = true;

                // Fetch user profile
                var profile = await _authService.GetUserProfileAsync(tokenResponse.AccessToken);
                if (profile != null)
                {
                    _settingsService.Settings.GoogleDriveUserEmail = profile.Email;
                    _settingsService.Settings.GoogleDriveUserName = profile.Name;
                    _settingsService.Settings.SelectedGoogleAccountName = profile.Name;
                }

                await _settingsService.SaveSettingsAsync();

                InitializeApiClient();

                // Trigger initial sync to discover or create QuickNote folder and fetch notes
                _ = Task.Run(async () => await PerformSyncAsync());

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GoogleCloudSyncEngine] Sign-in error: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        public async Task SignOutAsync()
        {
            _settingsService.Settings.GoogleDriveRefreshToken = null;
            _settingsService.Settings.GoogleDriveUserEmail = null;
            _settingsService.Settings.GoogleDriveUserName = null;
            _settingsService.Settings.GoogleDriveFolderId = null;
            _settingsService.Settings.IsGoogleDriveSyncEnabled = false;
            await _settingsService.SaveSettingsAsync();

            _cachedAccessToken = null;
            _tokenExpiry = DateTime.MinValue;
            _apiClient = null;

            SyncStatusChanged?.Invoke(this, new CloudSyncStatus
            {
                State = CloudSyncState.Disabled,
                Message = "Google Drive: Disconnected"
            });
        }

        private async Task<string> GetOrRefreshAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _cachedAccessToken;
            }

            var refreshToken = _settingsService.Settings.GoogleDriveRefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new Exception("Not authenticated with Google account");
            }

            var clientId = !string.IsNullOrWhiteSpace(_settingsService.Settings.GoogleDriveClientId)
                ? _settingsService.Settings.GoogleDriveClientId
                : GoogleDriveAuthService.DefaultClientId;
            var clientSecret = !string.IsNullOrWhiteSpace(_settingsService.Settings.GoogleDriveClientSecret)
                ? _settingsService.Settings.GoogleDriveClientSecret
                : GoogleDriveAuthService.DefaultClientSecret;

            var refreshed = await _authService.RefreshTokenAsync(clientId, clientSecret, refreshToken);
            _cachedAccessToken = refreshed.AccessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(refreshed.ExpiresIn - 60);

            if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            {
                _settingsService.Settings.GoogleDriveRefreshToken = refreshed.RefreshToken;
                await _settingsService.SaveSettingsAsync();
            }

            return _cachedAccessToken;
        }

        public async Task<SyncResult> PerformSyncAsync()
        {
            if (!IsAuthenticated)
            {
                return SyncResult.Fail("Not signed in to Google Drive");
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
                Console.WriteLine($"[GoogleCloudSyncEngine] PerformSyncAsync started. NotesRoot: {_notesRoot}");
                SyncStatusChanged?.Invoke(this, new CloudSyncStatus
                {
                    State = CloudSyncState.Syncing,
                    Message = "Syncing with Google Drive...",
                    AccountName = UserEmail ?? "Connected"
                });

                if (_apiClient == null)
                {
                    InitializeApiClient();
                }

                // 1. Ensure QuickNote folder exists in Google Drive
                var folderId = await _apiClient!.EnsureQuickNoteFolderAsync();
                Console.WriteLine($"[GoogleCloudSyncEngine] QuickNote folder ID: {folderId}");
                _settingsService.Settings.GoogleDriveFolderId = folderId;

                // 2. Load sync manifest
                var manifest = await _manifestManager.LoadManifestAsync();
                manifest.QuickNoteFolderId = folderId;

                // 3. Scan local files for new / modified notes
                var pendingList = _manifestManager.ScanLocalChanges(_notesRoot);
                Console.WriteLine($"[GoogleCloudSyncEngine] ScanLocalChanges found {pendingList.Count} pending uploads across {_notesRoot}");

                // 4. List remote items in Google Drive QuickNote folder
                var remoteItems = await _apiClient.ListFolderNotesAsync(folderId);
                Console.WriteLine($"[GoogleCloudSyncEngine] Listed {remoteItems.Count} remote items from Google Drive");
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
                            Console.WriteLine($"[GoogleCloudSyncEngine] Downloading remote note {remote.Name} ({remote.Id})...");
                            var content = await _apiClient.DownloadNoteTextAsync(remote.Id);
                            var dir = Path.GetDirectoryName(localFullPath);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            await File.WriteAllTextAsync(localFullPath, content);
                            File.SetLastWriteTimeUtc(localFullPath, remote.ModifiedTime.UtcDateTime);

                            var hash = SyncManifestManager.ComputeFileHash(localFullPath);
                            _manifestManager.RecordSuccessfulDownload(relPath, remote.Id, remote.Md5Checksum ?? string.Empty, remote.ModifiedTime.UtcDateTime, hash, remote.ModifiedTime.UtcDateTime);
                            downloaded++;
                        }
                        catch (Exception dlEx)
                        {
                            Console.WriteLine($"[GoogleCloudSyncEngine] Failed downloading {relPath}: {dlEx.Message}");
                        }
                    }
                    else
                    {
                        // File exists both locally and remotely
                        manifest.Entries.TryGetValue(relPath, out var entry);
                        bool localModified = entry?.Status == SyncStatus.PendingUpload;
                        var localHash = SyncManifestManager.ComputeFileHash(localFullPath);
                        bool remoteModified = entry == null ||
                            (!string.IsNullOrEmpty(remote.Md5Checksum) && !string.Equals(entry.RemoteETag, remote.Md5Checksum, StringComparison.OrdinalIgnoreCase)) ||
                            remote.ModifiedTime.UtcDateTime > entry.RemoteLastModified.AddSeconds(2);

                        if (localModified && remoteModified && !string.Equals(localHash, entry?.LocalHash, StringComparison.OrdinalIgnoreCase))
                        {
                            // Both modified! Safe conflict resolution: save remote copy as conflict file, keep local
                            try
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Conflict detected on {relPath}. Creating conflict copy...");
                                var remoteContent = await _apiClient.DownloadNoteTextAsync(remote.Id);
                                SyncManifestManager.CreateConflictCopy(localFullPath, remoteContent, remote.ModifiedTime.UtcDateTime);
                                conflicts++;

                                // Now upload our local version to replace the primary file
                                var localContent = await File.ReadAllTextAsync(localFullPath);
                                var updatedRemote = await _apiClient.UpdateExistingNoteAsync(remote.Id, localContent);
                                var localModTime = File.GetLastWriteTimeUtc(localFullPath);

                                _manifestManager.RecordSuccessfulUpload(relPath, updatedRemote.Id, updatedRemote.Md5Checksum ?? string.Empty, updatedRemote.ModifiedTime.UtcDateTime, localHash, localModTime);
                                uploaded++;
                            }
                            catch (Exception cEx)
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Conflict handling error for {relPath}: {cEx.Message}");
                            }
                        }
                        else if (localModified)
                        {
                            // Local edited, remote untouched -> Upload to cloud
                            try
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Updating remote note {relPath} ({remote.Id})...");
                                var localContent = await File.ReadAllTextAsync(localFullPath);
                                var updatedRemote = await _apiClient.UpdateExistingNoteAsync(remote.Id, localContent);
                                var localModTime = File.GetLastWriteTimeUtc(localFullPath);

                                _manifestManager.RecordSuccessfulUpload(relPath, updatedRemote.Id, updatedRemote.Md5Checksum ?? string.Empty, updatedRemote.ModifiedTime.UtcDateTime, localHash, localModTime);
                                uploaded++;
                            }
                            catch (Exception upEx)
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Upload error for {relPath}: {upEx.Message}");
                            }
                        }
                        else if (remoteModified)
                        {
                            // Remote edited on another device -> Download to phone
                            try
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Downloading updated remote note {relPath}...");
                                var content = await _apiClient.DownloadNoteTextAsync(remote.Id);
                                await File.WriteAllTextAsync(localFullPath, content);
                                File.SetLastWriteTimeUtc(localFullPath, remote.ModifiedTime.UtcDateTime);

                                var newHash = SyncManifestManager.ComputeFileHash(localFullPath);
                                _manifestManager.RecordSuccessfulDownload(relPath, remote.Id, remote.Md5Checksum ?? string.Empty, remote.ModifiedTime.UtcDateTime, newHash, remote.ModifiedTime.UtcDateTime);
                                downloaded++;
                            }
                            catch (Exception dlEx)
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Download error for {relPath}: {dlEx.Message}");
                            }
                        }
                    }
                }

                // 6. Upload any local files that do not exist remotely yet
                foreach (var entry in manifest.Entries.Values.ToList())
                {
                    var fileKey = entry.RelativePath.Replace('\\', '/');
                    var baseName = Path.GetFileName(entry.RelativePath);
                    bool alreadyRemote = remoteFileMap.ContainsKey(fileKey) || remoteFileMap.ContainsKey(baseName);

                    if (entry.Status == SyncStatus.PendingUpload && !alreadyRemote)
                    {
                        var localFullPath = Path.Combine(_notesRoot, entry.RelativePath);
                        if (File.Exists(localFullPath))
                        {
                            try
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Uploading new local note {entry.RelativePath}...");
                                var localContent = await File.ReadAllTextAsync(localFullPath);
                                var createdRemote = await _apiClient.UploadNewNoteAsync(folderId, baseName, localContent);
                                var localHash = SyncManifestManager.ComputeFileHash(localFullPath);
                                var localModTime = File.GetLastWriteTimeUtc(localFullPath);

                                _manifestManager.RecordSuccessfulUpload(entry.RelativePath, createdRemote.Id, createdRemote.Md5Checksum ?? string.Empty, createdRemote.ModifiedTime.UtcDateTime, localHash, localModTime);
                                uploaded++;
                                Console.WriteLine($"[GoogleCloudSyncEngine] Upload success: {entry.RelativePath} -> ID {createdRemote.Id}");
                            }
                            catch (Exception upEx)
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Upload error for new note {entry.RelativePath}: {upEx.Message}");
                            }
                        }
                    }
                }

                // 7. Save updated manifest and settings
                await _manifestManager.SaveManifestAsync();

                _settingsService.Settings.LastGoogleDriveSyncTime = DateTime.UtcNow;
                await _settingsService.SaveSettingsAsync();

                var statusMessage = $"Google Drive Synced (↑{uploaded} ↓{downloaded}{(conflicts > 0 ? $" ⚠️{conflicts}" : "")})";
                Console.WriteLine($"[GoogleCloudSyncEngine] {statusMessage}");
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
                Console.WriteLine($"[GoogleCloudSyncEngine] Sync error: {ex.Message}\n{ex.StackTrace}");
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
