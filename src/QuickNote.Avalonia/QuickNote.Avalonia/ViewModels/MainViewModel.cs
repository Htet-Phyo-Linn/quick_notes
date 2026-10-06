using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Markdown;
using QuickNote.Core.Models;
using QuickNote.Core.Utilities;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.Avalonia.ViewModels
{
    // Core fields, observable properties, constructor and top-level lifecycle.
    // Other regions are split into MainViewModel.*.cs partial files.
    public partial class MainViewModel : ViewModelBase
    {
        // ── Services ────────────────────────────────────────────────
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
        private readonly IMarkdownParser _markdownParser;
        private string _notesRoot;

        // ── Internal state ───────────────────────────────────────────
        private readonly DispatcherTimer _autoSaveTimer;
        private bool _isInternalContentChange;
        private bool _isModified;
        private bool _isCreatingNote;

        // ── Events ───────────────────────────────────────────────────
        public event Action<int>? ScrollToLineRequested;
        public event Action<Heading>? HeadingNavigationRequested;
        public event Action<string>? MarkdownRenderRequested;
        public event Action<string>? ExportRequested;

        // ── Collections ──────────────────────────────────────────────
        public ObservableCollection<NoteDocument> Notes { get; } = new();
        public ObservableCollection<FolderNode> FolderNodes { get; } = new();
        public ObservableCollection<Heading> OutlineHeadings { get; } = new();
        public ObservableCollection<SearchResult> SearchResults { get; } = new();
        public ObservableCollection<NoteTabViewModel> OpenTabs { get; } = new();
        public ObservableCollection<TagItemViewModel> CurrentNoteTags { get; } = new();

        // ── Observable properties ────────────────────────────────────
        [ObservableProperty] private NoteDocument? _currentNote;
        [ObservableProperty] private NoteTabViewModel? _activeTab;
        [ObservableProperty] private string _noteTitle = string.Empty;
        [ObservableProperty] private string _editorContent = string.Empty;
        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private bool _isSearching;
        [ObservableProperty] private bool _isNotionMode = true;
        [ObservableProperty] private bool _isSplitMode = false;
        [ObservableProperty] private bool _isReadingMode = false;
        [ObservableProperty] private bool _isDarkMode = true;
        [ObservableProperty] private string _statusText = "Ready";
        [ObservableProperty] private string _wordCountText = "0 words";
        [ObservableProperty] private string _lineCountText = "0 lines";
        [ObservableProperty] private string _notesCountText = "0 notes";
        [ObservableProperty] private bool _isAddingTag;
        [ObservableProperty] private string _newTagText = string.Empty;
        [ObservableProperty] private FolderNode? _selectedFolderNode;
        [ObservableProperty] private string _cloudSyncStatusText = "OneDrive: Synced";
        [ObservableProperty] private string _cloudSyncTooltip = "OneDrive Sync is Active";
        [ObservableProperty] private bool _isCloudSyncEnabled = true;
        [ObservableProperty] private bool _isCloudSyncWarning = false;
        [ObservableProperty] private bool _isLeftSidebarVisible = true;
        [ObservableProperty] private bool _isRightSidebarVisible = true;
        [ObservableProperty] private bool _isRenameDialogVisible;
        [ObservableProperty] private string _renameDialogTitle = "Rename";
        [ObservableProperty] private string _renameDialogInput = string.Empty;
        [ObservableProperty] private bool _isMoveDialogVisible;
        [ObservableProperty] private string _moveDialogTitle = "Move to Folder";
        [ObservableProperty] private ObservableCollection<FolderDestinationItem> _availableFolders = new();
        [ObservableProperty] private FolderDestinationItem? _selectedDestinationFolder;
        [ObservableProperty] private FolderNode? _clipboardCutNode;
        [ObservableProperty] private bool _isProfileDialogVisible;
        [ObservableProperty] private ObservableCollection<ProfileItemViewModel> _availableProfiles = new();
        [ObservableProperty] private ProfileItemViewModel? _selectedProfile;
        [ObservableProperty] private bool _migrateNotesOnSwitch;
        [ObservableProperty] private int _profileDialogTabIndex;
        [ObservableProperty] private bool _isSpellCheckEnabled;
        [ObservableProperty] private string _newIgnoredWordInput = string.Empty;
        public ObservableCollection<string> IgnoredWords { get; } = new();

        // ── Multi-selection ──────────────────────────────────────────
        /// <summary>All currently selected nodes in the file tree (multi-select).</summary>
        public List<FolderNode> SelectedFolderNodes { get; set; } = new();

        // ── Dialog helper nodes ───────────────────────────────────────
        private FolderNode? _nodeToRename;
        private FolderNode? _nodeToMove;

        // ── Derived / computed ────────────────────────────────────────
        public string NotesRoot => _notesRoot;
        public bool HasClipboardCutNode => ClipboardCutNode != null;
        public bool HasCurrentNote => CurrentNote != null;
        public bool HasNoCurrentNote => CurrentNote == null;

        public bool IsNotionEditorActive => IsNotionMode && !IsRenameDialogVisible && !IsMoveDialogVisible && !IsProfileDialogVisible;

        partial void OnIsNotionModeChanged(bool value) => OnPropertyChanged(nameof(IsNotionEditorActive));
        partial void OnIsRenameDialogVisibleChanged(bool value) => OnPropertyChanged(nameof(IsNotionEditorActive));
        partial void OnIsMoveDialogVisibleChanged(bool value) => OnPropertyChanged(nameof(IsNotionEditorActive));
        partial void OnIsProfileDialogVisibleChanged(bool value) => OnPropertyChanged(nameof(IsNotionEditorActive));

        public string CurrentModeDisplayName
        {
            get
            {
                if (IsNotionMode) return "Visual Editor";
                if (IsSplitMode) return "Split View";
                if (IsReadingMode) return "Preview";
                return "Markdown Editor";
            }
        }

        // ── Sidebar toggles ───────────────────────────────────────────
        [RelayCommand] public void ToggleLeftSidebar()  => IsLeftSidebarVisible  = !IsLeftSidebarVisible;
        [RelayCommand] public void ToggleRightSidebar() => IsRightSidebarVisible = !IsRightSidebarVisible;

        // ── Spell Check & Custom Dictionary Commands ───────────────────
        [RelayCommand]
        public async Task AddIgnoredWord(string? word)
        {
            var target = string.IsNullOrWhiteSpace(word) ? NewIgnoredWordInput : word;
            if (string.IsNullOrWhiteSpace(target)) return;
            var trimmed = target.Trim();
            if (!IgnoredWords.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                IgnoredWords.Add(trimmed);
                _settingsService.Settings.IgnoredWords = IgnoredWords.ToList();
                await _settingsService.SaveSettingsAsync();
                NewIgnoredWordInput = string.Empty;
                StatusText = $"Added \"{trimmed}\" to custom dictionary";
            }
        }

        [RelayCommand]
        public async Task RemoveIgnoredWord(string? word)
        {
            if (string.IsNullOrWhiteSpace(word)) return;
            var existing = IgnoredWords.FirstOrDefault(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                IgnoredWords.Remove(existing);
                _settingsService.Settings.IgnoredWords = IgnoredWords.ToList();
                await _settingsService.SaveSettingsAsync();
                StatusText = $"Removed \"{existing}\" from custom dictionary";
            }
        }

        [RelayCommand]
        public async Task ToggleSpellCheck()
        {
            IsSpellCheckEnabled = !IsSpellCheckEnabled;
            _settingsService.Settings.IsSpellCheckEnabled = IsSpellCheckEnabled;
            await _settingsService.SaveSettingsAsync();
            StatusText = IsSpellCheckEnabled ? "Spell check enabled" : "Spell check disabled";
        }

        [RelayCommand] public void ShowProfileTab() => ProfileDialogTabIndex = 0;
        [RelayCommand] public void ShowDictionaryTab() => ProfileDialogTabIndex = 1;

        // ── Constructor ───────────────────────────────────────────────
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
            string notesRoot,
            IMarkdownParser? markdownParser = null)
        {
            _noteRepository   = noteRepository   ?? throw new ArgumentNullException(nameof(noteRepository));
            _searchEngine     = searchEngine     ?? throw new ArgumentNullException(nameof(searchEngine));
            _outlineService   = outlineService   ?? throw new ArgumentNullException(nameof(outlineService));
            _tagService       = tagService       ?? throw new ArgumentNullException(nameof(tagService));
            _folderService    = folderService    ?? throw new ArgumentNullException(nameof(folderService));
            _recoveryService  = recoveryService  ?? throw new ArgumentNullException(nameof(recoveryService));
            _trashBinService  = trashBinService  ?? throw new ArgumentNullException(nameof(trashBinService));
            _fileWatcher      = fileWatcher      ?? throw new ArgumentNullException(nameof(fileWatcher));
            _cloudSyncService = cloudSyncService ?? throw new ArgumentNullException(nameof(cloudSyncService));
            _settingsService  = settingsService  ?? throw new ArgumentNullException(nameof(settingsService));
            _notesRoot        = notesRoot        ?? throw new ArgumentNullException(nameof(notesRoot));
            _markdownParser   = markdownParser   ?? new MarkdownParser();

            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _autoSaveTimer.Tick += async (s, e) =>
            {
                _autoSaveTimer.Stop();
                await SaveNoteAsync();
            };

            _isSpellCheckEnabled = _settingsService.Settings.IsSpellCheckEnabled;
            if (_settingsService.Settings.IgnoredWords != null)
            {
                foreach (var w in _settingsService.Settings.IgnoredWords)
                {
                    if (!string.IsNullOrWhiteSpace(w) && !IgnoredWords.Contains(w.Trim(), StringComparer.OrdinalIgnoreCase))
                    {
                        IgnoredWords.Add(w.Trim());
                    }
                }
            }

            InitializeCloudSync();
            _ = InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            await LoadAllAsync();
            await CheckCrashRecoveryAsync();
        }
    }

    /// <summary>Represents a destination folder in the Move dialog picker.</summary>
    public class FolderDestinationItem
    {
        public string Name     { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
    }
}
