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

        public void SaveManifest()
        {
            try
            {
                _manifest.LastSyncTime = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(_manifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_manifestPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncManifestManager] Error saving manifest: {ex.Message}");
            }
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

        public static Guid? ReadNoteIdFromFile(string fullPath)
        {
            if (!File.Exists(fullPath)) return null;
            try
            {
                using var stream = File.OpenRead(fullPath);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var firstLine = reader.ReadLine()?.Trim();
                if (firstLine == "---")
                {
                    for (int i = 0; i < 20; i++)
                    {
                        var line = reader.ReadLine();
                        if (line == null || line.Trim() == "---") break;
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("id:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase))
                        {
                            var colonIdx = trimmed.IndexOf(':');
                            if (colonIdx >= 0)
                            {
                                var val = trimmed.Substring(colonIdx + 1).Trim().Trim('\'', '\"');
                                if (Guid.TryParse(val, out var guid)) return guid;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
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

                // Skip any recursive QuickNote nested subfolder
                var segments = relPath.Split('/');
                if (segments.Take(segments.Length - 1).Any(s => s.Equals("QuickNote", StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Always register that this local file physically exists so deletions can be tracked
                currentLocalRelPaths.Add(relPath);

                // Skip upload for empty Untitled notes (never upload empty untitled drafts or zero-byte placeholders)
                if (fileName.StartsWith("Untitled", StringComparison.OrdinalIgnoreCase))
                {
                    var fi = new FileInfo(file);
                    if (fi.Length == 0)
                        continue;

                    try
                    {
                        var text = File.ReadAllText(file);
                        if (string.IsNullOrWhiteSpace(text))
                            continue;
                    }
                    catch { }
                }

                var localHash = ComputeFileHash(file);
                var localModified = File.GetLastWriteTimeUtc(file);
                var localNoteId = ReadNoteIdFromFile(file);

                if (!_manifest.Entries.TryGetValue(relPath, out var entry))
                {
                    // Check if an existing entry was moved to this path:
                    // 1) By matching NoteId (if present in frontmatter)
                    // 2) Or by matching LocalHash (where old path is absent from disk)
                    var movedFromEntry = _manifest.Entries.Values.FirstOrDefault(e =>
                        (!File.Exists(Path.Combine(notesRoot, e.RelativePath)) && !currentLocalRelPaths.Contains(e.RelativePath)) &&
                        ((localNoteId.HasValue && e.NoteId == localNoteId.Value) ||
                         (!string.IsNullOrEmpty(e.LocalHash) && string.Equals(e.LocalHash, localHash, StringComparison.OrdinalIgnoreCase))));

                    if (movedFromEntry != null)
                    {
                        var oldKey = movedFromEntry.RelativePath;
                        _manifest.Entries.Remove(oldKey);
                        movedFromEntry.OldRelativePath = oldKey;
                        movedFromEntry.RelativePath = relPath;
                        movedFromEntry.LocalHash = localHash;
                        movedFromEntry.LocalLastModified = localModified;
                        if (localNoteId.HasValue) movedFromEntry.NoteId = localNoteId.Value;

                        if (!string.IsNullOrEmpty(movedFromEntry.RemoteId))
                        {
                            movedFromEntry.Status = SyncStatus.PendingMove;
                        }
                        else
                        {
                            movedFromEntry.Status = SyncStatus.PendingUpload;
                        }
                        _manifest.Entries[relPath] = movedFromEntry;
                        pendingUploads.Add(relPath);
                    }
                    else
                    {
                        // Brand new local note
                        entry = new SyncFileEntry
                        {
                            NoteId = localNoteId,
                            RelativePath = relPath,
                            LocalHash = localHash,
                            LocalLastModified = localModified,
                            Status = SyncStatus.PendingUpload
                        };
                        _manifest.Entries[relPath] = entry;
                        pendingUploads.Add(relPath);
                    }
                }
                else
                {
                    if (localNoteId.HasValue) entry.NoteId = localNoteId.Value;
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

            // Track deletions for files deleted locally
            var toRemove = new List<string>();
            foreach (var kvp in _manifest.Entries)
            {
                if (!currentLocalRelPaths.Contains(kvp.Key))
                {
                    if (kvp.Value.Status == SyncStatus.PendingMove)
                    {
                        continue;
                    }
                    if (!string.IsNullOrEmpty(kvp.Value.RemoteId))
                    {
                        // File was previously synced to cloud: mark for remote deletion
                        kvp.Value.Status = SyncStatus.PendingDelete;
                    }
                    else
                    {
                        // Never synced to cloud: safely forget
                        toRemove.Add(kvp.Key);
                    }
                }
            }
            foreach (var r in toRemove)
            {
                _manifest.Entries.Remove(r);
            }

            return pendingUploads;
        }

        public List<SyncFileEntry> GetPendingDeletions()
        {
            var list = new List<SyncFileEntry>();
            foreach (var entry in _manifest.Entries.Values)
            {
                if (entry.Status == SyncStatus.PendingDelete && !string.IsNullOrEmpty(entry.RemoteId))
                {
                    list.Add(entry);
                }
            }
            return list;
        }

        public List<SyncFileEntry> GetPendingMoves()
        {
            var list = new List<SyncFileEntry>();
            foreach (var entry in _manifest.Entries.Values)
            {
                if (entry.Status == SyncStatus.PendingMove && !string.IsNullOrEmpty(entry.RemoteId) && !string.IsNullOrEmpty(entry.OldRelativePath))
                {
                    list.Add(entry);
                }
            }
            return list;
        }

        public void RemoveEntry(string relPath)
        {
            _manifest.Entries.Remove(relPath);
        }

        public void RecordLocalDeletion(string relPath)
        {
            var key = relPath.Replace('\\', '/').Trim('/');
            if (_manifest.Entries.TryGetValue(key, out var entry))
            {
                if (!string.IsNullOrEmpty(entry.RemoteId))
                {
                    entry.Status = SyncStatus.PendingDelete;
                }
                else
                {
                    _manifest.Entries.Remove(key);
                }
            }
        }

        public void RecordFileMoved(string oldRelPath, string newRelPath)
        {
            var oldKey = oldRelPath.Replace('\\', '/').Trim('/');
            var newKey = newRelPath.Replace('\\', '/').Trim('/');
            if (string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase)) return;

            if (_manifest.Entries.TryGetValue(oldKey, out var oldEntry))
            {
                _manifest.Entries.Remove(oldKey);
                oldEntry.OldRelativePath = oldKey;
                oldEntry.RelativePath = newKey;
                oldEntry.LocalLastModified = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(oldEntry.RemoteId))
                {
                    oldEntry.Status = SyncStatus.PendingMove;
                }
                _manifest.Entries[newKey] = oldEntry;
                SaveManifest();
            }
        }

        public void RecordSuccessfulMove(string newRelPath, string remoteId, string remoteETag, DateTime remoteModified, string localHash, DateTime localModified, Guid? noteId = null)
        {
            var key = newRelPath.Replace('\\', '/').Trim('/');
            if (!_manifest.Entries.TryGetValue(key, out var entry))
            {
                entry = new SyncFileEntry { RelativePath = key };
                _manifest.Entries[key] = entry;
            }

            if (noteId.HasValue) entry.NoteId = noteId.Value;
            entry.OldRelativePath = null;
            entry.RemoteId = remoteId;
            entry.RemoteETag = remoteETag;
            entry.RemoteLastModified = remoteModified;
            entry.LocalHash = localHash;
            entry.LocalLastModified = localModified;
            entry.Status = SyncStatus.Synced;
        }

        public void RecordSuccessfulUpload(string relPath, string remoteId, string remoteETag, DateTime remoteModified, string localHash, DateTime localModified, Guid? noteId = null)
        {
            if (!_manifest.Entries.TryGetValue(relPath, out var entry))
            {
                entry = new SyncFileEntry { RelativePath = relPath };
                _manifest.Entries[relPath] = entry;
            }

            if (noteId.HasValue) entry.NoteId = noteId.Value;
            entry.RemoteId = remoteId;
            entry.RemoteETag = remoteETag;
            entry.RemoteLastModified = remoteModified;
            entry.LocalHash = localHash;
            entry.LocalLastModified = localModified;
            entry.Status = SyncStatus.Synced;
        }

        public void RecordSuccessfulDownload(string relPath, string remoteId, string remoteETag, DateTime remoteModified, string localHash, DateTime localModified, Guid? noteId = null)
        {
            if (!_manifest.Entries.TryGetValue(relPath, out var entry))
            {
                entry = new SyncFileEntry { RelativePath = relPath };
                _manifest.Entries[relPath] = entry;
            }

            if (noteId.HasValue) entry.NoteId = noteId.Value;
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
