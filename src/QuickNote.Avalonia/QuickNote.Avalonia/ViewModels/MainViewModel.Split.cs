using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.ViewModels
{
    public enum EditorPane
    {
        Left,
        Right
    }

    public partial class MainViewModel
    {
        // ── Split View State ──────────────────────────────────────────
        [ObservableProperty]
        private bool _isSplitViewActive = false;

        [ObservableProperty]
        private EditorPane _activeEditorPane = EditorPane.Left;

        [ObservableProperty]
        private NoteTabViewModel? _rightActiveTab;

        [ObservableProperty]
        private NoteDocument? _rightCurrentNote;

        [ObservableProperty]
        private string _rightNoteTitle = string.Empty;

        [ObservableProperty]
        private string _rightEditorContent = string.Empty;

        private bool _isRightModified = false;
        private bool _isRightInternalChange = false;

        public ObservableCollection<NoteTabViewModel> RightOpenTabs { get; } = new();
        public ObservableCollection<TagItemViewModel> RightCurrentNoteTags { get; } = new();

        public bool HasRightCurrentNote => RightCurrentNote != null;
        public bool IsRightModified => _isRightModified;
        public bool IsLeftPaneActive => ActiveEditorPane == EditorPane.Left;
        public bool IsRightPaneActive => ActiveEditorPane == EditorPane.Right;

        public event Action<string>? RightMarkdownRenderRequested;

        // ── Property Changed Reactions ────────────────────────────────
        partial void OnIsSplitViewActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(CurrentModeDisplayName));
            OnPropertyChanged(nameof(IsNotionEditorActive));

            if (value)
            {
                // When activating split, if right note not set, clone current note
                if (RightCurrentNote == null && CurrentNote != null)
                {
                    SelectRightNote(CurrentNote);
                }
            }
        }

        partial void OnActiveEditorPaneChanged(EditorPane value)
        {
            OnPropertyChanged(nameof(IsLeftPaneActive));
            OnPropertyChanged(nameof(IsRightPaneActive));
        }

        partial void OnRightCurrentNoteChanged(NoteDocument? oldValue, NoteDocument? newValue)
        {
            OnPropertyChanged(nameof(HasRightCurrentNote));

            if (newValue == null)
            {
                _isRightInternalChange = true;
                RightNoteTitle = string.Empty;
                RightEditorContent = string.Empty;
                _isRightInternalChange = false;
                RightCurrentNoteTags.Clear();
                RightMarkdownRenderRequested?.Invoke(string.Empty);
                return;
            }

            _isRightInternalChange = true;
            var (extractedTitle, bodyContent) = SeparateTitleAndBody(
                newValue.Content ?? string.Empty, newValue.Title);

            if ((string.Equals(extractedTitle, "Untitled", StringComparison.OrdinalIgnoreCase) &&
                 string.IsNullOrWhiteSpace(bodyContent)) ||
                (string.IsNullOrWhiteSpace(extractedTitle) &&
                 (string.IsNullOrWhiteSpace(newValue.Title) ||
                  string.Equals(newValue.Title, "Untitled", StringComparison.OrdinalIgnoreCase)) &&
                 string.IsNullOrWhiteSpace(bodyContent)))
            {
                RightNoteTitle = string.Empty;
            }
            else
            {
                RightNoteTitle = !string.IsNullOrWhiteSpace(extractedTitle)
                    ? extractedTitle
                    : (!string.IsNullOrWhiteSpace(newValue.Title) ? newValue.Title : newValue.FileName);
            }

            RightEditorContent = bodyContent;
            _isRightInternalChange = false;

            RightCurrentNoteTags.Clear();
            if (newValue.Tags != null)
            {
                foreach (var tag in newValue.Tags)
                {
                    if (!string.IsNullOrWhiteSpace(tag))
                        RightCurrentNoteTags.Add(new TagItemViewModel(tag));
                }
            }

            RightMarkdownRenderRequested?.Invoke(RightEditorContent);
            SyncRightActiveTab(newValue);
        }

        partial void OnRightNoteTitleChanged(string value)
        {
            if (_isRightInternalChange || RightCurrentNote == null) return;

            RightCurrentNote.Title = value;
            if (RightActiveTab != null)
            {
                RightActiveTab.Title = value;
                RightActiveTab.IsModified = true;
            }

            // If same file is open in Left pane, synchronize title
            if (CurrentNote != null && string.Equals(CurrentNote.FullPath, RightCurrentNote.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(NoteTitle, value, StringComparison.Ordinal))
                {
                    _isInternalContentChange = true;
                    NoteTitle = value;
                    _isInternalContentChange = false;
                    _isModified = true;
                    if (ActiveTab != null)
                    {
                        ActiveTab.Title = value;
                        ActiveTab.IsModified = true;
                    }
                }
            }

            UpdateTreeNodeTitle(FolderNodes, RightCurrentNote.FullPath, value);
            _isRightModified = true;
            StatusText = "Editing...";
            TriggerDebouncedSave();
        }

        partial void OnRightEditorContentChanged(string value)
        {
            if (_isRightInternalChange || RightCurrentNote == null) return;

            RightMarkdownRenderRequested?.Invoke(value);
            _isRightModified = true;
            if (RightActiveTab != null) RightActiveTab.IsModified = true;
            StatusText = "Editing...";

            // If same file is open in Left pane, synchronize content ONLY if Right pane is active editor
            if (ActiveEditorPane == EditorPane.Right && CurrentNote != null && string.Equals(CurrentNote.FullPath, RightCurrentNote.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(EditorContent, value, StringComparison.Ordinal))
                {
                    _isInternalContentChange = true;
                    EditorContent = value;
                    _isInternalContentChange = false;
                    _isModified = true;
                    if (ActiveTab != null) ActiveTab.IsModified = true;
                    MarkdownRenderRequested?.Invoke(value);
                }
            }

            TriggerDebouncedSave();
        }

        // ── Tab & Note Selection for Right Pane ─────────────────────────
        public void SelectRightNote(NoteDocument note)
        {
            if (note == null) return;
            if (note == RightCurrentNote) return;

            if (_isRightModified && RightCurrentNote != null)
            {
                var oldNote = RightCurrentNote;
                var oldTitle = RightNoteTitle;
                var oldContent = RightEditorContent;
                _isRightModified = false;
                _ = SaveNoteAsync(oldNote, oldTitle, oldContent);
            }

            RightCurrentNote = note;
            ActiveEditorPane = EditorPane.Right;
        }

        private void SyncRightActiveTab(NoteDocument note)
        {
            var existingTab = RightOpenTabs.FirstOrDefault(t =>
                string.Equals(t.FullPath, note.FullPath, StringComparison.OrdinalIgnoreCase));

            if (existingTab == null)
            {
                existingTab = new NoteTabViewModel(note);
                var capture = existingTab;
                existingTab.CloseCommand = new RelayCommand(() => CloseRightTab(capture));
                RightOpenTabs.Add(existingTab);
            }

            foreach (var t in RightOpenTabs)
                t.IsActive = (t == existingTab);

            RightActiveTab = existingTab;
        }

        [RelayCommand]
        public void SelectRightTab(NoteTabViewModel? tab)
        {
            if (tab?.Note == null) return;
            SelectRightNote(tab.Note);
            ActiveEditorPane = EditorPane.Right;
        }

        [RelayCommand]
        public void CloseRightTab(NoteTabViewModel? tab)
        {
            if (tab == null) return;

            if (RightActiveTab == tab && _isRightModified && RightCurrentNote != null)
            {
                var noteToSave = RightCurrentNote;
                var titleToSave = RightNoteTitle;
                var contentToSave = RightEditorContent;
                _isRightModified = false;
                _ = SaveNoteAsync(noteToSave, titleToSave, contentToSave);
            }

            int index = RightOpenTabs.IndexOf(tab);
            RightOpenTabs.Remove(tab);

            if (RightActiveTab == tab)
            {
                if (RightOpenTabs.Count > 0)
                {
                    int nextIndex = Math.Clamp(index, 0, RightOpenTabs.Count - 1);
                    RightActiveTab = RightOpenTabs[nextIndex];
                    RightCurrentNote = RightActiveTab.Note;
                }
                else
                {
                    RightActiveTab = null;
                    RightCurrentNote = null;
                    // Closing last tab in right pane closes the split view (VS Code behavior)
                    CloseSplitEditor();
                }
            }
        }

        [RelayCommand]
        public void CloseActiveRightTab()
        {
            if (RightActiveTab != null)
                CloseRightTab(RightActiveTab);
        }

        // ── Split Commands ─────────────────────────────────────────────
        [RelayCommand]
        public void SplitEditorRight()
        {
            IsNotionMode = true;
            IsReadingMode = false;
            IsSplitViewActive = true;
            ActiveEditorPane = EditorPane.Right;

            if (RightCurrentNote == null && CurrentNote != null)
            {
                SelectRightNote(CurrentNote);
            }
        }

        [RelayCommand]
        public void CloseSplitEditor()
        {
            if (_isRightModified && RightCurrentNote != null)
            {
                _ = SaveNoteAsync(RightCurrentNote, RightNoteTitle, RightEditorContent);
                _isRightModified = false;
            }

            IsSplitViewActive = false;
            ActiveEditorPane = EditorPane.Left;
        }

        public async Task SaveRightNoteAsync()
        {
            if (RightCurrentNote == null) return;
            await SaveNoteAsync(RightCurrentNote, RightNoteTitle, RightEditorContent);
        }
    }
}
