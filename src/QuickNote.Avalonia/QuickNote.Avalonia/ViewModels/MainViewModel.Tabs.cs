using System;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.ViewModels
{
    // Tab management, note selection, search, search-result navigation.
    public partial class MainViewModel
    {
        // ── Note / tab selection ───────────────────────────────────────
        public void SelectNote(NoteDocument note)
        {
            if (note == null) return;
            if (note == CurrentNote) return;

            if (_isModified && CurrentNote != null)
            {
                _autoSaveTimer.Stop();
                var oldNote = CurrentNote;
                var oldTitle = NoteTitle;
                var oldContent = EditorContent;
                _isModified = false;
                _ = SaveNoteAsync(oldNote, oldTitle, oldContent);
            }
            CurrentNote = note;
        }

        private void SyncActiveTab(NoteDocument note)
        {
            var existingTab = OpenTabs.FirstOrDefault(t =>
                string.Equals(t.FullPath, note.FullPath, StringComparison.OrdinalIgnoreCase));

            if (existingTab == null)
            {
                existingTab = new NoteTabViewModel(note);
                var capture = existingTab;
                existingTab.CloseCommand = new RelayCommand(() => CloseTab(capture));
                OpenTabs.Add(existingTab);
            }

            foreach (var t in OpenTabs)
                t.IsActive = (t == existingTab);

            ActiveTab = existingTab;
        }

        public void SelectTab(NoteTabViewModel tab)
        {
            if (tab?.Note == null) return;
            SelectNote(tab.Note);
            ActiveEditorPane = EditorPane.Left;
        }

        public void CloseTab(NoteTabViewModel tab)
        {
            if (tab == null) return;

            if (ActiveTab == tab && _isModified && CurrentNote != null)
            {
                _autoSaveTimer.Stop();
                var noteToSave = CurrentNote;
                var titleToSave = NoteTitle;
                var contentToSave = EditorContent;
                _isModified = false;
                _ = SaveNoteAsync(noteToSave, titleToSave, contentToSave);
            }

            int index = OpenTabs.IndexOf(tab);
            OpenTabs.Remove(tab);

            // Clean up orphan file if closing an untouched, empty Untitled note
            if (tab.Note != null &&
                Path.GetFileNameWithoutExtension(tab.Note.FileName).StartsWith("Untitled", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(tab.Note.Title) &&
                (tab != ActiveTab || !_isModified) &&
                !tab.IsModified)
            {
                try
                {
                    if (File.Exists(tab.Note.FullPath))
                    {
                        var fi = new FileInfo(tab.Note.FullPath);
                        if (fi.Length == 0)
                        {
                            File.Delete(tab.Note.FullPath);
                            Notes.Remove(tab.Note);
                            _ = _noteRepository.DeleteNoteAsync(tab.Note.Id);
                            NotesCountText = $"{Notes.Count} notes";
                            _ = RefreshFolderTreeAsync();
                        }
                    }
                }
                catch { }
            }

            if (ActiveTab == tab)
            {
                if (OpenTabs.Count > 0)
                {
                    int nextIndex = System.Math.Clamp(index, 0, OpenTabs.Count - 1);
                    ActiveTab   = OpenTabs[nextIndex];
                    CurrentNote = ActiveTab.Note;
                }
                else
                {
                    ActiveTab   = null;
                    CurrentNote = null;
                }
            }
        }

        [RelayCommand]
        public void CloseActiveTab()
        {
            if (ActiveTab != null)
                CloseTab(ActiveTab);
        }

        // ── Property-change reactions ────────────────────────────────
        partial void OnCurrentNoteChanged(NoteDocument? oldValue, NoteDocument? newValue)
        {
            OnPropertyChanged(nameof(HasCurrentNote));
            OnPropertyChanged(nameof(HasNoCurrentNote));

            if (newValue == null)
            {
                _isInternalContentChange = true;
                NoteTitle    = string.Empty;
                EditorContent = string.Empty;
                _isInternalContentChange = false;
                CurrentNoteTags.Clear();
                OutlineHeadings.Clear();
                UpdateWordAndLineCounts(string.Empty);
                MarkdownRenderRequested?.Invoke(string.Empty);
                return;
            }

            _isInternalContentChange = true;
            var (extractedTitle, bodyContent) = SeparateTitleAndBody(
                newValue.Content ?? string.Empty, newValue.Title);

            if ((string.Equals(extractedTitle, "Untitled", StringComparison.OrdinalIgnoreCase) &&
                 string.IsNullOrWhiteSpace(bodyContent)) ||
                (string.IsNullOrWhiteSpace(extractedTitle) &&
                 (string.IsNullOrWhiteSpace(newValue.Title) ||
                  string.Equals(newValue.Title, "Untitled", StringComparison.OrdinalIgnoreCase)) &&
                 string.IsNullOrWhiteSpace(bodyContent)))
            {
                NoteTitle = string.Empty;
            }
            else
            {
                NoteTitle = !string.IsNullOrWhiteSpace(extractedTitle)
                    ? extractedTitle
                    : (!string.IsNullOrWhiteSpace(newValue.Title) ? newValue.Title : newValue.FileName);
            }

            newValue.Title = NoteTitle;
            EditorContent  = bodyContent;
            _isInternalContentChange = false;

            CurrentNoteTags.Clear();
            if (newValue.Tags != null)
            {
                foreach (var tag in newValue.Tags)
                    if (!string.IsNullOrWhiteSpace(tag))
                        CurrentNoteTags.Add(new TagItemViewModel(tag));
            }

            UpdateWordAndLineCounts(EditorContent);
            UpdateOutline(EditorContent);
            MarkdownRenderRequested?.Invoke(EditorContent);
            SyncActiveTab(newValue);
        }

        partial void OnNoteTitleChanged(string value)
        {
            if (_isInternalContentChange || CurrentNote == null) return;

            CurrentNote.Title = value;
            if (ActiveTab != null)
            {
                ActiveTab.Title      = value;
                ActiveTab.IsModified = true;
            }

            // If same file is open in Right pane, sync title
            if (IsSplitViewActive && RightCurrentNote != null && string.Equals(RightCurrentNote.FullPath, CurrentNote.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(RightNoteTitle, value, StringComparison.Ordinal))
                {
                    _isRightInternalChange = true;
                    RightNoteTitle = value;
                    _isRightInternalChange = false;
                    _isRightModified = true;
                    if (RightActiveTab != null)
                    {
                        RightActiveTab.Title = value;
                        RightActiveTab.IsModified = true;
                    }
                }
            }

            UpdateTreeNodeTitle(FolderNodes, CurrentNote.FullPath, value);
            _isModified = true;
            StatusText  = "Editing...";
            TriggerDebouncedSave();
        }

        partial void OnEditorContentChanged(string value)
        {
            UpdateWordAndLineCounts(value);

            // Only fire render for real user edits — NOT during internal note-switch.
            // OnCurrentNoteChanged explicitly fires MarkdownRenderRequested after all state is settled.
            if (_isInternalContentChange || CurrentNote == null) return;

            MarkdownRenderRequested?.Invoke(value);
            _isModified = true;
            if (ActiveTab != null) ActiveTab.IsModified = true;
            StatusText = "Editing...";
            UpdateOutline(value);

            // If same file is open in Right pane, sync content ONLY if Left pane is active editor
            if (IsSplitViewActive && ActiveEditorPane == EditorPane.Left && RightCurrentNote != null && string.Equals(RightCurrentNote.FullPath, CurrentNote.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(RightEditorContent, value, StringComparison.Ordinal))
                {
                    _isRightInternalChange = true;
                    RightEditorContent = value;
                    _isRightInternalChange = false;
                    _isRightModified = true;
                    if (RightActiveTab != null) RightActiveTab.IsModified = true;
                    RightMarkdownRenderRequested?.Invoke(value);
                }
            }

            TriggerDebouncedSave();
        }

        partial void OnSearchQueryChanged(string value) => ExecuteSearch(value);

        partial void OnSelectedFolderNodeChanged(FolderNode? value)
        {
            if (value == null || value.IsDirectory) return;
            var matchingNote = Notes.FirstOrDefault(n =>
                string.Equals(n.FullPath, value.FullPath, StringComparison.OrdinalIgnoreCase));
            if (matchingNote != null)
            {
                if (IsSplitViewActive && ActiveEditorPane == EditorPane.Right)
                {
                    SelectRightNote(matchingNote);
                }
                else
                {
                    SelectNote(matchingNote);
                }
            }
        }

        // ── Search ───────────────────────────────────────────────────
        public void ExecuteSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                IsSearching = false;
                SearchResults.Clear();
                return;
            }

            IsSearching = true;
            SearchResults.Clear();
            try
            {
                foreach (var res in _searchEngine.Search(query))
                    SearchResults.Add(res);
            }
            catch { }
        }

        [RelayCommand]
        public void ClearSearch()
        {
            SearchQuery = string.Empty;
            IsSearching = false;
            SearchResults.Clear();
        }

        public void SelectSearchResult(SearchResult result)
        {
            if (result == null) return;
            var matchingNote = Notes.FirstOrDefault(n =>
                n.Id == result.DocumentId ||
                string.Equals(n.FullPath, result.FilePath, StringComparison.OrdinalIgnoreCase));

            if (matchingNote != null)
            {
                SelectNote(matchingNote);
                if (result.LineNumber > 0)
                    ScrollToLineRequested?.Invoke(result.LineNumber);
            }
        }
    }
}
