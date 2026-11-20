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

namespace QuickNote.Infrastructure.FileSystem
{
    public class FileNoteRepository : INoteRepository
    {
        private string _notesRoot;
        private string _indexPath;
        private readonly IMarkdownParser _markdownParser;
        private readonly AtomicFileWriter _atomicFileWriter;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private NotesIndexCatalog? _cachedCatalog;
        private SqliteNoteIndexService _sqliteIndex;

        // Write-behind background queue
        private readonly System.Threading.Channels.Channel<string> _writeChannel = System.Threading.Channels.Channel.CreateUnbounded<string>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, NoteWriteRequest> _pendingWrites = new(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _writerTask;

        public string NotesRoot => _notesRoot;

        public void UpdateNotesRoot(string newNotesRoot)
        {
            if (string.IsNullOrWhiteSpace(newNotesRoot)) return;
            _notesRoot = newNotesRoot;
            if (!Directory.Exists(_notesRoot))
            {
                Directory.CreateDirectory(_notesRoot);
            }
            _indexPath = Path.Combine(_notesRoot, ".quicknote", "notes_index.json");
            _cachedCatalog = null;
            _sqliteIndex = new SqliteNoteIndexService(_notesRoot);
            _sqliteIndex.MigrateFromJsonIfEmpty(_indexPath);
        }

        public FileNoteRepository(string notesRoot, IMarkdownParser markdownParser, AtomicFileWriter? atomicFileWriter = null)
        {
            _notesRoot = notesRoot ?? throw new ArgumentNullException(nameof(notesRoot));
            _markdownParser = markdownParser ?? throw new ArgumentNullException(nameof(markdownParser));
            _atomicFileWriter = atomicFileWriter ?? new AtomicFileWriter();
            _indexPath = Path.Combine(_notesRoot, ".quicknote", "notes_index.json");

            if (!Directory.Exists(_notesRoot))
            {
                Directory.CreateDirectory(_notesRoot);
            }

            _sqliteIndex = new SqliteNoteIndexService(_notesRoot);
            _sqliteIndex.MigrateFromJsonIfEmpty(_indexPath);
            _writerTask = Task.Run(ProcessWriteQueueAsync);
        }

        public async Task<NoteDocument?> GetNoteByIdAsync(Guid id)
        {
            await _lock.WaitAsync();
            try
            {
                var catalog = await LoadIndexCatalogAsync();
                var entry = catalog.Notes.FirstOrDefault(e => e.Id == id);
                if (entry != null)
                {
                    var fullPath = Path.Combine(_notesRoot, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullPath))
                    {
                        var fi = new FileInfo(fullPath);
                        if (entry.LastFileWriteUtc == fi.LastWriteTimeUtc && entry.FileSize == fi.Length)
                        {
                            return entry.ToNoteDocument(_notesRoot);
                        }
                    }
                }
            }
            finally
            {
                _lock.Release();
            }

            var notes = await GetAllNotesAsync();
            return notes.FirstOrDefault(n => n.Id == id);
        }

        public async Task<NoteDocument?> GetNoteByPathAsync(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return null;
            }

            await _lock.WaitAsync();
            try
            {
                var catalog = await LoadIndexCatalogAsync();
                var rel = Path.GetRelativePath(_notesRoot, fullPath).Replace('\\', '/').TrimStart('/');
                var entry = catalog.Notes.FirstOrDefault(e => string.Equals(e.RelativePath.Replace('\\', '/').TrimStart('/'), rel, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    var fi = new FileInfo(fullPath);
                    if (entry.LastFileWriteUtc == fi.LastWriteTimeUtc && entry.FileSize == fi.Length)
                    {
                        return entry.ToNoteDocument(_notesRoot);
                    }
                }
            }
            finally
            {
                _lock.Release();
            }

            return await LoadNoteFromFileAsync(fullPath);
        }

        public async Task<List<NoteDocument>?> GetCachedNotesAsync()
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                var catalog = await LoadIndexCatalogAsync().ConfigureAwait(false);
                if (catalog.Notes == null || catalog.Notes.Count == 0)
                {
                    var sqliteEntries = _sqliteIndex.GetAllNotes();
                    if (sqliteEntries != null && sqliteEntries.Count > 0)
                    {
                        catalog.Notes = sqliteEntries;
                    }
                    else
                    {
                        return null;
                    }
                }

