using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Markdown;
using QuickNote.Core.Models;
using QuickNote.Core.Utilities;
using QuickNote.Infrastructure.FileSystem;

namespace QuickNote.Infrastructure.Cloud
{
    public class GoogleCloudSyncEngine
    {
        private string _notesRoot;
        private readonly ISettingsService _settingsService;
        private ITrashBinService _trashBinService;
        private SyncManifestManager _manifestManager;
        private readonly GoogleDriveAuthService _authService;
        private readonly AtomicFileWriter _atomicFileWriter = new();
        private GoogleDriveApiClient? _apiClient;
        private string? _cachedAccessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;
        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private bool _trailingSyncRequested;
        private bool _isSyncing;

        public event EventHandler<CloudSyncStatus>? SyncStatusChanged;

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_settingsService.Settings.GoogleDriveRefreshToken);
        public bool IsSyncing => _isSyncing;
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

        public void UpdateNotesRoot(string newNotesRoot)
        {
            if (string.IsNullOrWhiteSpace(newNotesRoot)) return;
            _notesRoot = newNotesRoot;
            _trashBinService = new QuickNote.Infrastructure.Services.TrashBinService(newNotesRoot);
            _manifestManager = new SyncManifestManager(newNotesRoot);
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
                _trailingSyncRequested = true;
                SyncLogger.Log("Sync queued: Another sync operation is already in progress. Trailing sync scheduled.");
                return SyncResult.Ok(0, 0, 0, "Sync queued");
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
                _isSyncing = true;
                ReportStage("Checking cloud catalog...");

                if (_apiClient == null)
                {
                    InitializeApiClient();
                }

                // 1. Ensure QuickNote root folder exists in Google Drive (cached in settings)
                string folderId = _settingsService.Settings.GoogleDriveFolderId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(folderId))
                {
                    folderId = await _apiClient!.EnsureQuickNoteFolderAsync();
                    _settingsService.Settings.GoogleDriveFolderId = folderId;
                    await _settingsService.SaveSettingsAsync();
                }

                // 2. Load sync manifest
                var manifest = await _manifestManager.LoadManifestAsync();
                if (!string.IsNullOrEmpty(manifest.QuickNoteFolderId) &&
                    !string.Equals(manifest.QuickNoteFolderId, folderId, StringComparison.OrdinalIgnoreCase))
                {
                    SyncLogger.Log($"Google Drive root folder ID changed from '{manifest.QuickNoteFolderId}' to '{folderId}'. Resetting manifest entries to synchronize all local notes to root.");
                    manifest.Entries.Clear();
                }
                manifest.QuickNoteFolderId = folderId;

                // 3. Fast Catalog-Driven Delta Sync (1 API Call to fetch quicknote_catalog.json)
                var (catalogFileId, catalogJson) = await _apiClient!.GetCatalogFileAsync(folderId);
                CloudCatalog? cloudCatalog = null;
                if (!string.IsNullOrEmpty(catalogJson))
                {
                    try
                    {
                        cloudCatalog = JsonSerializer.Deserialize<CloudCatalog>(catalogJson);
                    }
                    catch (Exception catEx)
                    {
                        SyncLogger.Log($"Catalog parse warning: {catEx.Message}. Will rebuild from remote snapshot.");
                    }
                }

                Dictionary<string, string> folderCache;
                Dictionary<string, GoogleDriveFileDto> remoteFileMap = new(StringComparer.OrdinalIgnoreCase);

