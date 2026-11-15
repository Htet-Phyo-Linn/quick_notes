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
        private readonly ITrashBinService _trashBinService;
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

        public GoogleCloudSyncEngine(string notesRoot, ISettingsService settingsService, ITrashBinService? trashBinService = null)
        {
            _notesRoot = notesRoot;
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _trashBinService = trashBinService ?? new QuickNote.Infrastructure.Services.TrashBinService(notesRoot);
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

                SyncLogger.Log($"Google sign-in successful: {profile?.Email}. Initiating first-time sync...");
                // Run initial sync so all notes and folders are immediately synchronized
                await PerformSyncAsync();

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
                SyncLogger.Log("Sync skipped: Not signed in to Google Drive.");
                return SyncResult.Fail("Not signed in to Google Drive");
            }

            if (!await _syncLock.WaitAsync(100))
            {
                SyncLogger.Log("Sync skipped: Another sync operation is already in progress.");
                return SyncResult.Ok(0, 0, 0, "Sync already in progress");
            }

            int uploaded = 0;
            int downloaded = 0;
            int conflicts = 0;
            int foldersSynced = 0;

            void ReportStage(string stageMsg, CloudSyncState state = CloudSyncState.Syncing)
            {
                SyncLogger.Log(stageMsg);
                SyncStatusChanged?.Invoke(this, new CloudSyncStatus
                {
                    State = state,
                    Message = stageMsg,
                    AccountName = UserEmail ?? "Connected",
                    LastSyncTime = LastSyncTime
                });
            }

            try
            {
                ReportStage("Starting Google Drive synchronization...");

                if (_apiClient == null)
                {
                    InitializeApiClient();
                }

                // 1. Ensure QuickNote root folder exists in Google Drive
                var folderId = await _apiClient!.EnsureQuickNoteFolderAsync();
                _settingsService.Settings.GoogleDriveFolderId = folderId;

                // 2. Load sync manifest
                var manifest = await _manifestManager.LoadManifestAsync();
                manifest.QuickNoteFolderId = folderId;

                // 3. Load initial remote snapshot to know existing cloud folders & files
                ReportStage("Scanning Google Drive remote folder hierarchy...");
                var remoteSnapshot = await _apiClient.FetchRemoteSnapshotAsync(folderId);
                var folderCache = remoteSnapshot.RemoteFolders;
                var remoteFileMap = remoteSnapshot.RemoteFiles;
                SyncLogger.Log($"Cloud hierarchy discovered: {folderCache.Count} folder(s), {remoteFileMap.Count} note(s).");

                // 4. Load current trash items so we NEVER resurrect or push trashed items
                var trashItems = await _trashBinService.GetTrashItemsAsync();
                var trashedOriginalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var trashedDirPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in trashItems)
                {
                    if (!string.IsNullOrEmpty(t.OriginalPath))
                    {
                        var rel = Path.GetRelativePath(_notesRoot, t.OriginalPath).Replace('\\', '/').Trim('/');
                        trashedOriginalPaths.Add(rel);
                        trashedOriginalPaths.Add(Path.GetFileName(t.OriginalPath));
                        if (t.IsDirectory)
                        {
                            trashedDirPaths.Add(rel);
                        }
                    }
                }

                // ─────────────────────────────────────────────────────────────
                // PHASE 1: LOCAL PUSH FIRST (Items 2 & 3)
                // ─────────────────────────────────────────────────────────────
                ReportStage("Phase 1: Pushing local notes & folders to Google Drive...");

                // A. Synchronize local folders to cloud (ensures blank local folders exist on cloud!)
                if (Directory.Exists(_notesRoot))
                {
                    var localDirs = Directory.GetDirectories(_notesRoot, "*", SearchOption.AllDirectories);
                    foreach (var dir in localDirs)
                    {
                        var dirName = Path.GetFileName(dir);
                        if (dirName.StartsWith(".") || dir.Contains(".trash", StringComparison.OrdinalIgnoreCase) || dir.Contains(".assets", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var relDir = Path.GetRelativePath(_notesRoot, dir).Replace('\\', '/').Trim('/');
                        if (trashedDirPaths.Contains(relDir)) continue;

                        if (!folderCache.ContainsKey(relDir))
                        {
                            try
                            {
                                var newFolderId = await _apiClient.EnsureFolderPathCachedAsync(folderId, relDir, folderCache);
                                foldersSynced++;
                                SyncLogger.Log($"Pushed local folder to Google Drive: '{relDir}'");
                            }
                            catch (Exception fEx)
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Folder upload error for '{relDir}': {fEx.Message}");
                            }
                        }
                    }
                }

                // B. Propagate pending local deletions to Google Drive
                var pendingDeletes = _manifestManager.GetPendingDeletions();
                foreach (var del in pendingDeletes)
                {
                    try
                    {
                        Console.WriteLine($"[GoogleCloudSyncEngine] Deleting remote note '{del.RelativePath}' ({del.RemoteId})...");
                        await _apiClient.DeleteNoteAsync(del.RemoteId);
                        SyncLogger.Log($"Deleted remote note: '{del.RelativePath}'");
                    }
                    catch (Exception delEx)
                    {
                        Console.WriteLine($"[GoogleCloudSyncEngine] DeleteNoteAsync error for '{del.RelativePath}': {delEx.Message}");
                    }
                    finally
                    {
                        _manifestManager.RemoveEntry(del.RelativePath);
                    }
                }

                // C. Clean up any remote folders that were moved to local trash
                foreach (var trashedRelDir in trashedDirPaths)
                {
                    if (folderCache.TryGetValue(trashedRelDir, out var trashedFolderRemoteId))
                    {
                        try
                        {
                            await _apiClient.DeleteNoteAsync(trashedFolderRemoteId);
                            folderCache.Remove(trashedRelDir);
                            SyncLogger.Log($"Removed trashed folder from Google Drive: '{trashedRelDir}'");
                        }
                        catch { }
                    }
                }

                // D. Scan local files & upload new or modified notes
                var pendingUploads = _manifestManager.ScanLocalChanges(_notesRoot);
                SyncLogger.Log($"Found {pendingUploads.Count} pending local note change(s) for upload.");

                foreach (var entry in manifest.Entries.Values.ToList())
                {
                    if (entry.Status != SyncStatus.PendingUpload) continue;

                    var fileKey = entry.RelativePath.Replace('\\', '/').Trim('/');
                    var localFullPath = Path.Combine(_notesRoot, entry.RelativePath);

                    if (!File.Exists(localFullPath)) continue;

                    // If note is in local trash, don't upload it!
                    if (trashedOriginalPaths.Contains(fileKey) || trashedOriginalPaths.Contains(Path.GetFileName(fileKey)))
                    {
                        continue;
                    }

                    try
                    {
                        var baseName = Path.GetFileName(entry.RelativePath);
                        var relDir = Path.GetDirectoryName(entry.RelativePath)?.Replace('\\', '/').Trim('/') ?? "";
                        var targetFolderId = await _apiClient.EnsureFolderPathCachedAsync(folderId, relDir, folderCache);
                        var localContent = await File.ReadAllTextAsync(localFullPath);
                        var localHash = SyncManifestManager.ComputeFileHash(localFullPath);
                        var localModTime = File.GetLastWriteTimeUtc(localFullPath);

                        if (remoteFileMap.TryGetValue(fileKey, out var existingRemote))
                        {
                            // Update existing cloud note
                            var updatedRemote = await _apiClient.UpdateExistingNoteAsync(existingRemote.Id, localContent);
                            _manifestManager.RecordSuccessfulUpload(entry.RelativePath, updatedRemote.Id, updatedRemote.Md5Checksum ?? string.Empty, updatedRemote.ModifiedTime.UtcDateTime, localHash, localModTime);
                            uploaded++;
                            SyncLogger.Log($"Uploaded modified note: '{entry.RelativePath}'");
                        }
                        else
                        {
                            // Upload new cloud note
                            var createdRemote = await _apiClient.UploadNewNoteAsync(targetFolderId, baseName, localContent);
                            _manifestManager.RecordSuccessfulUpload(entry.RelativePath, createdRemote.Id, createdRemote.Md5Checksum ?? string.Empty, createdRemote.ModifiedTime.UtcDateTime, localHash, localModTime);
                            remoteFileMap[fileKey] = createdRemote;
                            uploaded++;
                            SyncLogger.Log($"Uploaded new note: '{entry.RelativePath}'");
                        }
                    }
                    catch (Exception upEx)
                    {
                        Console.WriteLine($"[GoogleCloudSyncEngine] Upload error for note '{entry.RelativePath}': {upEx.Message}");
                        SyncLogger.Log($"Upload failed for '{entry.RelativePath}': {upEx.Message}");
                    }
                }

                // ─────────────────────────────────────────────────────────────
                // PHASE 2: CLOUD DOWNLOAD SECOND (Items 2 & 3)
                // ─────────────────────────────────────────────────────────────
                ReportStage("Phase 2: Downloading remote notes & folders...");

                // A. Replicate remote folders locally (ensures blank remote folders exist on local disk!)
                foreach (var kvp in folderCache)
                {
                    var relDir = kvp.Key;
                    if (string.IsNullOrWhiteSpace(relDir)) continue;

                    if (trashedDirPaths.Contains(relDir))
                    {
                        // Folder was trashed locally, delete remote zombie
                        try { await _apiClient.DeleteNoteAsync(kvp.Value); } catch { }
                        continue;
                    }

                    var localDirPath = Path.Combine(_notesRoot, relDir);
                    if (!Directory.Exists(localDirPath))
                    {
                        Directory.CreateDirectory(localDirPath);
                        foldersSynced++;
                        SyncLogger.Log($"Created local directory from Google Drive: '{relDir}'");
                    }
                }

                // B. Download remote notes
                foreach (var kvp in remoteFileMap)
                {
                    var relPath = kvp.Key;
                    var remote = kvp.Value;
                    var localFullPath = Path.Combine(_notesRoot, relPath);
                    bool localExists = File.Exists(localFullPath);

                    if (!localExists)
                    {
                        // If item is in trash, delete remote copy to prevent resurrection
                        if (trashedOriginalPaths.Contains(relPath) || trashedOriginalPaths.Contains(Path.GetFileName(relPath)))
                        {
                            try
                            {
                                await _apiClient.DeleteNoteAsync(remote.Id);
                                SyncLogger.Log($"Removed trashed note from cloud: '{relPath}'");
                            }
                            catch { }
                            continue;
                        }

                        // Download new note from cloud
                        try
                        {
                            var content = await _apiClient.DownloadNoteTextAsync(remote.Id);
                            var dir = Path.GetDirectoryName(localFullPath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            await File.WriteAllTextAsync(localFullPath, content);
                            File.SetLastWriteTimeUtc(localFullPath, remote.ModifiedTime.UtcDateTime);

                            var hash = SyncManifestManager.ComputeFileHash(localFullPath);
                            _manifestManager.RecordSuccessfulDownload(relPath, remote.Id, remote.Md5Checksum ?? string.Empty, remote.ModifiedTime.UtcDateTime, hash, remote.ModifiedTime.UtcDateTime);
                            downloaded++;
                            SyncLogger.Log($"Downloaded remote note: '{relPath}'");
                        }
                        catch (Exception dlEx)
                        {
                            Console.WriteLine($"[GoogleCloudSyncEngine] Download error for '{relPath}': {dlEx.Message}");
                            SyncLogger.Log($"Download error for '{relPath}': {dlEx.Message}");
                        }
                    }
                    else
                    {
                        // File exists both locally and remotely: check remote updates
                        manifest.Entries.TryGetValue(relPath, out var entry);
                        bool localModified = entry?.Status == SyncStatus.PendingUpload;
                        var localHash = SyncManifestManager.ComputeFileHash(localFullPath);
                        bool remoteModified = entry == null ||
                            (!string.IsNullOrEmpty(remote.Md5Checksum) && !string.Equals(entry.RemoteETag, remote.Md5Checksum, StringComparison.OrdinalIgnoreCase)) ||
                            remote.ModifiedTime.UtcDateTime > entry.RemoteLastModified.AddSeconds(2);

                        if (localModified && remoteModified && !string.Equals(localHash, entry?.LocalHash, StringComparison.OrdinalIgnoreCase))
                        {
                            // Conflict detected!
                            try
                            {
                                var remoteContent = await _apiClient.DownloadNoteTextAsync(remote.Id);
                                SyncManifestManager.CreateConflictCopy(localFullPath, remoteContent, remote.ModifiedTime.UtcDateTime);
                                conflicts++;
                                SyncLogger.Log($"Conflict detected & resolved with conflict copy: '{relPath}'");

                                // Keep local as primary and re-upload to cloud
                                var localContent = await File.ReadAllTextAsync(localFullPath);
                                var updatedRemote = await _apiClient.UpdateExistingNoteAsync(remote.Id, localContent);
                                var localModTime = File.GetLastWriteTimeUtc(localFullPath);
                                _manifestManager.RecordSuccessfulUpload(relPath, updatedRemote.Id, updatedRemote.Md5Checksum ?? string.Empty, updatedRemote.ModifiedTime.UtcDateTime, localHash, localModTime);
                                uploaded++;
                            }
                            catch (Exception cEx)
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Conflict handling error for '{relPath}': {cEx.Message}");
                            }
                        }
                        else if (remoteModified && !localModified)
                        {
                            // Remote edited on another device -> download update
                            try
                            {
                                var content = await _apiClient.DownloadNoteTextAsync(remote.Id);
                                await File.WriteAllTextAsync(localFullPath, content);
                                File.SetLastWriteTimeUtc(localFullPath, remote.ModifiedTime.UtcDateTime);

                                var newHash = SyncManifestManager.ComputeFileHash(localFullPath);
                                _manifestManager.RecordSuccessfulDownload(relPath, remote.Id, remote.Md5Checksum ?? string.Empty, remote.ModifiedTime.UtcDateTime, newHash, remote.ModifiedTime.UtcDateTime);
                                downloaded++;
                                SyncLogger.Log($"Updated local note from remote changes: '{relPath}'");
                            }
                            catch (Exception dlEx)
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Remote update error for '{relPath}': {dlEx.Message}");
                            }
                        }
                    }
                }

                // ─────────────────────────────────────────────────────────────
                // PHASE 3: MANIFEST, TIMESTAMP & COMPLETION
                // ─────────────────────────────────────────────────────────────
                await _manifestManager.SaveManifestAsync();

                _settingsService.Settings.LastGoogleDriveSyncTime = DateTime.UtcNow;
                await _settingsService.SaveSettingsAsync();

                var statusMessage = $"Google Drive Synced (↑{uploaded} pushed, ↓{downloaded} downloaded{(conflicts > 0 ? $", ⚠️{conflicts} conflicts" : "")})";
                ReportStage(statusMessage, CloudSyncState.Synced);

                return SyncResult.Ok(uploaded, downloaded, conflicts, statusMessage);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Sync failed: {ex.Message}";
                Console.WriteLine($"[GoogleCloudSyncEngine] {errorMsg}\n{ex.StackTrace}");
                SyncLogger.Log($"ERROR: {errorMsg}");
                SyncStatusChanged?.Invoke(this, new CloudSyncStatus
                {
                    State = CloudSyncState.Error,
                    Message = errorMsg,
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
