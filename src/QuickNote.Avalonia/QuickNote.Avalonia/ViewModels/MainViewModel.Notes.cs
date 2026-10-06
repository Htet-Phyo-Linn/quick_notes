using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using QuickNote.Core.Models;
using QuickNote.Core.Utilities;

namespace QuickNote.Avalonia.ViewModels
{
    // Note CRUD: load all, create, save, delete, rename file to match title.
    public partial class MainViewModel
    {
        // ── Load ─────────────────────────────────────────────────────
        public async Task LoadAllAsync()
        {
            try
            {
                StatusText = "Loading notes...";
                await SyncAllFileNamesWithTitlesAsync();

                var loadedNotes = await _noteRepository.GetAllNotesAsync();
                Notes.Clear();
                foreach (var note in loadedNotes.OrderByDescending(n => n.LastModified))
                {
                    Notes.Add(note);
                    _searchEngine.IndexDocument(note);
                }

                NotesCountText = $"{Notes.Count} notes";
                await RefreshFolderTreeAsync();

                if (CurrentNote == null && Notes.Count > 0)
                    SelectNote(Notes[0]);

                StatusText = "Ready";
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
            }
        }

        // ── Create ───────────────────────────────────────────────────
        public async Task NewNoteAsync(string? targetDirectory = null)
        {
            if (_isCreatingNote) return;
            _isCreatingNote = true;
            try
            {
                string dir = !string.IsNullOrWhiteSpace(targetDirectory) && Directory.Exists(targetDirectory)
                    ? targetDirectory
                    : _notesRoot;

                // 1. If current note is already an untitled empty note, don't spawn another one!
                if (CurrentNote != null &&
                    string.Equals(CurrentNote.FolderPath, dir, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileNameWithoutExtension(CurrentNote.FileName).StartsWith("Untitled", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(CurrentNote.Title) &&
                    string.IsNullOrWhiteSpace(EditorContent) &&
                    !_isModified)
                {
                    FocusEditorRequested?.Invoke();
                    return;
                }

                // 2. If an open tab in the same folder is already an empty untitled note, switch to it
                var existingBlankTab = OpenTabs.FirstOrDefault(t =>
                    t.Note != null &&
                    string.Equals(t.Note.FolderPath, dir, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileNameWithoutExtension(t.Note.FileName).StartsWith("Untitled", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(t.Note.Title) &&
                    string.IsNullOrWhiteSpace(t.Note.Content) &&
                    !t.IsModified);

                if (existingBlankTab != null && existingBlankTab.Note != null)
                {
                    SelectNote(existingBlankTab.Note);
                    FocusEditorRequested?.Invoke();
                    return;
                }

                // 3. Find file name: Check if there is an existing 0-byte orphan Untitled file on disk
                string baseName = "Untitled";
                string fullPath = Path.Combine(dir, $"{baseName}.md");

                // Check if Untitled.md or Untitled (N).md exists on disk with 0 bytes and not currently open/used
                NoteDocument? noteToUse = null;
                if (File.Exists(fullPath))
                {
                    try
                    {
                        var fi = new FileInfo(fullPath);
                        if (fi.Length == 0 && !OpenTabs.Any(t => string.Equals(t.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            noteToUse = Notes.FirstOrDefault(n => string.Equals(n.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    catch { }
                }

                if (noteToUse == null)
                {
                    int counter = 1;
                    while (File.Exists(fullPath))
                    {
                        try
                        {
                            var fi = new FileInfo(fullPath);
                            if (fi.Length == 0 && !OpenTabs.Any(t => string.Equals(t.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)))
                            {
                                noteToUse = Notes.FirstOrDefault(n => string.Equals(n.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
                                if (noteToUse != null) break;
                            }
                        }
                        catch { }

                        fullPath = Path.Combine(dir, $"{baseName} ({counter}).md");
                        counter++;
                    }
                }

                if (noteToUse != null)
                {
                    SelectNote(noteToUse);
                    FocusEditorRequested?.Invoke();
                    return;
                }

                var note = new NoteDocument
                {
                    Id           = Guid.NewGuid(),
                    FullPath     = fullPath,
                    FileName     = Path.GetFileName(fullPath),
                    FolderPath   = dir,
                    RelativePath = Path.GetRelativePath(_notesRoot, fullPath),
                    Title        = string.Empty,
                    Content      = string.Empty,
                    LastModified = DateTime.Now
                };

                await _noteRepository.AddNoteAsync(note);
                Notes.Insert(0, note);
                NotesCountText = $"{Notes.Count} notes";
                _searchEngine.IndexDocument(note);
                await RefreshFolderTreeAsync();
                SelectNote(note);
                FocusEditorRequested?.Invoke();
            }
            catch (Exception ex)
            {
                StatusText = $"New note failed: {ex.Message}";
            }
            finally
            {
                _isCreatingNote = false;
            }
        }

        // ── Save ─────────────────────────────────────────────────────
        private void TriggerDebouncedSave()
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        public async Task SaveNoteAsync(NoteDocument? targetNote = null, string? targetTitle = null, string? targetContent = null)
        {
            var noteToSave = targetNote ?? CurrentNote;
            if (noteToSave == null) return;

            var titleToSave = targetTitle ?? NoteTitle;
            var bodyToSave  = targetContent ?? EditorContent;

            var cleanTitle   = SanitizeFileName(titleToSave);
            var currentBase  = Path.GetFileNameWithoutExtension(noteToSave.FullPath);
            bool needsRename = !string.Equals(cleanTitle, currentBase, StringComparison.OrdinalIgnoreCase);

            if (targetNote == null && !_isModified && !needsRename) return;

            // Safety guard: if existing note on disk has real content, do NOT overwrite with empty body
            if (File.Exists(noteToSave.FullPath))
            {
                try
                {
                    var diskText = File.ReadAllText(noteToSave.FullPath);
                    var (_, diskBody) = SeparateTitleAndBody(diskText, noteToSave.Title);
                    if (!string.IsNullOrWhiteSpace(diskBody) && diskBody.Length > 15 && string.IsNullOrWhiteSpace(bodyToSave))
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveNoteAsync] SAFETY GUARD: Blocked empty overwrite of '{noteToSave.FullPath}'");
                        return;
                    }

                    // Auto-backup before saving
                    var notesRoot = NotesRoot ?? Path.GetDirectoryName(noteToSave.FullPath);
                    if (!string.IsNullOrEmpty(notesRoot))
                    {
                        var backupDir = Path.Combine(notesRoot, ".backups");
                        if (!Directory.Exists(backupDir))
                        {
                            var d = Directory.CreateDirectory(backupDir);
                            d.Attributes |= FileAttributes.Hidden;
                        }
                        var backupFile = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(noteToSave.FullPath)}_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
                        File.Copy(noteToSave.FullPath, backupFile, overwrite: true);
                    }
                }
                catch { }
            }

            try
            {
                StatusText            = "Saving...";
                noteToSave.Title      = titleToSave;
                noteToSave.Content    = CombineTitleAndBody(titleToSave, bodyToSave, noteToSave);
                noteToSave.LastModified = DateTime.Now;

                bool renamed = RenameNoteFileToMatchTitle(noteToSave, titleToSave);
                await _noteRepository.UpdateNoteAsync(noteToSave);
                await _recoveryService.ClearDraftAsync(noteToSave.FullPath);
                _ = Task.Run(() =>
                {
                    try { _searchEngine.IndexDocument(noteToSave); } catch { }
                });

                if (renamed)
                    await RefreshFolderTreeAsync();

                if (targetNote == null || targetNote == CurrentNote || (CurrentNote != null && string.Equals(CurrentNote.FullPath, noteToSave.FullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _isModified = false;
                    if (ActiveTab != null)
                    {
                        ActiveTab.IsModified = false;
                        ActiveTab.Title = !string.IsNullOrWhiteSpace(noteToSave.Title)
                            ? noteToSave.Title
                            : noteToSave.FileName;
                    }
                }

                if (targetNote == RightCurrentNote || (RightCurrentNote != null && string.Equals(RightCurrentNote.FullPath, noteToSave.FullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _isRightModified = false;
                    if (RightActiveTab != null)
                    {
                        RightActiveTab.IsModified = false;
                        RightActiveTab.Title = !string.IsNullOrWhiteSpace(noteToSave.Title)
                            ? noteToSave.Title
                            : noteToSave.FileName;
                    }
                }
                StatusText = "Saved";
                _ = Task.Delay(2500).ContinueWith(_ =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (StatusText == "Saved") StatusText = "Ready";
                    });
                });
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
            }
        }

        // ── Delete ───────────────────────────────────────────────────
        public async Task DeleteNoteAsync(NoteDocument? targetNote = null)
        {
            var noteToDelete = targetNote ?? CurrentNote;
            if (noteToDelete == null) return;

            try
            {
                StatusText = "Deleting...";
                await _trashBinService.MoveToTrashAsync(noteToDelete.FullPath);

                var tab = OpenTabs.FirstOrDefault(t =>
                    string.Equals(t.FullPath, noteToDelete.FullPath, StringComparison.OrdinalIgnoreCase));
                if (tab != null) OpenTabs.Remove(tab);

                Notes.Remove(noteToDelete);
                NotesCountText = $"{Notes.Count} notes";

                if (CurrentNote == noteToDelete)
                    CurrentNote = OpenTabs.FirstOrDefault()?.Note ?? Notes.FirstOrDefault();

                await RefreshFolderTreeAsync();
                StatusText = "Note moved to trash";
            }
            catch (Exception ex)
            {
                StatusText = $"Delete failed: {ex.Message}";
            }
        }

        // ── File name ↔ title sync ────────────────────────────────────
        public static string SanitizeFileName(string name) => FileNameSanitizer.Sanitize(name);

        public bool RenameNoteFileToMatchTitle(NoteDocument note, string newTitle)
        {
            if (note == null || string.IsNullOrWhiteSpace(note.FullPath) || !File.Exists(note.FullPath))
                return false;

            var cleanTitle    = SanitizeFileName(newTitle);
            var dir           = Path.GetDirectoryName(note.FullPath) ?? _notesRoot;
            var currentBase   = Path.GetFileNameWithoutExtension(note.FullPath);

            if (string.Equals(cleanTitle, currentBase, StringComparison.OrdinalIgnoreCase))
                return false;

            var targetPath = Path.Combine(dir, $"{cleanTitle}.md");
            int counter = 1;
            while (File.Exists(targetPath) &&
                   !string.Equals(targetPath, note.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Path.Combine(dir, $"{cleanTitle} ({counter}).md");
                counter++;
            }

            if (string.Equals(targetPath, note.FullPath, StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var oldPath = note.FullPath;
                File.Move(oldPath, targetPath);

                note.FullPath     = targetPath;
                note.FileName     = Path.GetFileName(targetPath);
                note.RelativePath = Path.GetRelativePath(_notesRoot, targetPath);

                UpdateTreeNodePathAndTitle(FolderNodes, oldPath, targetPath, note.Title);

                var openTab = OpenTabs.FirstOrDefault(t =>
                    string.Equals(t.FullPath, oldPath, StringComparison.OrdinalIgnoreCase) || t.Note == note);
                openTab?.NotifyPathChanged();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MainViewModel] Rename failed {note.FullPath} -> {targetPath}: {ex.Message}");
                return false;
            }
        }

        public async Task SyncAllFileNamesWithTitlesAsync()
        {
            if (!Directory.Exists(_notesRoot)) return;
            try
            {
                var files = Directory.GetFiles(_notesRoot, "*.md", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var rel = Path.GetRelativePath(_notesRoot, file);
                    if (rel.StartsWith(".") || rel.Contains(Path.DirectorySeparatorChar + "."))
                        continue;

                    var currentBase = Path.GetFileNameWithoutExtension(file);
                    string content;
                    try   { content = await File.ReadAllTextAsync(file); }
                    catch { continue; }

                    var parsedDoc  = _markdownParser.Parse(file, content);
                    var cleanTitle = SanitizeFileName(parsedDoc.Title);

                    if (!string.IsNullOrWhiteSpace(cleanTitle) &&
                        !cleanTitle.Equals("Untitled", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(cleanTitle, currentBase, StringComparison.OrdinalIgnoreCase))
                    {
                        var dir        = Path.GetDirectoryName(file) ?? _notesRoot;
                        var targetPath = Path.Combine(dir, $"{cleanTitle}.md");
                        int counter    = 1;
                        while (File.Exists(targetPath) &&
                               !string.Equals(targetPath, file, StringComparison.OrdinalIgnoreCase))
                        {
                            targetPath = Path.Combine(dir, $"{cleanTitle} ({counter}).md");
                            counter++;
                        }

                        if (!string.Equals(targetPath, file, StringComparison.OrdinalIgnoreCase))
                        {
                            try   { File.Move(file, targetPath); }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[MainViewModel] Sync rename failed {file}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MainViewModel] SyncAllFileNamesWithTitlesAsync error: {ex.Message}");
            }
        }

        // ── Crash recovery ────────────────────────────────────────────
        private async Task CheckCrashRecoveryAsync()
        {
            try
            {
                var drafts = await _recoveryService.CheckForRecoverableItemsAsync();
                foreach (var draft in drafts)
                {
                    var existingNote = Notes.FirstOrDefault(n =>
                        string.Equals(n.FullPath, draft.OriginalFilePath, StringComparison.OrdinalIgnoreCase));

                    if (existingNote != null && !string.IsNullOrWhiteSpace(draft.Content))
                    {
                        existingNote.Content      = draft.Content;
                        existingNote.LastModified = draft.SavedAt;
                        await _noteRepository.UpdateNoteAsync(existingNote);
                    }
                    await _recoveryService.ClearDraftAsync(draft.OriginalFilePath);
                }
            }
            catch { }
        }

        // ── Content helpers ───────────────────────────────────────────
        private (string title, string body) SeparateTitleAndBody(string content, string fallbackTitle)
        {
            if (string.IsNullOrEmpty(content))
                return (fallbackTitle, string.Empty);

            var normalized   = content.Replace("\r\n", "\n");
            string mainContent = normalized;

            if (normalized.StartsWith("---\n"))
            {
                var endIndex = normalized.IndexOf("\n---\n", 4);
                if (endIndex > 0)
                    mainContent = normalized.Substring(endIndex + 5);
            }

            var trimmedMain = mainContent.TrimStart('\n');
            if (trimmedMain.StartsWith("# ") && !trimmedMain.StartsWith("## "))
            {
                var newlineIndex = trimmedMain.IndexOf('\n');
                if (newlineIndex >= 0)
                {
                    var title = trimmedMain.Substring(2, newlineIndex - 2).Trim();
                    var body  = trimmedMain.Substring(newlineIndex + 1).TrimStart('\n');
                    return (title, body);
                }
                else
                {
                    return (trimmedMain.Substring(2).Trim(), string.Empty);
                }
            }

            return (fallbackTitle, mainContent);
        }

        private string CombineTitleAndBody(string title, string body, NoteDocument? noteContext = null)
        {
            body ??= string.Empty;
            var normalized   = body.Replace("\r\n", "\n");
            string mainContent = normalized;

            if (normalized.StartsWith("---\n"))
            {
                var endIndex = normalized.IndexOf("\n---\n", 4);
                if (endIndex > 0)
                    mainContent = normalized.Substring(endIndex + 5);
            }

            var trimmedMain  = mainContent.TrimStart('\n');
            string frontMatter = string.Empty;

            var activeNote = noteContext ?? CurrentNote;
            if (activeNote?.Tags != null && activeNote.Tags.Length > 0)
            {
                var validTags = activeNote.Tags
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim().TrimStart('#'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (validTags.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("---");
                    sb.AppendLine("tags:");
                    foreach (var tag in validTags)
                        sb.AppendLine($"  - {tag}");
                    sb.AppendLine("---");
                    frontMatter = sb.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(title))
                return string.IsNullOrEmpty(frontMatter) ? mainContent : frontMatter + "\n" + mainContent;

            if (trimmedMain.StartsWith("# ") && !trimmedMain.StartsWith("## "))
            {
                var newlineIndex = trimmedMain.IndexOf('\n');
                var rest         = newlineIndex >= 0 ? trimmedMain.Substring(newlineIndex + 1).TrimStart('\n') : string.Empty;
                var result       = $"# {title}\n\n{rest}";
                return string.IsNullOrEmpty(frontMatter) ? result : frontMatter + "\n" + result;
            }

            var combined = string.IsNullOrWhiteSpace(trimmedMain)
                ? $"# {title}\n"
                : $"# {title}\n\n{trimmedMain}";
            return string.IsNullOrEmpty(frontMatter) ? combined : frontMatter + "\n" + combined;
        }
    }
}