                if (cloudCatalog != null)
                {
                    SyncLogger.Log($"Central cloud catalog loaded (Revision: {cloudCatalog.CatalogRevision}, {cloudCatalog.Notes.Count} note(s)). Using fast delta sync.");
                    folderCache = new Dictionary<string, string>(cloudCatalog.Folders, StringComparer.OrdinalIgnoreCase);
                    if (!folderCache.ContainsKey("")) folderCache[""] = folderId;

                    foreach (var kvp in cloudCatalog.Notes)
                    {
                        remoteFileMap[kvp.Key] = new GoogleDriveFileDto
                        {
                            Id = kvp.Value.DriveFileId,
                            Name = Path.GetFileName(kvp.Key),
                            Md5Checksum = kvp.Value.ContentHash,
                            ModifiedTime = new DateTimeOffset(DateTime.SpecifyKind(kvp.Value.LastModifiedUtc, DateTimeKind.Utc)),
                            Size = kvp.Value.SizeBytes.ToString()
                        };
                    }
                }
                else
                {
                    ReportStage("Scanning Google Drive remote folder hierarchy (first-time catalog build)...");
                    var remoteSnapshot = await _apiClient!.FetchRemoteSnapshotAsync(folderId);
                    folderCache = remoteSnapshot.RemoteFolders;
                    remoteFileMap = remoteSnapshot.RemoteFiles;
                    cloudCatalog = new CloudCatalog
                    {
                        Folders = new Dictionary<string, string>(folderCache, StringComparer.OrdinalIgnoreCase)
                    };
                    foreach (var (relPath, dto) in remoteFileMap)
                    {
                        cloudCatalog.Notes[relPath] = new CloudNoteEntry
                        {
                            RelativePath = relPath,
                            DriveFileId = dto.Id,
                            Title = Path.GetFileNameWithoutExtension(dto.Name),
                            ContentHash = dto.Md5Checksum ?? string.Empty,
                            LastModifiedUtc = dto.ModifiedTime.UtcDateTime,
                            SizeBytes = long.TryParse(dto.Size, out var sz) ? sz : 0
                        };
                    }
                    SyncLogger.Log($"Initial cloud catalog built with {cloudCatalog.Notes.Count} note(s).");
                }

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
                // PHASE 1: FIRST REMOVE CLOUD FILES (Deletions, Trashed & Zombie Untitled)
                // ─────────────────────────────────────────────────────────────
                ReportStage("Phase 1: Removing deleted & trashed cloud files...");

                // A. Synchronize local folders to cloud (ensures blank local folders exist on cloud)
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
                        if (relDir.Split('/', '\\').Any(s => s.Equals("QuickNote", StringComparison.OrdinalIgnoreCase)) || dirName.Equals("QuickNote", StringComparison.OrdinalIgnoreCase)) continue;

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

                string currentDeviceName = "Device";
                try
                {
                    currentDeviceName = OperatingSystem.IsAndroid() ? "Android-Device" : (Environment.MachineName ?? "Device");
                }
                catch { }

                // Scan local changes immediately so any locally moved or deleted notes are marked PendingDelete/PendingMove before Phase 1 deletions run
                var pendingUploads = _manifestManager.ScanLocalChanges(_notesRoot);
                SyncLogger.Log($"Found {pendingUploads.Count} pending local note change(s) for upload.");

