using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using QuickNote.App.Dialogs;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Core.Services;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.App.ViewModels
{
    public class NavigationEventArgs : EventArgs
    {
        public int LineNumber { get; set; }
        public int StartOffset { get; set; }
        public int Length { get; set; }
        public bool TopAlign { get; set; }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly INoteRepository _noteRepository;
        private readonly ISearchEngine _searchEngine;
        private readonly IOutlineService _outlineService;
        private readonly ITagService _tagService;
        private readonly IFolderService _folderService;
        private readonly IRecoveryService _recoveryService;
        private readonly ITrashBinService _trashBinService;
        private readonly IFileWatcher _fileWatcher;
        private readonly ICloudSyncService _cloudSyncService;
        private readonly ISettingsService _settingsService;
        private string _notesRoot;

        private readonly DispatcherTimer _autoSaveTimer;
        private readonly DispatcherTimer _settingsSaveTimer;
        private bool _isRebuildingFolderTree;
        private bool _isRestoringDraft;

        // UI State
        private NoteDocument? _currentNote;
        private string _editorContent = string.Empty;
        private bool _isModified;
        private string _statusText = "● Saved";
        private string _searchTerm = string.Empty;
        private bool _hasSearchResults;
        private SearchResult? _selectedSearchResult;
        private FolderNode? _folderRoot;
        private Tag? _selectedTag;
        private string _wordCountText = "0 words";
        private string _lineCountText = "1 line";

        public event EventHandler<NavigationEventArgs>? NavigationRequested;

        public ObservableCollection<NoteDocument> Notes { get; } = new ObservableCollection<NoteDocument>();
        public ObservableCollection<FolderNode> FolderNodes { get; } = new ObservableCollection<FolderNode>();
        public ObservableCollection<Heading> OutlineHeadings { get; } = new ObservableCollection<Heading>();
        public ObservableCollection<Tag> Tags { get; } = new ObservableCollection<Tag>();
        public ObservableCollection<string> CurrentNoteTags { get; } = new ObservableCollection<string>();
        public ObservableCollection<SearchResult> SearchResults { get; } = new ObservableCollection<SearchResult>();
        public ObservableCollection<NoteTabViewModel> OpenTabs { get; } = new ObservableCollection<NoteTabViewModel>();

        public MainViewModel(
            INoteRepository noteRepository,
            ISearchEngine searchEngine,
            IOutlineService outlineService,
            ITagService tagService,
            IFolderService folderService,
            IRecoveryService recoveryService,
            ITrashBinService trashBinService,
            IFileWatcher fileWatcher,
            ICloudSyncService cloudSyncService,
            ISettingsService settingsService,
            string notesRoot)
        {
            _noteRepository = noteRepository ?? throw new ArgumentNullException(nameof(noteRepository));
            _searchEngine = searchEngine ?? throw new ArgumentNullException(nameof(searchEngine));
            _outlineService = outlineService ?? throw new ArgumentNullException(nameof(outlineService));
            _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
            _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
            _recoveryService = recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));
            _trashBinService = trashBinService ?? throw new ArgumentNullException(nameof(trashBinService));
            _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));
            _cloudSyncService = cloudSyncService ?? throw new ArgumentNullException(nameof(cloudSyncService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _notesRoot = notesRoot ?? throw new ArgumentNullException(nameof(notesRoot));

            // Autosave debounce timer (500ms)
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _autoSaveTimer.Tick += async (s, e) =>
            {
                _autoSaveTimer.Stop();
                await SaveNoteAsync();
            };

            // Settings save debounce timer (500ms)
            _settingsSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _settingsSaveTimer.Tick += async (s, e) =>
            {
                _settingsSaveTimer.Stop();
                try
                {
                    await _settingsService.SaveSettingsAsync();
                }
                catch { }
            };

            // Initialize commands
            NewNoteCommand = new RelayCommand(async _ => await NewNoteAsync());
            NewFolderCommand = new RelayCommand(async _ => await NewFolderAsync());
            SaveCommand = new RelayCommand(async _ => await SaveNoteAsync(), () => CanSave);
            DeleteNoteCommand = new RelayCommand(async _ => await DeleteNoteAsync(), () => CanDelete);
            RefreshCommand = new RelayCommand(async _ => await LoadAllAsync());
            SearchCommand = new RelayCommand(_ => ExecuteSearch());
            ClearSearchCommand = new RelayCommand(_ => ClearSearch());
            ToggleModeCommand = new RelayCommand(_ => IsNotionMode = !IsNotionMode);
            ToggleThemeCommand = new RelayCommand(_ => IsDarkMode = !IsDarkMode);
            StartAddTagCommand = new RelayCommand(_ => { IsAddingTag = true; NewTagText = string.Empty; });
            ConfirmAddTagCommand = new RelayCommand(async _ => await AddTagAsync(NewTagText));
            CancelAddTagCommand = new RelayCommand(_ => { IsAddingTag = false; NewTagText = string.Empty; });
            RemoveTagCommand = new RelayCommand(async p =>
            {
                if (p is string tag)
                {
                    await RemoveTagAsync(tag);
                }
            });
            OpenSyncDialogCommand = new RelayCommand(_ => RequestOpenSyncDialog?.Invoke(this, EventArgs.Empty));
            OpenTrashBinCommand = new RelayCommand(_ => RequestOpenTrashBin?.Invoke(this, EventArgs.Empty));

            CloseTabCommand = new RelayCommand(async p =>
            {
                if (p is NoteTabViewModel tab)
                {
                    await CloseTabAsync(tab);
                }
                else if (_activeTab != null)
                {
                    await CloseTabAsync(_activeTab);
                }
            });
            NewTabCommand = new RelayCommand(async _ => await NewNoteAsync());
            SelectTabCommand = new RelayCommand(p =>
            {
                if (p is NoteTabViewModel tab)
                {
                    ActiveTab = tab;
                }
            });
            NextTabCommand = new RelayCommand(_ => SelectNextTab(1));
            PrevTabCommand = new RelayCommand(_ => SelectNextTab(-1));
            ToggleReadingModeCommand = new RelayCommand(_ => IsReadingMode = !IsReadingMode);
            ToggleSplitModeCommand = new RelayCommand(_ => IsSplitMode = !IsSplitMode);
            CloseSplitCommand = new RelayCommand(_ => IsSplitMode = false);

            _fileWatcher.Start();

            // Cloud sync monitoring
            _cloudSyncService.SyncStatusChanged += (s, status) =>
            {
                OnPropertyChanged(nameof(SyncStatusText));
                OnPropertyChanged(nameof(IsSyncActive));
            };

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await LoadAllAsync();
            await CheckCrashRecoveryAsync();
        }

        // Properties
        public NoteDocument? CurrentNote
        {
            get => _currentNote;
            set
            {
                if (_currentNote != value)
                {
                    // Save pending changes before switching
                    if (_isModified && _currentNote != null)
                    {
                        _autoSaveTimer.Stop();
                        var oldNote = _currentNote;
                        var oldContent = _editorContent;
                        oldNote.Content = oldContent;
                        oldNote.LastModified = DateTime.Now;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _noteRepository.UpdateNoteAsync(oldNote);
                                await _recoveryService.ClearDraftAsync(oldNote.FullPath);
                                _searchEngine.IndexDocument(oldNote);
                            }
                            catch { }
                        });
                        _isModified = false;
                    }

                    _currentNote = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FileNameText));
                    OnPropertyChanged(nameof(HasCurrentNote));
                    OnPropertyChanged(nameof(HasNoCurrentNote));
                    LoadNoteContent();
                    SyncActiveTab();
                    ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)DeleteNoteCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private NoteTabViewModel? _activeTab;
        public NoteTabViewModel? ActiveTab
        {
            get => _activeTab;
            set
            {
                if (_activeTab != value)
                {
                    if (_activeTab != null) _activeTab.IsActive = false;
                    _activeTab = value;
                    if (_activeTab != null)
                    {
                        _activeTab.IsActive = true;
                        if (_currentNote != _activeTab.Note)
                        {
                            CurrentNote = _activeTab.Note;
                        }
                    }
                    OnPropertyChanged();
                }
            }
        }

        private void SyncActiveTab()
        {
            if (_currentNote == null)
            {
                if (_activeTab != null)
                {
                    _activeTab.IsActive = false;
                    _activeTab = null;
                    OnPropertyChanged(nameof(ActiveTab));
                }
                return;
            }

            var existing = OpenTabs.FirstOrDefault(t => t.Note.Id == _currentNote.Id ||
                string.Equals(t.FullPath, _currentNote.FullPath, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = new NoteTabViewModel(_currentNote);
                OpenTabs.Add(existing);
            }
            else
            {
                existing.Note = _currentNote;
            }

            if (_activeTab != existing)
            {
                if (_activeTab != null) _activeTab.IsActive = false;
                _activeTab = existing;
                _activeTab.IsActive = true;
                _activeTab.IsModified = _isModified;
                OnPropertyChanged(nameof(ActiveTab));
            }
        }

        public string EditorContent
        {
            get => _editorContent;
            set
            {
                if (_editorContent != value)
                {
                    _editorContent = value;
                    OnPropertyChanged();

                    if (_currentNote != null && !_isRestoringDraft)
                    {
                        IsModified = true;
                        StatusText = "● Edited";

                        // Trigger autosave debounce
                        _autoSaveTimer.Stop();
                        _autoSaveTimer.Start();

                        // Save recovery draft
                        _ = _recoveryService.SaveDraftAsync(_currentNote.FullPath, _editorContent);
                    }

                    UpdateCounts();
                    UpdateOutline();
                }
            }
        }

        public bool IsModified
        {
            get => _isModified;
            private set
            {
                if (_isModified != value)
                {
                    _isModified = value;
                    OnPropertyChanged();
                    if (_activeTab != null)
                    {
                        _activeTab.IsModified = value;
                    }
                    ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FileNameText => _currentNote?.FileName ?? "No Note Selected";
        public bool HasCurrentNote => _currentNote != null;
        public bool HasNoCurrentNote => _currentNote == null;

        public static bool IsEmptyUntitledNote(NoteDocument? note)
        {
            if (note == null) return false;

            bool isUntitledTitle = string.IsNullOrWhiteSpace(note.Title) ||
                                  note.Title.Equals("Untitled", StringComparison.OrdinalIgnoreCase) ||
                                  System.Text.RegularExpressions.Regex.IsMatch(note.Title, @"^Untitled\s*\(\d+\)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            bool isUntitledFile = string.IsNullOrWhiteSpace(note.FileName) ||
                                 note.FileName.StartsWith("Untitled", StringComparison.OrdinalIgnoreCase);

            bool hasNoContent = string.IsNullOrWhiteSpace(note.Content);
            bool hasNoTags = note.Tags == null || note.Tags.Length == 0;

            return (isUntitledTitle || isUntitledFile) && hasNoContent && hasNoTags;
        }

        public string CurrentNoteTitle
        {
            get => _currentNote?.Title ?? string.Empty;
            set
            {
                if (_currentNote != null && _currentNote.Title != value)
                {
                    _currentNote.Title = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FileNameText));

                    if (_activeTab != null)
                    {
                        _activeTab.Title = !string.IsNullOrWhiteSpace(value) ? value : _currentNote.FileName;
                    }

                    // Immediately synchronize left side Explorer title
                    UpdateFolderNodeTitle(FolderRoot, _currentNote.FullPath, value);
                    foreach (var node in FolderNodes)
                    {
                        UpdateFolderNodeTitle(node, _currentNote.FullPath, value);
                    }

                    IsModified = true;
                    _autoSaveTimer.Stop();
                    _autoSaveTimer.Start();
                }
            }
        }

        public string WordCountText
        {
            get => _wordCountText;
            private set
            {
                if (_wordCountText != value)
                {
                    _wordCountText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LineCountText
        {
            get => _lineCountText;
            private set
            {
                if (_lineCountText != value)
                {
                    _lineCountText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SearchTerm
        {
            get => _searchTerm;
            set
            {
                if (_searchTerm != value)
                {
                    _searchTerm = value;
                    OnPropertyChanged();
                    ExecuteSearch();
                }
            }
        }

        public bool HasSearchResults
        {
            get => _hasSearchResults;
            set
            {
                if (_hasSearchResults != value)
                {
                    _hasSearchResults = value;
                    OnPropertyChanged();
                }
            }
        }

        public SearchResult? SelectedSearchResult
        {
            get => _selectedSearchResult;
            set
            {
                if (_selectedSearchResult != value)
                {
                    _selectedSearchResult = value;
                    OnPropertyChanged();
                    if (value != null)
                    {
                        NavigateToSearchResult(value);
                    }
                }
            }
        }

        public FolderNode? FolderRoot
        {
            get => _folderRoot;
            private set
            {
                if (_folderRoot != value)
                {
                    _folderRoot = value;
                    OnPropertyChanged();
                }
            }
        }

        public Tag? SelectedTag
        {
            get => _selectedTag;
            set
            {
                if (_selectedTag != value)
                {
                    _selectedTag = value;
                    OnPropertyChanged();
                    if (value != null)
                    {
                        SearchTerm = $"tag:{value.Name}";
                    }
                }
            }
        }

        private bool _isNotionMode = true;
        public bool IsNotionMode
        {
            get => _isNotionMode;
            set
            {
                if (_isNotionMode != value)
                {
                    _isNotionMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRawMarkdownMode));
                }
            }
        }

        public bool IsRawMarkdownMode => !_isNotionMode;

        private bool _isDarkMode = true;
        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode != value)
                {
                    _isDarkMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThemeButtonText));
                    ThemeChanged?.Invoke(this, _isDarkMode);
                }
            }
        }

        public string ThemeButtonText => _isDarkMode ? "Light" : "Dark";

        public event EventHandler<bool>? ThemeChanged;

        private bool _isAddingTag;
        public bool IsAddingTag
        {
            get => _isAddingTag;
            set
            {
                if (_isAddingTag != value)
                {
                    _isAddingTag = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _newTagText = string.Empty;
        public string NewTagText
        {
            get => _newTagText;
            set
            {
                if (_newTagText != value)
                {
                    _newTagText = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isReadingMode;
        public bool IsReadingMode
        {
            get => _isReadingMode;
            set
            {
                if (_isReadingMode != value)
                {
                    _isReadingMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanEdit));
                }
            }
        }

        public bool CanEdit => !IsReadingMode;

        private bool _isSplitMode;
        public bool IsSplitMode
        {
            get => _isSplitMode;
            set
            {
                if (_isSplitMode != value)
                {
                    _isSplitMode = value;
                    OnPropertyChanged();
                    if (_isSplitMode && _secondaryNote == null && _currentNote != null)
                    {
                        SecondaryNote = _currentNote;
                    }
                }
            }
        }

        private NoteDocument? _secondaryNote;
        public NoteDocument? SecondaryNote
        {
            get => _secondaryNote;
            set
            {
                if (_secondaryNote != value)
                {
                    _secondaryNote = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SecondaryNoteTitle));
                    OnPropertyChanged(nameof(HasSecondaryNote));
                    if (_secondaryNote != null)
                    {
                        _secondaryEditorContent = _secondaryNote.Content ?? string.Empty;
                        OnPropertyChanged(nameof(SecondaryEditorContent));
                    }
                }
            }
        }

        public bool HasSecondaryNote => _secondaryNote != null;

        public string SecondaryNoteTitle => _secondaryNote?.Title ?? _currentNote?.Title ?? "Untitled";

        private string _secondaryEditorContent = string.Empty;
        public string SecondaryEditorContent
        {
            get => _secondaryEditorContent;
            set
            {
                if (_secondaryEditorContent != value)
                {
                    _secondaryEditorContent = value;
                    OnPropertyChanged();
                }
            }
        }

        // Commands
        public ICommand NewNoteCommand { get; }
        public ICommand NewFolderCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteNoteCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ToggleModeCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand StartAddTagCommand { get; }
        public ICommand ConfirmAddTagCommand { get; }
        public ICommand CancelAddTagCommand { get; }
        public ICommand RemoveTagCommand { get; }
        public ICommand OpenSyncDialogCommand { get; }
        public ICommand OpenTrashBinCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand NewTabCommand { get; }
        public ICommand SelectTabCommand { get; }
        public ICommand NextTabCommand { get; }
        public ICommand PrevTabCommand { get; }
        public ICommand ToggleReadingModeCommand { get; }
        public ICommand ToggleSplitModeCommand { get; }
        public ICommand CloseSplitCommand { get; }

        public event EventHandler? RequestOpenSyncDialog;
        public event EventHandler? RequestOpenTrashBin;

        public void SelectNextTab(int direction)
        {
            if (OpenTabs.Count <= 1 || _activeTab == null) return;
            int currentIndex = OpenTabs.IndexOf(_activeTab);
            if (currentIndex < 0) return;
            int nextIndex = (currentIndex + direction + OpenTabs.Count) % OpenTabs.Count;
            ActiveTab = OpenTabs[nextIndex];
        }

        public async Task CloseTabAsync(NoteTabViewModel tab)
        {
            if (tab == null) return;

            if (tab == _activeTab && _isModified)
            {
                await SaveNoteAsync();
            }

            // If the note being closed was an empty untitled note that has no data, delete it so we don't litter empty files on disk
            if (tab.Note != null && IsEmptyUntitledNote(tab.Note) && !tab.IsModified)
            {
                try
                {
                    if (File.Exists(tab.Note.FullPath))
                    {
                        var content = await File.ReadAllTextAsync(tab.Note.FullPath);
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            File.Delete(tab.Note.FullPath);
                        }
                    }
                    Notes.Remove(tab.Note);
                    _searchEngine.RemoveDocument(tab.Note.Id);
                    await RefreshFolderTreeAsync();
                }
                catch { }
            }

            int index = OpenTabs.IndexOf(tab);
            OpenTabs.Remove(tab);

            if (tab == _activeTab)
            {
                if (OpenTabs.Count > 0)
                {
                    int nextIndex = Math.Min(index, OpenTabs.Count - 1);
                    ActiveTab = OpenTabs[nextIndex];
                }
                else
                {
                    CurrentNote = null;
                }
            }
        }

        public async Task OpenNoteByPathAsync(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return;

            var existing = Notes.FirstOrDefault(n => string.Equals(n.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                CurrentNote = existing;
                return;
            }

            if (File.Exists(fullPath))
            {
                var loaded = await _noteRepository.GetNoteByPathAsync(fullPath);
                if (loaded != null)
                {
                    if (!Notes.Any(n => n.Id == loaded.Id))
                    {
                        Notes.Add(loaded);
                    }
                    CurrentNote = loaded;
                }
            }
        }

        public bool IsSyncActive => _cloudSyncService.GetSyncStatus().State == CloudSyncState.Synced;
        public string SyncStatusText => _cloudSyncService.GetSyncStatus().Message;

        public async Task SwitchNotesRootAsync(string newPath)
        {
            if (string.IsNullOrWhiteSpace(newPath) || !Directory.Exists(newPath)) return;
            _notesRoot = newPath;
            _trashBinService.UpdateNotesRoot(newPath);
            await LoadAllAsync();
        }

        private bool CanSave => _currentNote != null && _isModified;
        private bool CanDelete => _currentNote != null;

        // Core Operations
        public async Task LoadAllAsync()
        {
            try
            {
                var notesList = (await _noteRepository.GetAllNotesAsync()).ToList();

                // Clean up any 0-byte orphan Untitled files that have no data
                var emptyUntitled = notesList.Where(n => IsEmptyUntitledNote(n)).ToList();
                foreach (var emptyNote in emptyUntitled)
                {
                    try
                    {
                        var fi = new FileInfo(emptyNote.FullPath);
                        if (fi.Exists && fi.Length == 0)
                        {
                            File.Delete(emptyNote.FullPath);
                            notesList.Remove(emptyNote);
                        }
                    }
                    catch { }
                }

                // If no notes exist yet, create a welcome note
                if (notesList.Count == 0)
                {
                    var welcomePath = Path.Combine(_notesRoot, "Welcome to QuickNote.md");
                    var welcomeContent = "---\ntags:\n  - quickstart\n  - guide\n---\n\n# Welcome to QuickNote\n\nQuickNote is a fast, local-first Markdown note-taking app.\n\n## Getting Started\n\nHere are some features you can try:\n\n### Markdown Lists\n\n- [ ] Try out interactive checkboxes\n- [x] Fast keyboard shortcuts (Ctrl+S to save, Ctrl+N for new note)\n- Easy bullet lists and numbered lists\n\n### Document Outline\n\nLook at the outline panel on the right! Click any heading to jump to it directly.\n\n### Real-time Search\n\nUse the search bar at the top to search titles, headings, tags, or body text with weighted ranking.\n";
                    await File.WriteAllTextAsync(welcomePath, welcomeContent);
                    notesList = (await _noteRepository.GetAllNotesAsync()).ToList();
                }

                Notes.Clear();
                _searchEngine.Clear();

                foreach (var note in notesList)
                {
                    Notes.Add(note);
                    _searchEngine.IndexDocument(note);
                }

                // Update tags
                Tags.Clear();
                foreach (var tag in _tagService.GetAllTags(Notes))
                {
                    Tags.Add(tag);
                }

                // Update folder tree
                await RefreshFolderTreeAsync();

                // Select current or first note
                if (CurrentNote == null || !Notes.Any(n => n.Id == CurrentNote.Id))
                {
                    CurrentNote = Notes.FirstOrDefault();
                }

                _ = CleanOrphanedAssetsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading notes: {ex.Message}");
            }
        }

        private void LoadNoteContent()
        {
            CurrentNoteTags.Clear();
            if (_currentNote != null)
            {
                if (_currentNote.Tags != null)
                {
                    foreach (var tag in _currentNote.Tags)
                    {
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            CurrentNoteTags.Add(tag);
                        }
                    }
                }

                var rawContent = _currentNote.Content ?? string.Empty;
                var (extractedTitle, bodyContent) = SeparateTitleAndBody(rawContent, _currentNote.Title);
                if (!string.IsNullOrWhiteSpace(extractedTitle))
                {
                    _currentNote.Title = extractedTitle;
                }

                OnPropertyChanged(nameof(CurrentNoteTitle));

                _editorContent = bodyContent;
                OnPropertyChanged(nameof(EditorContent));
                IsModified = false;
                StatusText = "● Saved";
                UpdateCounts();
                UpdateOutline();
            }
            else
            {
                _editorContent = string.Empty;
                OnPropertyChanged(nameof(CurrentNoteTitle));
                OnPropertyChanged(nameof(EditorContent));
                IsModified = false;
                StatusText = "○ No note selected";
                WordCountText = "0 words";
                LineCountText = "0 lines";
                OutlineHeadings.Clear();
            }
        }

        public async Task SaveNoteAsync()
        {
            if (_currentNote == null) return;

            try
            {
                StatusText = "● Saving...";
                var fullMarkdown = CombineTitleAndBody(CurrentNoteTitle, EditorContent);
                _currentNote.Content = fullMarkdown;
                _currentNote.LastModified = DateTime.Now;

                await _noteRepository.UpdateNoteAsync(_currentNote);
                await _recoveryService.ClearDraftAsync(_currentNote.FullPath);

                // Update search index with new content
                _searchEngine.IndexDocument(_currentNote);

                IsModified = false;
                StatusText = "● Saved";
                _ = CleanOrphanedAssetsAsync();
            }
            catch (Exception ex)
            {
                StatusText = "⚠ Save failed";
                System.Diagnostics.Debug.WriteLine($"Error saving note: {ex.Message}");
            }
        }

        public async Task CleanOrphanedAssetsAsync()
        {
            var root = _notesRoot;
            var currentFullPath = _currentNote?.FullPath;
            var currentEditorText = _editorContent;
            var openTabContents = OpenTabs.Where(t => t.Note?.Content != null).Select(t => t.Note!.Content).ToList();

            await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

                    var dotAssetsDir = Path.Combine(root, ".assets");
                    var legacyAssetsDir = Path.Combine(root, "assets");

                    // 1. Unify: if legacy 'assets' folder exists, migrate all files to '.assets' and delete 'assets'
                    if (Directory.Exists(legacyAssetsDir))
                    {
                        Directory.CreateDirectory(dotAssetsDir);
                        foreach (var file in Directory.GetFiles(legacyAssetsDir))
                        {
                            var dest = Path.Combine(dotAssetsDir, Path.GetFileName(file));
                            try
                            {
                                if (!File.Exists(dest))
                                {
                                    File.Move(file, dest);
                                }
                                else
                                {
                                    File.Delete(file);
                                }
                            }
                            catch { }
                        }

                        try
                        {
                            if (Directory.GetFiles(legacyAssetsDir).Length == 0 && Directory.GetDirectories(legacyAssetsDir).Length == 0)
                            {
                                Directory.Delete(legacyAssetsDir, false);
                            }
                        }
                        catch { }
                    }

                    if (!Directory.Exists(dotAssetsDir)) return;

                    var assetFiles = Directory.GetFiles(dotAssetsDir);
                    if (assetFiles.Length == 0) return;

                    // 2. Collect all markdown text across all notes + active editor content + open tabs
                    var allMdFiles = Directory.GetFiles(root, "*.md", SearchOption.AllDirectories);
                    var allContent = new System.Text.StringBuilder();

                    foreach (var mdFile in allMdFiles)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(currentFullPath) && string.Equals(currentFullPath, mdFile, StringComparison.OrdinalIgnoreCase))
                            {
                                allContent.Append(" ");
                                allContent.Append(currentEditorText);
                            }
                            else
                            {
                                allContent.Append(" ");
                                allContent.Append(File.ReadAllText(mdFile));
                            }
                        }
                        catch { }
                    }

                    foreach (var tabContent in openTabContents)
                    {
                        allContent.Append(" ");
                        allContent.Append(tabContent);
                    }

                    if (!string.IsNullOrEmpty(currentEditorText))
                    {
                        allContent.Append(" ");
                        allContent.Append(currentEditorText);
                    }

                    var combinedText = allContent.ToString();

                    // 3. For each asset file, if its filename is not referenced anywhere in combined markdown text, delete it
                    foreach (var assetPath in assetFiles)
                    {
                        var fileName = Path.GetFileName(assetPath);
                        if (string.IsNullOrWhiteSpace(fileName)) continue;

                        if (combinedText.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            try
                            {
                                File.Delete(assetPath);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            });
        }

        public async Task NewNoteAsync(string? targetDirectory = null)
        {
            // If the currently active note is ALREADY an empty untitled note with no data,
            // don't create another redundant empty file — just keep focus on it!
            if (_currentNote != null && IsEmptyUntitledNote(_currentNote) && !_isModified)
            {
                return;
            }

            // 1. Flush and save any current note edits before creating new note
            if (_isModified && _currentNote != null)
            {
                _autoSaveTimer.Stop();
                await SaveNoteAsync();
            }

            targetDirectory ??= _currentNote?.FolderPath ?? _notesRoot;
            if (!Directory.Exists(targetDirectory))
            {
                targetDirectory = _notesRoot;
            }

            string baseName = "Untitled";
            string fileName = $"{baseName}.md";
            string fullPath = Path.Combine(targetDirectory, fileName);
            int counter = 1;

            while (File.Exists(fullPath))
            {
                // If the existing file is 0 bytes and has no data, reuse it instead of creating Untitled (1), Untitled (2)...
                try
                {
                    var fileInfo = new FileInfo(fullPath);
                    if (fileInfo.Length == 0)
                    {
                        break;
                    }
                }
                catch { }

                fileName = $"{baseName} ({counter++}).md";
                fullPath = Path.Combine(targetDirectory, fileName);
            }

            // Create note with clean empty initial content (only if not already existing)
            if (!File.Exists(fullPath))
            {
                await File.WriteAllTextAsync(fullPath, string.Empty);
            }

            var createdNote = await _noteRepository.GetNoteByPathAsync(fullPath);
            if (createdNote != null)
            {
                createdNote.Title = string.Empty; // clean Untitled
                if (!Notes.Any(n => n.Id == createdNote.Id || string.Equals(n.FullPath, createdNote.FullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    Notes.Add(createdNote);
                }
                _searchEngine.IndexDocument(createdNote);

                // Refresh folder tree safely while preserving expanded/collapsed states
                await RefreshFolderTreeAsync(targetDirectory);

                CurrentNote = createdNote;
                ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteNoteCommand).RaiseCanExecuteChanged();
            }
        }

        public void SetFolderExpandedState(string relativePath, bool isExpanded)
        {
            if (_isRebuildingFolderTree) return;

            var key = FolderService.NormalizePathKey(relativePath);
            if (!string.IsNullOrEmpty(key))
            {
                _settingsService.Settings.FolderExpandedStates[key] = isExpanded;
                DebounceSaveSettings();
            }
        }

        private void DebounceSaveSettings()
        {
            _settingsSaveTimer.Stop();
            _settingsSaveTimer.Start();
        }

        private Dictionary<string, bool> GetCurrentFolderExpandedStates()
        {
            var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (_settingsService.Settings.FolderExpandedStates != null)
            {
                foreach (var kvp in _settingsService.Settings.FolderExpandedStates)
                {
                    states[FolderService.NormalizePathKey(kvp.Key)] = kvp.Value;
                }
            }

            CaptureLiveExpandedStates(FolderNodes, states);
            return states;
        }

        private void CaptureLiveExpandedStates(IEnumerable<FolderNode> nodes, Dictionary<string, bool> target)
        {
            foreach (var node in nodes)
            {
                if (node.IsDirectory)
                {
                    var key = FolderService.NormalizePathKey(node.RelativePath);
                    if (!string.IsNullOrEmpty(key))
                    {
                        target[key] = node.IsExpanded;
                    }
                    CaptureLiveExpandedStates(node.Children, target);
                }
            }
        }

        private void EnsureFolderPathVisible(FolderNode? root, string folderFullPath)
        {
            if (root == null || string.IsNullOrWhiteSpace(folderFullPath)) return;

            var normalizedTarget = folderFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            void ExpandPath(FolderNode node)
            {
                if (!node.IsDirectory) return;
                var nodePath = node.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(nodePath, normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                    normalizedTarget.StartsWith(nodePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    normalizedTarget.StartsWith(nodePath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    node.IsExpanded = true;
                }

                foreach (var child in node.Children)
                {
                    ExpandPath(child);
                }
            }

            ExpandPath(root);
        }

        private void HookFolderTreeEvents(FolderNode node)
        {
            if (node.IsDirectory)
            {
                node.PropertyChanged -= FolderNode_PropertyChanged;
                node.PropertyChanged += FolderNode_PropertyChanged;
            }

            foreach (var child in node.Children)
            {
                HookFolderTreeEvents(child);
            }
        }

        private void UnhookFolderTreeEvents(FolderNode? node)
        {
            if (node == null) return;
            if (node.IsDirectory)
            {
                node.PropertyChanged -= FolderNode_PropertyChanged;
            }

            foreach (var child in node.Children)
            {
                UnhookFolderTreeEvents(child);
            }
        }

        private void FolderNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isRebuildingFolderTree) return;

            if (e.PropertyName == nameof(FolderNode.IsExpanded) && sender is FolderNode node && node.IsDirectory)
            {
                var key = FolderService.NormalizePathKey(node.RelativePath);
                if (!string.IsNullOrEmpty(key))
                {
                    _settingsService.Settings.FolderExpandedStates[key] = node.IsExpanded;
                    DebounceSaveSettings();
                }
            }
        }

        private async Task RefreshFolderTreeAsync(string? targetDirectoryToEnsureVisible = null)
        {
            var states = GetCurrentFolderExpandedStates();
            var newRoot = await _folderService.BuildFolderTreeAsync(_notesRoot, states);
            SyncFolderTreeNotes(newRoot);

            if (!string.IsNullOrEmpty(targetDirectoryToEnsureVisible) &&
                !string.Equals(targetDirectoryToEnsureVisible, _notesRoot, StringComparison.OrdinalIgnoreCase))
            {
                EnsureFolderPathVisible(newRoot, targetDirectoryToEnsureVisible);
            }

            UnhookFolderTreeEvents(_folderRoot);

            _isRebuildingFolderTree = true;
            try
            {
                FolderRoot = newRoot;
                FolderNodes.Clear();
                if (FolderRoot != null)
                {
                    foreach (var child in FolderRoot.Children)
                    {
                        FolderNodes.Add(child);
                    }
                    HookFolderTreeEvents(FolderRoot);
                }
            }
            finally
            {
                _isRebuildingFolderTree = false;
            }
        }

        private void SyncFolderTreeNotes(FolderNode? node)
        {
            if (node == null) return;
            if (!node.IsDirectory)
            {
                var note = Notes.FirstOrDefault(n => string.Equals(n.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase));
                if (note != null)
                {
                    node.Note = note;
                    node.Title = note.Title;
                }
            }
            foreach (var child in node.Children)
            {
                SyncFolderTreeNotes(child);
            }
        }

        public void UpdateFolderNodeTitle(FolderNode? node, string fullPath, string newTitle)
        {
            if (node == null) return;
            if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                node.Title = newTitle;
                return;
            }
            foreach (var child in node.Children)
            {
                UpdateFolderNodeTitle(child, fullPath, newTitle);
            }
        }

        public async Task AddTagAsync(string? tagName)
        {
            IsAddingTag = false;
            if (string.IsNullOrWhiteSpace(tagName) || _currentNote == null) return;

            var clean = tagName.Trim().TrimStart('#').Trim();
            if (string.IsNullOrEmpty(clean)) return;

            if (!CurrentNoteTags.Any(t => string.Equals(t, clean, StringComparison.OrdinalIgnoreCase)))
            {
                CurrentNoteTags.Add(clean);
                _currentNote.Tags = CurrentNoteTags.ToArray();
                UpdateGlobalTags();
                IsModified = true;
                _autoSaveTimer.Stop();
                await SaveNoteAsync();
            }
        }

        public async Task RemoveTagAsync(string? tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName) || _currentNote == null) return;

            var clean = tagName.Trim();
            var existing = CurrentNoteTags.FirstOrDefault(t => string.Equals(t, clean, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                CurrentNoteTags.Remove(existing);
                _currentNote.Tags = CurrentNoteTags.ToArray();
                UpdateGlobalTags();
                IsModified = true;
                _autoSaveTimer.Stop();
                await SaveNoteAsync();
            }
        }

        private void UpdateGlobalTags()
        {
            Tags.Clear();
            foreach (var tag in _tagService.GetAllTags(Notes))
            {
                Tags.Add(tag);
            }
        }

        private (string title, string body) SeparateTitleAndBody(string content, string fallbackTitle)
        {
            if (string.IsNullOrEmpty(content))
            {
                return (fallbackTitle, string.Empty);
            }

            var normalized = content.Replace("\r\n", "\n");

            // Check for front matter
            string mainContent = normalized;
            if (normalized.StartsWith("---\n"))
            {
                var endIndex = normalized.IndexOf("\n---\n", 4);
                if (endIndex > 0)
                {
                    mainContent = normalized.Substring(endIndex + 5);
                }
            }

            var trimmedMain = mainContent.TrimStart('\n');
            if (trimmedMain.StartsWith("# ") && !trimmedMain.StartsWith("## "))
            {
                var newlineIndex = trimmedMain.IndexOf('\n');
                if (newlineIndex >= 0)
                {
                    var title = trimmedMain.Substring(2, newlineIndex - 2).Trim();
                    var body = trimmedMain.Substring(newlineIndex + 1).TrimStart('\n');
                    return (title, body);
                }
                else
                {
                    var title = trimmedMain.Substring(2).Trim();
                    return (title, string.Empty);
                }
            }

            return (fallbackTitle, mainContent);
        }

        private string CombineTitleAndBody(string title, string body)
        {
            body ??= string.Empty;
            var normalized = body.Replace("\r\n", "\n");

            // Check if body accidentally contains front matter and strip it
            string mainContent = normalized;
            if (normalized.StartsWith("---\n"))
            {
                var endIndex = normalized.IndexOf("\n---\n", 4);
                if (endIndex > 0)
                {
                    mainContent = normalized.Substring(endIndex + 5);
                }
            }

            var trimmedMain = mainContent.TrimStart('\n');

            // Generate clean YAML front matter if note has tags
            string frontMatter = string.Empty;
            if (_currentNote?.Tags != null && _currentNote.Tags.Length > 0)
            {
                var validTags = _currentNote.Tags
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
                    {
                        sb.AppendLine($"  - {tag}");
                    }
                    sb.AppendLine("---");
                    frontMatter = sb.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return string.IsNullOrEmpty(frontMatter) ? mainContent : frontMatter + "\n" + mainContent;
            }

            // If mainContent starts with an H1 heading, replace that heading
            if (trimmedMain.StartsWith("# ") && !trimmedMain.StartsWith("## "))
            {
                var newlineIndex = trimmedMain.IndexOf('\n');
                var rest = newlineIndex >= 0 ? trimmedMain.Substring(newlineIndex + 1).TrimStart('\n') : string.Empty;
                var result = $"# {title}\n\n{rest}";
                return string.IsNullOrEmpty(frontMatter) ? result : frontMatter + "\n" + result;
            }

            var combined = string.IsNullOrWhiteSpace(trimmedMain) ? $"# {title}\n" : $"# {title}\n\n{trimmedMain}";
            return string.IsNullOrEmpty(frontMatter) ? combined : frontMatter + "\n" + combined;
        }

        public async Task NewFolderAsync()
        {
            var targetDirectory = _currentNote?.FolderPath ?? _notesRoot;
            await _folderService.CreateFolderAsync(targetDirectory, "New Folder");
            await LoadAllAsync();
        }

        public async Task NewNoteInFolderAsync(string folderPath)
        {
            await NewNoteAsync(folderPath);
        }

        public async Task NewSubfolderInFolderAsync(string parentFolderPath)
        {
            await _folderService.CreateFolderAsync(parentFolderPath, "New Folder");
            await LoadAllAsync();
        }

        public async Task<string> MoveItemAsync(FolderNode node, string targetDirectory)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.FullPath) || string.IsNullOrWhiteSpace(targetDirectory)) return string.Empty;

            // If item does not exist on disk, reload tree to ensure state matches disk and return
            if (!File.Exists(node.FullPath) && !Directory.Exists(node.FullPath))
            {
                await LoadAllAsync();
                return string.Empty;
            }

            var currentDir = Path.GetDirectoryName(node.FullPath);
            if (string.Equals(currentDir, targetDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return node.FullPath;
            }

            if (_isModified && _currentNote != null && string.Equals(_currentNote.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                _autoSaveTimer.Stop();
                await SaveNoteAsync();
            }

            var newPath = await _folderService.MoveItemAsync(node.FullPath, targetDirectory);
            node.FullPath = newPath;
            if (!string.IsNullOrEmpty(_notesRoot))
            {
                node.RelativePath = Path.GetRelativePath(_notesRoot, newPath);
            }

            await LoadAllAsync();

            if (!node.IsDirectory)
            {
                var movedNote = Notes.FirstOrDefault(n => string.Equals(n.FullPath, newPath, StringComparison.OrdinalIgnoreCase));
                if (movedNote != null)
                {
                    CurrentNote = movedNote;
                }
            }

            return newPath;
        }

        public async Task<string> CopyItemAsync(FolderNode node, string targetDirectory)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.FullPath)) return string.Empty;

            if (_isModified && _currentNote != null && string.Equals(_currentNote.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                _autoSaveTimer.Stop();
                await SaveNoteAsync();
            }

            var newPath = await _folderService.CopyItemAsync(node.FullPath, targetDirectory);
            await LoadAllAsync();

            if (!node.IsDirectory)
            {
                var copiedNote = Notes.FirstOrDefault(n => string.Equals(n.FullPath, newPath, StringComparison.OrdinalIgnoreCase));
                if (copiedNote != null)
                {
                    CurrentNote = copiedNote;
                }
            }

            return newPath;
        }

        public async Task<string> RenameItemAsync(FolderNode node, string newName)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.FullPath)) return string.Empty;

            if (_isModified && _currentNote != null && string.Equals(_currentNote.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                _autoSaveTimer.Stop();
                await SaveNoteAsync();
            }

            var newPath = await _folderService.RenameItemAsync(node.FullPath, newName);
            await LoadAllAsync();

            if (!node.IsDirectory)
            {
                var renamedNote = Notes.FirstOrDefault(n => string.Equals(n.FullPath, newPath, StringComparison.OrdinalIgnoreCase));
                if (renamedNote != null)
                {
                    CurrentNote = renamedNote;
                }
            }

            return newPath;
        }

        public async Task DeleteNodeAsync(FolderNode node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.FullPath)) return;

            if (node.IsDirectory)
            {
                if (_currentNote != null && _currentNote.FullPath.StartsWith(node.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentNote = null;
                }
                await _trashBinService.MoveToTrashAsync(node.FullPath);
            }
            else
            {
                if (_currentNote != null && string.Equals(_currentNote.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentNote = null;
                }
                await _trashBinService.MoveToTrashAsync(node.FullPath);
            }

            var tabsToClose = OpenTabs.Where(t => t.FullPath.StartsWith(node.FullPath, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var t in tabsToClose)
            {
                OpenTabs.Remove(t);
            }

            await LoadAllAsync();
        }

        public List<(string DisplayName, string FullPath)> GetAllFolderOptions()
        {
            var result = new List<(string DisplayName, string FullPath)>
            {
                ("[Root Explorer]", _notesRoot)
            };

            if (FolderRoot != null)
            {
                CollectFolderOptions(FolderRoot, "  ", result);
            }

            return result;
        }

        private void CollectFolderOptions(FolderNode parent, string indent, List<(string DisplayName, string FullPath)> list)
        {
            foreach (var child in parent.Children)
            {
                if (child.IsDirectory)
                {
                    list.Add(($"{indent}{child.DisplayName}", child.FullPath));
                    CollectFolderOptions(child, indent + "  ", list);
                }
            }
        }

        public async Task DeleteNoteAsync()
        {
            if (_currentNote == null) return;

            var idToDelete = _currentNote.Id;
            var pathToDelete = _currentNote.FullPath;

            await _trashBinService.MoveToTrashAsync(pathToDelete);
            await _recoveryService.ClearDraftAsync(pathToDelete);
            _searchEngine.RemoveDocument(idToDelete);

            var tabToClose = OpenTabs.FirstOrDefault(t => t.Note.Id == idToDelete);
            if (tabToClose != null)
            {
                OpenTabs.Remove(tabToClose);
            }

            var nextNote = Notes.FirstOrDefault(n => n.Id != idToDelete);
            CurrentNote = nextNote;

            await LoadAllAsync();
        }

        public void ExecuteSearch()
        {
            SearchResults.Clear();
            if (string.IsNullOrWhiteSpace(SearchTerm))
            {
                HasSearchResults = false;
                return;
            }

            var results = _searchEngine.Search(SearchTerm).Take(30).ToList();
            foreach (var r in results)
            {
                SearchResults.Add(r);
            }

            HasSearchResults = SearchResults.Count > 0;
        }

        public void ClearSearch()
        {
            SearchTerm = string.Empty;
            SearchResults.Clear();
            HasSearchResults = false;
        }

        public void NavigateToSearchResult(SearchResult result)
        {
            if (result == null) return;

            // Load note if not current
            if (CurrentNote == null || CurrentNote.Id != result.DocumentId)
            {
                var targetNote = Notes.FirstOrDefault(n => n.Id == result.DocumentId);
                if (targetNote != null)
                {
                    CurrentNote = targetNote;
                }
            }

            // Request navigation in editor
            NavigationRequested?.Invoke(this, new NavigationEventArgs
            {
                LineNumber = result.LineNumber,
                StartOffset = result.StartOffset,
                Length = result.MatchedText?.Length ?? 0
            });

            // Close search results overlay
            HasSearchResults = false;
            _selectedSearchResult = null;
            OnPropertyChanged(nameof(SelectedSearchResult));
        }

        public void NavigateToHeading(Heading heading)
        {
            if (heading == null) return;

            NavigationRequested?.Invoke(this, new NavigationEventArgs
            {
                LineNumber = heading.LineNumber,
                StartOffset = heading.StartOffset,
                Length = heading.Text?.Length ?? 0,
                TopAlign = true
            });
        }

        private void UpdateOutline()
        {
            OutlineHeadings.Clear();
            if (string.IsNullOrEmpty(_editorContent)) return;

            var tree = _outlineService.BuildOutline(_editorContent);
            foreach (var h in tree)
            {
                OutlineHeadings.Add(h);
            }
        }

        private void UpdateCounts()
        {
            if (string.IsNullOrWhiteSpace(_editorContent))
            {
                WordCountText = "0 words";
                LineCountText = "1 line";
                return;
            }

            int words = 0;
            bool inWord = false;
            int lines = 1;

            for (int i = 0; i < _editorContent.Length; i++)
            {
                char c = _editorContent[i];
                if (c == '\n') lines++;

                if (char.IsWhiteSpace(c))
                {
                    inWord = false;
                }
                else if (!inWord)
                {
                    inWord = true;
                    words++;
                }
            }

            WordCountText = $"{words:N0} words";
            LineCountText = $"{lines:N0} lines";
        }

        private async Task CheckCrashRecoveryAsync()
        {
            try
            {
                var items = (await _recoveryService.CheckForRecoverableItemsAsync()).ToList();
                if (items.Count == 0) return;

                foreach (var item in items)
                {
                    var confirm = ConfirmDialog.ShowConfirm(
                        Application.Current?.MainWindow,
                        "QuickNote Recovery",
                        $"Unsaved changes were recovered for:\n{item.OriginalFilePath}\n\nDo you want to restore these changes?",
                        "Restore",
                        "Discard",
                        isDanger: false,
                        isDarkMode: IsDarkMode);

                    if (confirm)
                    {
                        var note = await _noteRepository.GetNoteByPathAsync(item.OriginalFilePath);
                        if (note != null)
                        {
                            CurrentNote = note;
                            _isRestoringDraft = true;
                            try
                            {
                                EditorContent = item.Content;
                                await SaveNoteAsync();
                            }
                            finally
                            {
                                _isRestoringDraft = false;
                            }
                        }
                        else
                        {
                            // If the original note file does not exist on disk (e.g. was a new untitled note that crashed before initial save),
                            // recreate it from the recovered draft!
                            var fileName = Path.GetFileName(item.OriginalFilePath);
                            if (string.IsNullOrWhiteSpace(fileName)) fileName = "Recovered Note.md";
                            var dir = Path.GetDirectoryName(item.OriginalFilePath);
                            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                            {
                                dir = _notesRoot;
                            }

                            var (extractedTitle, body) = SeparateTitleAndBody(item.Content, Path.GetFileNameWithoutExtension(fileName));
                            var fullMarkdown = CombineTitleAndBody(extractedTitle, body);
                            var targetPath = Path.Combine(dir, fileName);
                            await File.WriteAllTextAsync(targetPath, fullMarkdown);
                            var newNote = await _noteRepository.GetNoteByPathAsync(targetPath);
                            if (newNote != null)
                            {
                                if (!Notes.Any(n => n.Id == newNote.Id || string.Equals(n.FullPath, newNote.FullPath, StringComparison.OrdinalIgnoreCase)))
                                {
                                    Notes.Add(newNote);
                                }
                                CurrentNote = newNote;
                                _isRestoringDraft = true;
                                try
                                {
                                    EditorContent = body;
                                    await SaveNoteAsync();
                                }
                                finally
                                {
                                    _isRestoringDraft = false;
                                }
                            }
                        }
                    }

                    // Always clear the draft file once handled (whether restored or discarded)
                    await _recoveryService.ClearDraftAsync(item.OriginalFilePath);
                    try
                    {
                        if (File.Exists(item.RecoveryFilePath))
                        {
                            File.Delete(item.RecoveryFilePath);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}