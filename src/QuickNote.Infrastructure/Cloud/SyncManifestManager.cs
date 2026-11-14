using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using QuickNote.Core.Models;

namespace QuickNote.Infrastructure.Cloud
{
    public class SyncManifestManager
    {
        private readonly string _manifestPath;
        private SyncManifest _manifest = new();

        public SyncManifest Manifest => _manifest;

        public SyncManifestManager(string notesRoot)
        {
            _manifestPath = Path.Combine(notesRoot, ".sync_manifest.json");
        }

        public async Task<SyncManifest> LoadManifestAsync()
        {
            try
            {
                if (File.Exists(_manifestPath))
                {
                    var json = await File.ReadAllTextAsync(_manifestPath);
                    var loaded = JsonSerializer.Deserialize<SyncManifest>(json);
                    if (loaded != null)
                    {
                        _manifest = loaded;
                        _manifest.Entries ??= new Dictionary<string, SyncFileEntry>(StringComparer.OrdinalIgnoreCase);
                        return _manifest;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncManifestManager] Error loading manifest: {ex.Message}");
            }

            _manifest = new SyncManifest();
            return _manifest;
        }

        public async Task SaveManifestAsync()
        {
            try
            {
                _manifest.LastSyncTime = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(_manifest, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_manifestPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncManifestManager] Error saving manifest: {ex.Message}");
            }
        }

        public static string ComputeFileHash(string fullPath)
        {
            if (!File.Exists(fullPath)) return string.Empty;
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(fullPath);
                var hash = sha256.ComputeHash(stream);
                return Convert.ToHexString(hash);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string ComputeTextHash(string text)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        /// <summary>
        /// Scans local notes directory and marks new or modified files as PendingUpload.
        /// Returns a list of relative paths that are pending upload.
        /// </summary>
        public List<string> ScanLocalChanges(string notesRoot)
        {
            var pendingUploads = new List<string>();
            if (!Directory.Exists(notesRoot)) return pendingUploads;

            var localFiles = Directory.GetFiles(notesRoot, "*.md", SearchOption.AllDirectories);
            var currentLocalRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in localFiles)
            {
                var fileName = Path.GetFileName(file);
                // Skip hidden files, system files, or trash bin files
                if (fileName.StartsWith(".") || file.Contains(".trash", StringComparison.OrdinalIgnoreCase))
                    continue;

                var relPath = Path.GetRelativePath(notesRoot, file).Replace('\\', '/');
                currentLocalRelPaths.Add(relPath);

                var localHash = ComputeFileHash(file);
                var localModified = File.GetLastWriteTimeUtc(file);

                if (!_manifest.Entries.TryGetValue(relPath, out var entry))
                {
                    // Brand new local note
                    entry = new SyncFileEntry
                    {
                        RelativePath = relPath,
                        LocalHash = localHash,
                        LocalLastModified = localModified,
                        Status = SyncStatus.PendingUpload
                    };
                    _manifest.Entries[relPath] = entry;
                    pendingUploads.Add(relPath);
                }
                else
                {
                    // Existing note - check if modified locally
                    if (!string.Equals(entry.LocalHash, localHash, StringComparison.OrdinalIgnoreCase) ||
                        localModified > entry.LocalLastModified.AddSeconds(1))
                    {
                        entry.LocalHash = localHash;
                        entry.LocalLastModified = localModified;
                        entry.Status = SyncStatus.PendingUpload;
                        pendingUploads.Add(relPath);
                    }
                }
            }

            // Remove entries for files deleted locally
            var toRemove = new List<string>();
            foreach (var kvp in _manifest.Entries)
            {
                if (!currentLocalRelPaths.Contains(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var r in toRemove)
            {
                _manifest.Entries.Remove(r);
            }

            return pendingUploads;
        }

        public void RecordSuccessfulUpload(string relPath, string remoteId, string remoteETag, DateTime remoteModified, string localHash, DateTime localModified)
        {
            if (!_manifest.Entries.TryGetValue(relPath, out var entry))
            {
                entry = new SyncFileEntry { RelativePath = relPath };
                _manifest.Entries[relPath] = entry;
            }

            entry.RemoteId = remoteId;
            entry.RemoteETag = remoteETag;
            entry.RemoteLastModified = remoteModified;
            entry.LocalHash = localHash;
            entry.LocalLastModified = localModified;
            entry.Status = SyncStatus.Synced;
        }

        public void RecordSuccessfulDownload(string relPath, string remoteId, string remoteETag, DateTime remoteModified, string localHash, DateTime localModified)
        {
            if (!_manifest.Entries.TryGetValue(relPath, out var entry))
            {
                entry = new SyncFileEntry { RelativePath = relPath };
                _manifest.Entries[relPath] = entry;
            }

            entry.RemoteId = remoteId;
            entry.RemoteETag = remoteETag;
            entry.RemoteLastModified = remoteModified;
            entry.LocalHash = localHash;
            entry.LocalLastModified = localModified;
            entry.Status = SyncStatus.Synced;
        }

        public static string CreateConflictCopy(string fullPath, string remoteContent, DateTime remoteModified)
        {
            var dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fullPath);
            var ext = Path.GetExtension(fullPath);
            var timestamp = remoteModified.ToString("yyyyMMdd-HHmmss");
            var conflictPath = Path.Combine(dir, $"{nameWithoutExt}.conflict-{timestamp}{ext}");

            try
            {
                File.WriteAllText(conflictPath, remoteContent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncManifestManager] Failed to create conflict file: {ex.Message}");
            }
            return conflictPath;
        }
    }
}