                // B0. Propagate pending local moves/renames to Google Drive atomically
                var pendingMoves = _manifestManager.GetPendingMoves();
                foreach (var move in pendingMoves)
                {
                    try
                    {
                        var newRelDir = Path.GetDirectoryName(move.RelativePath)?.Replace('\\', '/').Trim('/') ?? "";
                        var targetFolderId = await _apiClient.EnsureFolderPathCachedAsync(folderId, newRelDir, folderCache);
                        var oldRelDir = Path.GetDirectoryName(move.OldRelativePath ?? "")?.Replace('\\', '/').Trim('/') ?? "";
                        folderCache.TryGetValue(oldRelDir, out var oldFolderId);

                        var newName = Path.GetFileName(move.RelativePath);
                        Console.WriteLine($"[GoogleCloudSyncEngine] Moving remote note from '{move.OldRelativePath}' to '{move.RelativePath}' ({move.RemoteId})...");

                        var updatedRemote = await _apiClient.MoveOrRenameNoteAsync(move.RemoteId, newName, targetFolderId, oldFolderId);
                        var localFullPath = Path.Combine(_notesRoot, move.RelativePath);
                        var localHash = SyncManifestManager.ComputeFileHash(localFullPath);
                        var localModTime = File.Exists(localFullPath) ? File.GetLastWriteTimeUtc(localFullPath) : DateTime.UtcNow;
                        var localNoteId = SyncManifestManager.ReadNoteIdFromFile(localFullPath);

                        _manifestManager.RecordSuccessfulMove(move.RelativePath, updatedRemote.Id, updatedRemote.Md5Checksum ?? string.Empty, updatedRemote.ModifiedTime.UtcDateTime, localHash, localModTime, localNoteId);

                        // Update remote maps so old path is gone and new path is present
                        if (!string.IsNullOrEmpty(move.OldRelativePath))
                        {
                            remoteFileMap.Remove(move.OldRelativePath);
                            cloudCatalog?.Notes.Remove(move.OldRelativePath);
                        }
                        remoteFileMap[move.RelativePath] = updatedRemote;
                        if (cloudCatalog != null)
                        {
                            cloudCatalog.Notes[move.RelativePath] = new CloudNoteEntry
                            {
                                NoteId = localNoteId?.ToString("D") ?? string.Empty,
                                RelativePath = move.RelativePath,
                                DriveFileId = updatedRemote.Id,
                                Title = Path.GetFileNameWithoutExtension(newName),
                                ContentHash = updatedRemote.Md5Checksum ?? string.Empty,
                                LastModifiedUtc = updatedRemote.ModifiedTime.UtcDateTime,
                                SizeBytes = long.TryParse(updatedRemote.Size, out var sz) ? sz : 0
                            };
                        }
                        uploaded++;
                        SyncLogger.Log($"Moved remote note on Google Drive from '{move.OldRelativePath}' to '{move.RelativePath}'");
                    }
                    catch (Exception moveEx)
                    {
                        Console.WriteLine($"[GoogleCloudSyncEngine] MoveOrRenameNoteAsync error for '{move.RelativePath}': {moveEx.Message}");
                        SyncLogger.Log($"Move failed for '{move.RelativePath}', will fall back to normal upload/delete: {moveEx.Message}");
                    }
                }

                // B. Propagate pending local deletions to Google Drive and PURGE from remoteFileMap and catalog
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
                        remoteFileMap.Remove(del.RelativePath);
                        if (cloudCatalog != null)
                        {
                            cloudCatalog.Notes.Remove(del.RelativePath);
                            cloudCatalog.Tombstones[del.RelativePath] = new CloudTombstoneEntry
                            {
                                RelativePath = del.RelativePath,
                                DeletedUtc = DateTime.UtcNow,
                                DeletedByDevice = currentDeviceName
                            };
                        }
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

                // D. Clean up any remote notes that are currently in local trash (or tombstoned)
                foreach (var kvp in remoteFileMap.ToList())
                {
                    var relKey = kvp.Key;
                    var baseName = Path.GetFileName(relKey);
                    bool isTrashed = trashedOriginalPaths.Contains(relKey) ||
                                     trashedOriginalPaths.Contains(baseName) ||
                                     (cloudCatalog?.Tombstones.ContainsKey(relKey) == true);

                    if (isTrashed)
                    {
                        try
                        {
                            await _apiClient.DeleteNoteAsync(kvp.Value.Id);
                            SyncLogger.Log($"Deleted remote trashed note: '{relKey}'");
                        }
                        catch { }
                        remoteFileMap.Remove(relKey);
                        cloudCatalog?.Notes.Remove(relKey);
                        _manifestManager.RemoveEntry(relKey);
                    }
                }