                var notes = new List<NoteDocument>(catalog.Notes.Count);
                foreach (var entry in catalog.Notes)
                {
                    if (!string.IsNullOrWhiteSpace(entry.RelativePath))
                    {
                        notes.Add(entry.ToNoteDocument(_notesRoot));
                    }
                }
                return notes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileNoteRepository] GetCachedNotesAsync failed: {ex.Message}");
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<IEnumerable<NoteDocument>> GetAllNotesAsync()
        {
            return await ReconcileDiskNotesAsync().ConfigureAwait(false);
        }

        public async Task<IEnumerable<NoteDocument>> ReconcileDiskNotesAsync()
        {
            await FlushPendingWritesAsync().ConfigureAwait(false);

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!Directory.Exists(_notesRoot))
                {
                    return Enumerable.Empty<NoteDocument>();
                }

                var catalog = await LoadIndexCatalogAsync().ConfigureAwait(false);
                var catalogMap = new Dictionary<string, NoteIndexEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in catalog.Notes)
                {
                    if (!string.IsNullOrWhiteSpace(entry.RelativePath))
                    {
                        var k = entry.RelativePath.Replace('\\', '/').TrimStart('/');
                        catalogMap[k] = entry;
                    }
                }

                // Restore any pending un-synced entries to disk
                var unsynced = catalog.Notes.Where(e => !e.IsDiskSynced).ToList();
                foreach (var unEntry in unsynced)
                {
                    try
                    {
                        var fullPath = Path.Combine(_notesRoot, unEntry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                        var d = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(d) && !Directory.Exists(d))
                        {
                            Directory.CreateDirectory(d);
                        }
                        await _atomicFileWriter.WriteAllTextAsync(fullPath, unEntry.Content ?? string.Empty).ConfigureAwait(false);
                        var fi = new FileInfo(fullPath);
                        unEntry.IsDiskSynced = true;
                        unEntry.FileSize = fi.Exists ? fi.Length : 0;
                        unEntry.LastFileWriteUtc = fi.Exists ? fi.LastWriteTimeUtc : DateTime.UtcNow;
                    }
                    catch { }
                }

                var files = Directory.GetFiles(_notesRoot, "*.md", SearchOption.AllDirectories);
                var notes = new List<NoteDocument>();
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var foundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool isDirty = unsynced.Count > 0;

                foreach (var file in files)
                {
                    // Skip hidden files or files inside .recovery or dot directories
                    var relative = Path.GetRelativePath(_notesRoot, file);
                    if (relative.StartsWith(".") || relative.Contains(Path.DirectorySeparatorChar + "."))
                    {
                        continue;
                    }

                    // Skip nested QuickNote recursion folders, .backups, or build directories
                    var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (parts.Any(p => p.Equals("QuickNote", StringComparison.OrdinalIgnoreCase) ||
                                      p.Equals(".backups", StringComparison.OrdinalIgnoreCase) ||
                                      p.Equals(".trash", StringComparison.OrdinalIgnoreCase) ||
                                      p.Equals(".recovery", StringComparison.OrdinalIgnoreCase) ||
                                      p.Equals(".quicknote", StringComparison.OrdinalIgnoreCase) ||
                                      p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                      p.Equals("obj", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var normalizedPath = Path.GetFullPath(file);
                    if (!seenPaths.Add(normalizedPath))
                    {
                        continue;
                    }

                    var key = relative.Replace('\\', '/').TrimStart('/');
                    foundKeys.Add(key);

                    var fi = new FileInfo(file);
                    if (catalogMap.TryGetValue(key, out var entry) &&
                        entry.LastFileWriteUtc == fi.LastWriteTimeUtc &&
                        entry.FileSize == fi.Length &&
                        !string.IsNullOrEmpty(entry.Content))
                    {
                        // Cache hit: instant reconstruction from notes_index.json without disk reading/parsing
                        if ((string.IsNullOrWhiteSpace(entry.Snippet) || entry.Snippet == "Empty note" || string.IsNullOrWhiteSpace(entry.RenderText)) && !string.IsNullOrWhiteSpace(entry.Content))
                        {
                            entry.Snippet = MarkdownParser.ComputeRenderText(entry.Content, entry.Title);
                            entry.RenderText = entry.Snippet;
                            isDirty = true;
                        }
                        var doc = entry.ToNoteDocument(_notesRoot);
                        notes.Add(doc);
                    }
                    else
                    {
                        // Cache miss: file is new or modified
                        var note = await LoadNoteFromFileAsync(file).ConfigureAwait(false);
                        if (note != null)
                        {
                            note.Snippet = MarkdownParser.ComputeRenderText(note.Content ?? string.Empty, note.Title);
                            note.RenderText = note.Snippet;
                            notes.Add(note);
                            var newEntry = NoteIndexEntry.FromNoteDocument(note, fi.Length, fi.LastWriteTimeUtc, isDiskSynced: true);
                            catalogMap[key] = newEntry;
                            isDirty = true;
                        }
                    }
                }

                // Remove deleted files from catalog
                var missingKeys = catalogMap.Keys.Where(k => !foundKeys.Contains(k)).ToList();
                if (missingKeys.Count > 0)
                {
                    foreach (var mk in missingKeys)
                    {
                        catalogMap.Remove(mk);
                    }
                    isDirty = true;
                }

                if (isDirty || !File.Exists(_indexPath))
                {
                    catalog.Notes = catalogMap.Values.ToList();
                    await SaveIndexCatalogAsync(catalog).ConfigureAwait(false);
                }

                return notes;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveNoteFastAsync(NoteDocument note, string? oldFullPath = null)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));

            note.Snippet = MarkdownParser.ComputeRenderText(note.Content ?? string.Empty, note.Title);
            note.RenderText = note.Snippet;
            note.RelativePath = Path.GetRelativePath(_notesRoot, note.FullPath);

            if ((note.Tags == null || note.Tags.Length == 0) && !string.IsNullOrWhiteSpace(note.Content))
            {
                var parsed = _markdownParser.Parse(note.FullPath, note.Content);
                note.Tags = parsed.Tags;
                if (note.Headings == null || note.Headings.Length == 0)
                {
                    note.Headings = parsed.Headings;
                }
            }

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                var catalog = await LoadIndexCatalogAsync().ConfigureAwait(false);
                var entry = NoteIndexEntry.FromNoteDocument(note, note.FileSize, DateTime.UtcNow, isDiskSynced: false);
                var key = note.RelativePath.Replace('\\', '/').TrimStart('/');

                var oldRel = !string.IsNullOrWhiteSpace(oldFullPath) ? Path.GetRelativePath(_notesRoot, oldFullPath).Replace('\\', '/').TrimStart('/') : null;
                var idx = catalog.Notes.FindIndex(e =>
                    string.Equals(e.RelativePath.Replace('\\', '/').TrimStart('/'), key, StringComparison.OrdinalIgnoreCase) ||
                    (oldRel != null && string.Equals(e.RelativePath.Replace('\\', '/').TrimStart('/'), oldRel, StringComparison.OrdinalIgnoreCase)) ||
                    e.Id == note.Id);

                if (idx >= 0)
                {
                    catalog.Notes[idx] = entry;
                }
                else
                {
                    catalog.Notes.Add(entry);
                }

                _sqliteIndex.UpsertNote(entry, note.Content);
                await SaveIndexCatalogAsync(catalog).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }

            var writeKey = note.FullPath.ToLowerInvariant();
            var req = new NoteWriteRequest
            {
                Note = note,
                OldFullPath = oldFullPath
            };

            _pendingWrites[writeKey] = req;
            _writeChannel.Writer.TryWrite(writeKey);
        }

        public async Task FlushPendingWritesAsync()
        {
            while (!_pendingWrites.IsEmpty)
            {
                var tasks = _pendingWrites.Values.Select(v => v.Completion.Task).ToArray();
                if (tasks.Length == 0) break;
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch { }
            }
        }

        private async Task ProcessWriteQueueAsync()
        {
            var reader = _writeChannel.Reader;
            try
            {
                while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var key))
                    {
                        if (!_pendingWrites.TryGetValue(key, out var req))
                            continue;

                        try
                        {
                            await WriteNoteToDiskAsync(req).ConfigureAwait(false);
                            req.Completion.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ProcessWriteQueueAsync] Write failed: {ex.Message}");
                            req.Completion.TrySetException(ex);
                        }
                        finally
                        {
                            _pendingWrites.TryRemove(key, out _);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task WriteNoteToDiskAsync(NoteWriteRequest req)
        {
            var note = req.Note;

            // Handle disk rename if old path exists
            if (!string.IsNullOrWhiteSpace(req.OldFullPath) &&
                !string.Equals(req.OldFullPath, note.FullPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(req.OldFullPath))
            {
                try
                {
                    var targetDir = Path.GetDirectoryName(note.FullPath);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    File.Move(req.OldFullPath, note.FullPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WriteNoteToDiskAsync] Move failed: {ex.Message}");
                }
            }

            // Auto-backup before saving
            if (File.Exists(note.FullPath))
            {
                try
                {
                    var backupDir = Path.Combine(_notesRoot, ".backups");
                    if (!Directory.Exists(backupDir))
                    {
                        var d = Directory.CreateDirectory(backupDir);
                        if (OperatingSystem.IsWindows())
                        {
                            try { d.Attributes |= FileAttributes.Hidden; } catch { }
                        }
                    }
                    var backupFile = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(note.FullPath)}_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
                    File.Copy(note.FullPath, backupFile, overwrite: true);
                }
                catch { }
            }

            var dir = Path.GetDirectoryName(note.FullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await _atomicFileWriter.WriteAllTextAsync(note.FullPath, note.Content ?? string.Empty).ConfigureAwait(false);

            // Mark IsDiskSynced = true in catalog
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                var catalog = await LoadIndexCatalogAsync().ConfigureAwait(false);
                var fi = new FileInfo(note.FullPath);
                var normRel = note.RelativePath?.Replace('\\', '/').TrimStart('/') ?? string.Empty;
                var entry = catalog.Notes.FirstOrDefault(e =>
                    string.Equals(e.RelativePath.Replace('\\', '/').TrimStart('/'), normRel, StringComparison.OrdinalIgnoreCase) ||
                    e.Id == note.Id);

                if (entry != null)
                {
                    entry.IsDiskSynced = true;
                    entry.FileSize = fi.Exists ? fi.Length : 0;
                    entry.LastFileWriteUtc = fi.Exists ? fi.LastWriteTimeUtc : DateTime.UtcNow;
                    await SaveIndexCatalogAsync(catalog).ConfigureAwait(false);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task AddNoteAsync(NoteDocument note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            await _atomicFileWriter.WriteAllTextAsync(note.FullPath, note.Content ?? string.Empty);

            await _lock.WaitAsync();
            try
            {
                var catalog = await LoadIndexCatalogAsync();
                var fi = new FileInfo(note.FullPath);
                note.LastModified = fi.LastWriteTime;
                note.FileSize = fi.Length;
                note.RelativePath = Path.GetRelativePath(_notesRoot, note.FullPath);
                note.Snippet = MarkdownParser.ComputeRenderText(note.Content ?? string.Empty, note.Title);
                note.RenderText = note.Snippet;

                var entry = NoteIndexEntry.FromNoteDocument(note, fi.Length, fi.LastWriteTimeUtc);
                var key = note.RelativePath.Replace('\\', '/').TrimStart('/');

                var idx = catalog.Notes.FindIndex(e =>
                    string.Equals(e.RelativePath.Replace('\\', '/').TrimStart('/'), key, StringComparison.OrdinalIgnoreCase) ||
                    e.Id == note.Id);

                if (idx >= 0)
                {
                    catalog.Notes[idx] = entry;
                }
                else
                {
                    catalog.Notes.Add(entry);
                }

                _sqliteIndex.UpsertNote(entry, note.Content);
                await SaveIndexCatalogAsync(catalog);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task UpdateNoteAsync(NoteDocument note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            await _atomicFileWriter.WriteAllTextAsync(note.FullPath, note.Content ?? string.Empty);

            await _lock.WaitAsync();
            try
            {
                var catalog = await LoadIndexCatalogAsync();
                var fi = new FileInfo(note.FullPath);
                note.LastModified = fi.LastWriteTime;
                note.FileSize = fi.Length;
                note.RelativePath = Path.GetRelativePath(_notesRoot, note.FullPath);
                note.Snippet = MarkdownParser.ComputeRenderText(note.Content ?? string.Empty, note.Title);
                note.RenderText = note.Snippet;

                var entry = NoteIndexEntry.FromNoteDocument(note, fi.Length, fi.LastWriteTimeUtc);
                var key = note.RelativePath.Replace('\\', '/').TrimStart('/');

                var idx = catalog.Notes.FindIndex(e =>
                    string.Equals(e.RelativePath.Replace('\\', '/').TrimStart('/'), key, StringComparison.OrdinalIgnoreCase) ||
                    e.Id == note.Id);

                if (idx >= 0)
                {
                    catalog.Notes[idx] = entry;
                }
                else
                {
                    catalog.Notes.Add(entry);
                }

                _sqliteIndex.UpsertNote(entry, note.Content);
                await SaveIndexCatalogAsync(catalog);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeleteNoteAsync(Guid id)
        {
            await _lock.WaitAsync();
            try
            {
                var catalog = await LoadIndexCatalogAsync();
                var entry = catalog.Notes.FirstOrDefault(e => e.Id == id);
                if (entry != null)
                {
                    var fullPath = Path.Combine(_notesRoot, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullPath))
                    {
                        try { File.Delete(fullPath); } catch { }
                    }
                    catalog.Notes.Remove(entry);
                    _sqliteIndex.DeleteNote(id);
                    await SaveIndexCatalogAsync(catalog);
                    return;
                }

                // Fallback if not found in catalog: search disk
                var files = Directory.GetFiles(_notesRoot, "*.md", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var hash = System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(file.ToLowerInvariant()));
                    var fileId = new Guid(hash.Take(16).ToArray());
                    if (fileId == id)
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                        }
                        break;
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> NoteExistsAsync(Guid id)
        {
            var note = await GetNoteByIdAsync(id);
            return note != null;
        }

        private async Task<NotesIndexCatalog> LoadIndexCatalogAsync()
        {
            if (_cachedCatalog != null) return _cachedCatalog;

            if (File.Exists(_indexPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_indexPath);
                    var loaded = JsonSerializer.Deserialize<NotesIndexCatalog>(json);
                    if (loaded != null)
                    {
                        loaded.Notes ??= new List<NoteIndexEntry>();
                        _cachedCatalog = loaded;
                        return _cachedCatalog;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FileNoteRepository] Failed to read {_indexPath}: {ex.Message}");
                }
            }

            _cachedCatalog = new NotesIndexCatalog { Notes = new List<NoteIndexEntry>() };
            return _cachedCatalog;
        }

        private async Task SaveIndexCatalogAsync(NotesIndexCatalog catalog)
        {
            try
            {
                catalog.LastUpdatedUtc = DateTime.UtcNow;
                var dir = Path.GetDirectoryName(_indexPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
                await _atomicFileWriter.WriteAllTextAsync(_indexPath, json);
                _cachedCatalog = catalog;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileNoteRepository] Error saving index catalog: {ex.Message}");
            }
        }

        private async Task<NoteDocument?> LoadNoteFromFileAsync(string filePath)
        {
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                var doc = _markdownParser.Parse(filePath, content);
                doc.RelativePath = Path.GetRelativePath(_notesRoot, filePath);
                doc.Snippet = MarkdownParser.ComputeRenderText(content, doc.Title);
                doc.RenderText = doc.Snippet;
                return doc;
            }
            catch
            {
                return null;
            }
        }
    }

    public class NoteWriteRequest
    {
        public NoteDocument Note { get; set; } = null!;
        public string? OldFullPath { get; set; }
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