                // E. Clean up remote empty Untitled zombie files from cloud (NEVER allow empty untitled files in cloud)
                foreach (var kvp in remoteFileMap.ToList())
                {
                    var fName = Path.GetFileName(kvp.Key);
                    bool isUntitled = fName.StartsWith("Untitled", StringComparison.OrdinalIgnoreCase);
                    bool isEmpty = kvp.Value.Size == "0" || string.IsNullOrEmpty(kvp.Value.Size) || kvp.Value.Size == "null";
                    long.TryParse(kvp.Value.Size, out var parsedSize);
                    var localTarget = Path.Combine(_notesRoot, kvp.Key.Replace('/', Path.DirectorySeparatorChar));
                    bool localExists = File.Exists(localTarget);
                    if (isUntitled && (isEmpty || (!localExists && parsedSize < 60)))
                    {
                        try
                        {
                            await _apiClient.DeleteNoteAsync(kvp.Value.Id);
                            SyncLogger.Log($"Purged empty/ghost untitled note from cloud: '{kvp.Key}'");
                        }
                        catch { }
                        remoteFileMap.Remove(kvp.Key);
                        cloudCatalog?.Notes.Remove(kvp.Key);
                        if (cloudCatalog != null)
                        {
                            cloudCatalog.Tombstones[kvp.Key] = new CloudTombstoneEntry
                            {
                                RelativePath = kvp.Key,
                                DeletedUtc = DateTime.UtcNow,
                                DeletedByDevice = currentDeviceName
                            };
                        }
                        _manifestManager.RemoveEntry(kvp.Key);
                    }
                }

                // ─────────────────────────────────────────────────────────────
                // PHASE 2: UPLOAD LOCAL NEW & MODIFIED NOTES
                // ─────────────────────────────────────────────────────────────
                ReportStage("Phase 2: Uploading new & modified notes to Google Drive...");

                pendingUploads = _manifestManager.ScanLocalChanges(_notesRoot);
                SyncLogger.Log($"Found {pendingUploads.Count} pending local note change(s) for upload.");

                // Reconcile: Ensure all local notes not present in Google Drive are handled (upload or remote deletion)
                foreach (var entry in manifest.Entries.Values.ToList())
                {
                    var fileKey = entry.RelativePath.Replace('\\', '/').Trim('/');
                    if (fileKey.Split('/', '\\').Take(fileKey.Split('/', '\\').Length - 1).Any(s => s.Equals("QuickNote", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (!remoteFileMap.ContainsKey(fileKey) && entry.Status != SyncStatus.PendingDelete)
                    {
                        var localFullPath = Path.Combine(_notesRoot, entry.RelativePath);
                        if (File.Exists(localFullPath))
                        {
                            var currentLocalHash = SyncManifestManager.ComputeFileHash(localFullPath);
                            bool localModified = entry.Status == SyncStatus.PendingUpload ||
                                                 !string.Equals(currentLocalHash, entry.LocalHash, StringComparison.OrdinalIgnoreCase);

                            if (!string.IsNullOrEmpty(entry.RemoteId) && !localModified)
                            {
                                // Note was previously synced and was not edited locally, but is now gone from Google Drive.
                                // This indicates it was deleted on another device or directly in Google Drive.
                                try
                                {
                                    await _trashBinService.MoveToTrashAsync(localFullPath);
                                    _manifestManager.RemoveEntry(entry.RelativePath);
                                    downloaded++;
                                    SyncLogger.Log($"Remote deletion detected: Moved '{entry.RelativePath}' to local trash.");
                                }
                                catch (Exception trEx)
                                {
                                    Console.WriteLine($"[GoogleCloudSyncEngine] Error moving remotely deleted note to trash: {trEx.Message}");
                                }
                            }
                            else
                            {
                                entry.Status = SyncStatus.PendingUpload;
                                entry.RemoteId = string.Empty;
                            }
                        }
                    }
                }

                foreach (var entry in manifest.Entries.Values.ToList())
                {
                    if (entry.Status != SyncStatus.PendingUpload) continue;

                    var fileKey = entry.RelativePath.Replace('\\', '/').Trim('/');
                    if (fileKey.Split('/', '\\').Take(fileKey.Split('/', '\\').Length - 1).Any(s => s.Equals("QuickNote", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var localFullPath = Path.Combine(_notesRoot, entry.RelativePath);

                    if (!File.Exists(localFullPath)) continue;

                    // If note is in local trash, don't upload it!
                    if (trashedOriginalPaths.Contains(fileKey))
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
                        var localNoteId = SyncManifestManager.ReadNoteIdFromFile(localFullPath);

                        if (remoteFileMap.TryGetValue(fileKey, out var existingRemote))
                        {
                            // Update existing cloud note
                            var updatedRemote = await _apiClient.UpdateExistingNoteAsync(existingRemote.Id, localContent);
                            _manifestManager.RecordSuccessfulUpload(entry.RelativePath, updatedRemote.Id, updatedRemote.Md5Checksum ?? string.Empty, updatedRemote.ModifiedTime.UtcDateTime, localHash, localModTime, localNoteId);
                            uploaded++;
                            SyncLogger.Log($"Uploaded modified note: '{entry.RelativePath}'");
                        }
                        else
                        {
                            // Upload new cloud note
                            var createdRemote = await _apiClient.UploadNewNoteAsync(targetFolderId, baseName, localContent);
                            _manifestManager.RecordSuccessfulUpload(entry.RelativePath, createdRemote.Id, createdRemote.Md5Checksum ?? string.Empty, createdRemote.ModifiedTime.UtcDateTime, localHash, localModTime, localNoteId);
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
                // PHASE 3: CLOUD DOWNLOAD (Items 2 & 3)
                // ─────────────────────────────────────────────────────────────
                ReportStage("Phase 3: Downloading remote notes & folders...");

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
                foreach (var kvp in remoteFileMap.ToList())
                {
                    var relPath = kvp.Key;
                    var safeRelPath = SanitizeRelativePath(relPath);
                    var remote = kvp.Value;
                    var localFullPath = Path.Combine(_notesRoot, safeRelPath);
                    bool localExists = File.Exists(localFullPath);

                    if (!localExists)
                    {
                        // If item is in trash or tombstoned, delete remote copy to prevent resurrection
                        if (trashedOriginalPaths.Contains(safeRelPath) ||
                            trashedOriginalPaths.Contains(relPath) ||
                            trashedOriginalPaths.Contains(Path.GetFileName(safeRelPath)) ||
                            (cloudCatalog?.Tombstones.ContainsKey(relPath) == true) ||
                            (cloudCatalog?.Tombstones.ContainsKey(safeRelPath) == true))
                        {
                            try
                            {
                                await _apiClient.DeleteNoteAsync(remote.Id);
                                SyncLogger.Log($"Removed trashed/tombstoned note from cloud: '{safeRelPath}'");
                            }
                            catch { }
                            remoteFileMap.Remove(relPath);
                            cloudCatalog?.Notes.Remove(relPath);
                            continue;
                        }

                        // Check if remote note is an empty untitled note (prevent downloading unwanted untitled files)
                        bool isUntitled = Path.GetFileName(safeRelPath).StartsWith("Untitled", StringComparison.OrdinalIgnoreCase);
                        long.TryParse(remote.Size, out var remoteByteSize);
                        if (isUntitled && (remote.Size == "0" || string.IsNullOrEmpty(remote.Size) || remote.Size == "null" || remoteByteSize < 10))
                        {
                            try
                            {
                                await _apiClient.DeleteNoteAsync(remote.Id);
                                SyncLogger.Log($"Purged empty untitled note from cloud: '{safeRelPath}'");
                            }
                            catch { }
                            remoteFileMap.Remove(relPath);
                            cloudCatalog?.Notes.Remove(relPath);
                            if (cloudCatalog != null)
                            {
                                cloudCatalog.Tombstones[safeRelPath] = new CloudTombstoneEntry
                                {
                                    RelativePath = safeRelPath,
                                    DeletedUtc = DateTime.UtcNow,
                                    DeletedByDevice = currentDeviceName
                                };
                            }
                            _manifestManager.RemoveEntry(safeRelPath);
                            continue;
                        }

                        // Guard: check if this remote file is already mapped to a different local file in manifest (e.g. moved locally)
                        var catalogNoteEntry = cloudCatalog?.Notes.Values.FirstOrDefault(c => string.Equals(c.DriveFileId, remote.Id, StringComparison.OrdinalIgnoreCase));
                        var remoteNoteId = catalogNoteEntry?.NoteId;

                        var existingMappedEntry = manifest.Entries.Values.FirstOrDefault(e =>
                            (string.Equals(e.RemoteId, remote.Id, StringComparison.OrdinalIgnoreCase) ||
                             (!string.IsNullOrEmpty(remoteNoteId) && e.NoteId.HasValue && string.Equals(e.NoteId.Value.ToString("D"), remoteNoteId, StringComparison.OrdinalIgnoreCase))) &&
                            !string.Equals(e.RelativePath, safeRelPath, StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(Path.Combine(_notesRoot, e.RelativePath)));

                        if (existingMappedEntry != null)
                        {
                            // This remote note already exists locally at existingMappedEntry.RelativePath!
                            // Do not resurrect it at safeRelPath!
                            SyncLogger.Log($"Skipping download for '{safeRelPath}' as it is already present locally at '{existingMappedEntry.RelativePath}'");
                            continue;
                        }

                        // Download new note from cloud
                        try
                        {
                            var content = await _apiClient.DownloadNoteTextAsync(remote.Id);

                            // If downloaded content of an untitled note is empty or default template, do NOT litter local disk
                            bool isTrivialUntitled = string.IsNullOrWhiteSpace(content) ||
                                content.Trim().Equals("# Untitled", StringComparison.OrdinalIgnoreCase) ||
                                content.Trim().Equals("#", StringComparison.OrdinalIgnoreCase) ||
                                content.Trim().Equals("Untitled", StringComparison.OrdinalIgnoreCase) ||
                                (content.Trim().StartsWith("# Untitled", StringComparison.OrdinalIgnoreCase) && content.Trim().Length < 25);

                            if (isUntitled && isTrivialUntitled)
                            {
                                try { await _apiClient.DeleteNoteAsync(remote.Id); } catch { }
                                remoteFileMap.Remove(relPath);
                                cloudCatalog?.Notes.Remove(relPath);
                                if (cloudCatalog != null)
                                {
                                    cloudCatalog.Tombstones[safeRelPath] = new CloudTombstoneEntry
                                    {
                                        RelativePath = safeRelPath,
                                        DeletedUtc = DateTime.UtcNow,
                                        DeletedByDevice = currentDeviceName
                                    };
                                }
                                _manifestManager.RemoveEntry(safeRelPath);
                                continue;
                            }

                            var dir = Path.GetDirectoryName(localFullPath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            await _atomicFileWriter.WriteAllTextAsync(localFullPath, content);
                            try { File.SetLastWriteTimeUtc(localFullPath, remote.ModifiedTime.UtcDateTime); } catch { }

                            var downloadedNoteId = MarkdownParser.ExtractIdFromFrontMatter(MarkdownParser.ParseFrontMatter(content).frontMatter);
                            var hash = SyncManifestManager.ComputeFileHash(localFullPath);
                            _manifestManager.RecordSuccessfulDownload(safeRelPath, remote.Id, remote.Md5Checksum ?? string.Empty, remote.ModifiedTime.UtcDateTime, hash, remote.ModifiedTime.UtcDateTime, downloadedNoteId);
                            downloaded++;
                            SyncLogger.Log($"Downloaded remote note: '{safeRelPath}'");
                        }
                        catch (Exception dlEx)
                        {
                            Console.WriteLine($"[GoogleCloudSyncEngine] Download error for '{safeRelPath}': {dlEx.Message}");
                            SyncLogger.Log($"Download error for '{safeRelPath}': {dlEx.Message}");
                        }
                    }
                    else
                    {
                        // File exists both locally and remotely: check remote updates
                        manifest.Entries.TryGetValue(safeRelPath, out var entry);
                        if (entry == null) manifest.Entries.TryGetValue(relPath, out entry);
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
                                SyncLogger.Log($"Conflict detected & resolved with conflict copy: '{safeRelPath}'");

                                // Keep local as primary and re-upload to cloud
                                var localContent = await File.ReadAllTextAsync(localFullPath);
                                var updatedRemote = await _apiClient.UpdateExistingNoteAsync(remote.Id, localContent);
                                var localModTime = File.GetLastWriteTimeUtc(localFullPath);
                                _manifestManager.RecordSuccessfulUpload(safeRelPath, updatedRemote.Id, updatedRemote.Md5Checksum ?? string.Empty, updatedRemote.ModifiedTime.UtcDateTime, localHash, localModTime);
                                uploaded++;
                            }
                            catch (Exception cEx)
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Conflict handling error for '{safeRelPath}': {cEx.Message}");
                            }
                        }
                        else if (remoteModified && !localModified)
                        {
                            // Remote edited on another device -> download update
                            try
                            {
                                var content = await _apiClient.DownloadNoteTextAsync(remote.Id);
                                await _atomicFileWriter.WriteAllTextAsync(localFullPath, content);
                                try { File.SetLastWriteTimeUtc(localFullPath, remote.ModifiedTime.UtcDateTime); } catch { }

                                var newHash = SyncManifestManager.ComputeFileHash(localFullPath);
                                _manifestManager.RecordSuccessfulDownload(safeRelPath, remote.Id, remote.Md5Checksum ?? string.Empty, remote.ModifiedTime.UtcDateTime, newHash, remote.ModifiedTime.UtcDateTime);
                                downloaded++;
                                SyncLogger.Log($"Updated local note from remote changes: '{safeRelPath}'");
                            }
                            catch (Exception dlEx)
                            {
                                Console.WriteLine($"[GoogleCloudSyncEngine] Remote update error for '{safeRelPath}': {dlEx.Message}");
                            }
                        }
                    }
                }

                // ─────────────────────────────────────────────────────────────
                // PHASE 4: CATALOG PRUNING, UPDATE, MANIFEST & COMPLETION
                // ─────────────────────────────────────────────────────────────
                if (cloudCatalog != null)
                {
                    // Prune any deleted, trashed, tombstoned, or ghost notes from cloudCatalog.Notes so they never resurrect or duplicate
                    foreach (var catalogKey in cloudCatalog.Notes.Keys.ToList())
                    {
                        var fullP = Path.Combine(_notesRoot, catalogKey);
                        bool existsLocally = File.Exists(fullP);
                        bool existsRemotely = remoteFileMap.ContainsKey(catalogKey);
                        bool isTrashed = trashedOriginalPaths.Contains(catalogKey) || trashedOriginalPaths.Contains(Path.GetFileName(catalogKey));
                        bool isTombstone = cloudCatalog.Tombstones.ContainsKey(catalogKey);

                        if (isTrashed || isTombstone || (!existsLocally && !existsRemotely))
                        {
                            cloudCatalog.Notes.Remove(catalogKey);
                        }
                    }

                    // Prune tombstones older than 30 days to keep the catalog JSON compact over time
                    var tombstoneCutoff = DateTime.UtcNow.AddDays(-30);
                    var expiredTombstones = cloudCatalog.Tombstones
                        .Where(kvp => kvp.Value.DeletedUtc < tombstoneCutoff)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    foreach (var expKey in expiredTombstones)
                    {
                        cloudCatalog.Tombstones.Remove(expKey);
                    }

                    bool catalogNeedsUpload = uploaded > 0 || downloaded > 0 || string.IsNullOrEmpty(catalogFileId);
                    if (catalogNeedsUpload)
                    {
                        try
                        {
                            cloudCatalog.Folders = folderCache;
                            cloudCatalog.CatalogRevision++;
                            cloudCatalog.LastUpdatedUtc = DateTime.UtcNow;
                            string deviceName = "Android-Device";
                            try
                            {
                                deviceName = OperatingSystem.IsAndroid() ? "Android-Device" : (Environment.MachineName ?? "Device");
                            }
                            catch { }
                            cloudCatalog.UpdatedByDevice = deviceName;

                            // Update note catalog entries from current manifest
                            foreach (var entry in manifest.Entries.Values)
                            {
                                if (!string.IsNullOrEmpty(entry.RemoteId) && entry.Status == SyncStatus.Synced)
                                {
                                    var fullP = Path.Combine(_notesRoot, entry.RelativePath);
                                    var sz = File.Exists(fullP) ? new FileInfo(fullP).Length : 0;
                                    cloudCatalog.Notes[entry.RelativePath] = new CloudNoteEntry
                                    {
                                        RelativePath = entry.RelativePath,
                                        DriveFileId = entry.RemoteId,
                                        Title = Path.GetFileNameWithoutExtension(entry.RelativePath),
                                        ContentHash = entry.LocalHash,
                                        SizeBytes = sz,
                                        LastModifiedUtc = entry.LocalLastModified,
                                        Revision = cloudCatalog.Notes.TryGetValue(entry.RelativePath, out var oldN) ? oldN.Revision + 1 : 1
                                    };
                                }
                            }

                            var catalogJsonOutput = JsonSerializer.Serialize(cloudCatalog, new JsonSerializerOptions { WriteIndented = true });
                            await _apiClient!.UploadOrUpdateCatalogAsync(folderId, catalogFileId, catalogJsonOutput);

                            // Save local catalog copy
                            var localCatalogPath = Path.Combine(_notesRoot, ".sync_catalog.json");
                            await _atomicFileWriter.WriteAllTextAsync(localCatalogPath, catalogJsonOutput);
                            SyncLogger.Log($"Central cloud catalog saved (Revision: {cloudCatalog.CatalogRevision}).");
                        }
                        catch (Exception catSaveEx)
                        {
                            SyncLogger.Log($"Warning saving cloud catalog: {catSaveEx.Message}");
                        }
                    }
                }

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
                _isSyncing = false;
                _syncLock.Release();
                if (_trailingSyncRequested)
                {
                    _trailingSyncRequested = false;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(500);
                            await PerformSyncAsync();
                        }
                        catch (Exception tEx)
                        {
                            Console.WriteLine($"[GoogleCloudSyncEngine] Trailing sync error: {tEx.Message}");
                        }
                    });
                }
            }
        }

        public void RecordFileMoved(string oldRelPath, string newRelPath)
        {
            _manifestManager.RecordFileMoved(oldRelPath, newRelPath);
        }

        public void RecordFileDeleted(string relPath)
        {
            _manifestManager.RecordLocalDeletion(relPath);
        }

        private static string SanitizeRelativePath(string relPath)
        {
            if (string.IsNullOrWhiteSpace(relPath)) return string.Empty;
            var parts = relPath.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var ext = Path.GetExtension(parts[i]);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(parts[i]);
                if (!string.IsNullOrEmpty(ext))
                {
                    parts[i] = FileNameSanitizer.Sanitize(nameWithoutExt) + ext;
                }
                else
                {
                    parts[i] = FileNameSanitizer.Sanitize(parts[i]);
                }
            }
            return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
        }
    }
}
