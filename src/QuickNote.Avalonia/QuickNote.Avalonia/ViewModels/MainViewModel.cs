using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Markdown;
using QuickNote.Core.Models;
using QuickNote.Core.Utilities;
using QuickNote.Avalonia.Themes;
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
        private readonly IBackupService _backupService;
        public IBackupService BackupService => _backupService;
        private readonly IFileWatcher _fileWatcher;
        private readonly ICloudSyncService _cloudSyncService;
        public ICloudSyncService CloudSyncService => _cloudSyncService;
        private readonly ISettingsService _settingsService;
        private readonly IMarkdownParser _markdownParser;
        private string _notesRoot;

        // ── Internal state ───────────────────────────────────────────
        private readonly DispatcherTimer _autoSaveTimer;
        private readonly DispatcherTimer _autoSyncTimer;
        private readonly DispatcherTimer _periodicSyncTimer;
        private bool _isAutoSyncRunning;
        private bool _isInternalContentChange;
        private bool _isModified;
        private bool _isCreatingNote;

        public bool IsModified => _isModified;

        // ── Events ───────────────────────────────────────────────────
        public event Action<int>? ScrollToLineRequested;
        public event Action<Heading>? HeadingNavigationRequested;
        public event Action<string>? MarkdownRenderRequested;
        public event Action<string>? ExportRequested;
        public event Action? FocusEditorRequested;

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
        [ObservableProperty] private bool _isGoogleLoginDialogVisible;
        [ObservableProperty] private bool _isTrashBinDialogVisible;
        [ObservableProperty] private bool _isBackupHistoryDialogVisible;
        [ObservableProperty] private ObservableCollection<ProfileItemViewModel> _availableProfiles = new();
        [ObservableProperty] private ProfileItemViewModel? _selectedProfile;
        [ObservableProperty] private bool _migrateNotesOnSwitch;
        [ObservableProperty] private int _profileDialogTabIndex;
        [ObservableProperty] private bool _isSpellCheckEnabled = true;
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
        public ITrashBinService TrashBinService => _trashBinService;
        public ISettingsService SettingsService => _settingsService;
        public bool HasClipboardCutNode => ClipboardCutNode != null;
        public bool HasCurrentNote => CurrentNote != null;
        public bool HasNoCurrentNote => CurrentNote == null;

        public bool HasCurrentNoteLastModified => CurrentNote != null && CurrentNote.LastModified != DateTime.MinValue;

        public string CurrentNoteLastModifiedText
        {
            get
            {
                if (CurrentNote == null || CurrentNote.LastModified == DateTime.MinValue)
                    return string.Empty;

                var local = CurrentNote.LastModified.ToLocalTime();
                return $"Last modified: {local:MMM d, yyyy 'at' h:mm tt}";
            }
        }

        public string CurrentNoteLastModifiedFullText
        {
            get
            {
                if (CurrentNote == null || CurrentNote.LastModified == DateTime.MinValue)
                    return string.Empty;

                var local = CurrentNote.LastModified.ToLocalTime();
                return $"Last modified: {local:dddd, MMMM d, yyyy 'at' h:mm:ss tt}\nClick to view version history & backups";
            }
        }

        public bool HasRightCurrentNoteLastModified => RightCurrentNote != null && RightCurrentNote.LastModified != DateTime.MinValue;

        public string RightCurrentNoteLastModifiedText
        {
            get
            {
                if (RightCurrentNote == null || RightCurrentNote.LastModified == DateTime.MinValue)
                    return string.Empty;

                var local = RightCurrentNote.LastModified.ToLocalTime();
                return $"Last modified: {local:MMM d, yyyy 'at' h:mm tt}";
            }
        }

        public string RightCurrentNoteLastModifiedFullText
        {
            get
            {
                if (RightCurrentNote == null || RightCurrentNote.LastModified == DateTime.MinValue)
                    return string.Empty;

                var local = RightCurrentNote.LastModified.ToLocalTime();
                return $"Last modified: {local:dddd, MMMM d, yyyy 'at' h:mm:ss tt}\nClick to view version history & backups";
            }
        }

        public bool IsNotionEditorActive => IsNotionMode && !IsRenameDialogVisible && !IsMoveDialogVisible && !IsProfileDialogVisible && !IsTrashBinDialogVisible && !IsGoogleLoginDialogVisible && !IsBackupHistoryDialogVisible;

        public bool IsThemeTabActive => ProfileDialogTabIndex == 0;
        public bool IsProfileTabActive => ProfileDialogTabIndex == 1;
        public bool IsDictionaryTabActive => ProfileDialogTabIndex == 2;
        public bool IsShortcutsTabActive => ProfileDialogTabIndex == 3;

        public event Action<bool>? ThemeChangedRequested;

        private static readonly StreamGeometry FallbackSunGeom = StreamGeometry.Parse("M12 7c-2.76 0-5 2.24-5 5s2.24 5 5 5 5-2.24 5-5-2.24-5-5-5zM2 13h2c.55 0 1-.45 1-1s-.45-1-1-1H2c-.55 0-1 .45-1 1s.45 1 1 1zm18 0h2c.55 0 1-.45 1-1s-.45-1-1-1h-2c-.55 0-1 .45-1 1s.45 1 1 1zM11 2v2c0 .55.45 1 1 1s1-.45 1-1V2c0-.55-.45-1-1-1s-1 .45-1 1zm0 18v2c0 .55.45 1 1 1s1-.45 1-1v-2c0-.55-.45-1-1-1s-1 .45-1 1zM5.99 4.58c-.39-.39-1.03-.39-1.41 0s-.39 1.03 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0s.39-1.03 0-1.41L5.99 4.58zm12.37 12.37c-.39-.39-1.03-.39-1.41 0s-.39 1.03 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0s.39-1.03 0-1.41l-1.06-1.06zm1.06-10.96c.39-.39.39-1.03 0-1.41s-1.03-.39-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06zM7.05 18.36c.39-.39.39-1.03 0-1.41s-1.03-.39-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06z");
        private static readonly StreamGeometry FallbackMoonGeom = StreamGeometry.Parse("M12.3 2a10 10 0 0 0-1.9 19.8 10 10 0 0 0 10.9-10.9A10.02 10.02 0 0 1 12.3 2z");

        public StreamGeometry ThemeIconGeometry => IsDarkMode ? FallbackSunGeom : FallbackMoonGeom;
        public string ThemeToggleText => IsDarkMode ? "Light Mode" : "Dark Mode";

        public ThemeManager ThemeManager => ThemeManager.Instance;

        partial void OnIsDarkModeChanged(bool value)
        {
            if (value != ThemeManager.Instance.IsCurrentThemeDark)
            {
                ThemeManager.Instance.ToggleDarkLightMode();
            }
        }

        public void ApplyTheme(bool isDark)
        {
            if (isDark != ThemeManager.Instance.IsCurrentThemeDark)
            {
                ThemeManager.Instance.ToggleDarkLightMode();
            }
        }

        [RelayCommand]
        public void SelectTheme(ThemeSchema theme)
        {
            if (theme != null)
            {
                ThemeManager.Instance.ApplyTheme(theme);
            }
        }

        private void OnThemeManagerChanged(ThemeSchema theme)
        {
            var isDark = theme.BaseVariant.Equals("Dark", StringComparison.OrdinalIgnoreCase);
            if (IsDarkMode != isDark)
            {
                IsDarkMode = isDark;
            }
            OnPropertyChanged(nameof(ThemeIconGeometry));
            OnPropertyChanged(nameof(ThemeToggleText));
            ThemeChangedRequested?.Invoke(isDark);
        }

        [RelayCommand]
        public void OpenTrashBin()
        {
            IsTrashBinDialogVisible = true;
        }

        [ObservableProperty] private string? _backupHistoryPreferredTitle;

        [RelayCommand]
        public void OpenBackupHistory()
        {
            BackupHistoryPreferredTitle = CurrentNote?.Title;
            IsBackupHistoryDialogVisible = true;
        }

        [RelayCommand]
        public void OpenRightBackupHistory()
        {
            BackupHistoryPreferredTitle = RightCurrentNote?.Title ?? CurrentNote?.Title;
            IsBackupHistoryDialogVisible = true;
        }

        partial void OnIsNotionModeChanged(bool value) => OnPropertyChanged(nameof(IsNotionEditorActive));
        partial void OnIsRenameDialogVisibleChanged(bool value) => OnPropertyChanged(nameof(IsNotionEditorActive));
        partial void OnIsMoveDialogVisibleChanged(bool value) => OnPropertyChanged(nameof(IsNotionEditorActive));
        partial void OnIsProfileDialogVisibleChanged(bool value) => OnPropertyChanged(nameof(IsNotionEditorActive));
        partial void OnIsGoogleLoginDialogVisibleChanged(bool value) => OnPropertyChanged(nameof(IsNotionEditorActive));
        partial void OnIsTrashBinDialogVisibleChanged(bool value) => OnPropertyChanged(nameof(IsNotionEditorActive));
        partial void OnIsBackupHistoryDialogVisibleChanged(bool value) => OnPropertyChanged(nameof(IsNotionEditorActive));
        partial void OnProfileDialogTabIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsThemeTabActive));
            OnPropertyChanged(nameof(IsProfileTabActive));
            OnPropertyChanged(nameof(IsDictionaryTabActive));
            OnPropertyChanged(nameof(IsShortcutsTabActive));
        }

        public string CurrentModeDisplayName
        {
            get
            {
                if (IsNotionMode)
                {
                    return IsSplitViewActive ? "Visual Editor (Split)" : "Visual Editor";
                }
                if (IsReadingMode) return "Reading View";
                return "Normal Editor";
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
            var trimmed = target.Trim().Trim('"', '\'', '-', '.', ',', '!', '?');
            if (string.IsNullOrWhiteSpace(trimmed)) return;

            if (!IgnoredWords.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                IgnoredWords.Add(trimmed);
                _settingsService.Settings.IgnoredWords = IgnoredWords.ToList();
                await _settingsService.SaveSettingsAsync();
                StatusText = $"Added \"{trimmed}\" to custom dictionary";
            }
            NewIgnoredWordInput = string.Empty;
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

        [RelayCommand] public void ShowThemeTab() => ProfileDialogTabIndex = 0;
        [RelayCommand] public void ShowProfileTab() => ProfileDialogTabIndex = 1;
        [RelayCommand] public void ShowDictionaryTab() => ProfileDialogTabIndex = 2;
        [RelayCommand] public void ShowShortcutsTab() => ProfileDialogTabIndex = 3;
        [RelayCommand] public void OpenSettings()
        {
            ShowProfileDialog();
            ProfileDialogTabIndex = 0;
        }
        [RelayCommand] public void OpenGoogleDriveLogin() => IsGoogleLoginDialogVisible = true;
        [RelayCommand] public void CloudStatusClick()
        {
            if (_settingsService.Settings.IsGoogleDriveSyncEnabled && !_cloudSyncService.IsAuthenticated)
            {
                OpenGoogleDriveLogin();
            }
            else
            {
                ShowProfileDialog();
            }
        }

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
            IMarkdownParser? markdownParser = null,
            IBackupService? backupService = null)
        {
            _noteRepository   = noteRepository   ?? throw new ArgumentNullException(nameof(noteRepository));
            _searchEngine     = searchEngine     ?? throw new ArgumentNullException(nameof(searchEngine));
            _outlineService   = outlineService   ?? throw new ArgumentNullException(nameof(outlineService));
            _tagService       = tagService       ?? throw new ArgumentNullException(nameof(tagService));
            _folderService    = folderService    ?? throw new ArgumentNullException(nameof(folderService));
            _recoveryService  = recoveryService  ?? throw new ArgumentNullException(nameof(recoveryService));
            _trashBinService  = trashBinService  ?? throw new ArgumentNullException(nameof(trashBinService));
            _backupService    = backupService    ?? new QuickNote.Infrastructure.Services.BackupService(notesRoot);
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
                if (IsSplitViewActive && _isRightModified && RightCurrentNote != null)
                {
                    await SaveRightNoteAsync();
                }
            };

            _autoSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _autoSyncTimer.Tick += async (s, e) =>
            {
                _autoSyncTimer.Stop();
                await ExecuteAutoSyncAsync();
            };

            _periodicSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _periodicSyncTimer.Tick += async (s, e) =>
            {
                if (_cloudSyncService.IsAuthenticated && _settingsService.Settings.IsGoogleDriveSyncEnabled)
                {
                    await ExecuteAutoSyncAsync();
                }
            };
            _periodicSyncTimer.Start();

            _isSpellCheckEnabled = _settingsService.Settings.IsSpellCheckEnabled;
            _isDarkMode = ThemeManager.Instance.IsCurrentThemeDark;
            ThemeManager.Instance.ThemeChanged += OnThemeManagerChanged;
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

        private DateTime _lastUserActivityUtc = DateTime.UtcNow;

        /// <summary>
        /// Records user interaction so background cloud sync waits for 1 full minute of inactivity.
        /// Real-time sync to local JSON occurs immediately on every change.
        /// </summary>
        public void NotifyUserActivity()
        {
            _lastUserActivityUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Requests a debounced auto-sync with Google Drive / cloud storage.
        /// Defaults to 60,000ms (1 minute) of user inactivity before syncing with the cloud.
        /// </summary>
        public void RequestAutoSync(int debounceMs = 60000)
        {
            if (_cloudSyncService == null || !_cloudSyncService.IsAuthenticated) return;
            if (!_settingsService.Settings.IsGoogleDriveSyncEnabled) return;

            Dispatcher.UIThread.Post(() =>
            {
                _autoSyncTimer.Stop();
                _autoSyncTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(5000, debounceMs));
                _autoSyncTimer.Start();
            });
        }

        /// <summary>
        /// Executes synchronization in the background after verifying the user has had no activity
        /// for at least 1 minute (60 seconds). If remote changes were downloaded,
        /// refreshes the note list and folder tree so changes appear immediately.
        /// </summary>
        public async Task ExecuteAutoSyncAsync()
        {
            if (_cloudSyncService == null || !_cloudSyncService.IsAuthenticated) return;
            if (!_settingsService.Settings.IsGoogleDriveSyncEnabled) return;

            // Enforce requirement: double-check user has had NO ACTIVITY for 1 minute before syncing to cloud
            var idleDuration = DateTime.UtcNow - _lastUserActivityUtc;
            if (idleDuration < TimeSpan.FromSeconds(55))
            {
                // User was active within the last 1 minute; postpone cloud sync until 60 seconds of idle
                int remainingMs = (int)Math.Max(5000, (TimeSpan.FromSeconds(60) - idleDuration).TotalMilliseconds);
                RequestAutoSync(remainingMs);
                return;
            }

            if (_isAutoSyncRunning)
            {
                RequestAutoSync(10000);
                return;
            }

            _isAutoSyncRunning = true;
            try
            {
                await _noteRepository.FlushPendingWritesAsync();
                var result = await _cloudSyncService.TriggerSyncAsync();
                if (result != null && result.DownloadedCount > 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var curId = CurrentNote?.Id;
                        var curPath = CurrentNote?.FullPath;
                        await RefreshFolderTreeAsync();
                        var loaded = await _noteRepository.GetAllNotesAsync();
                        var loadedList = loaded.OrderByDescending(n => n.LastModified).ToList();
                        var loadedKeys = new HashSet<string>(loadedList.Select(n => n.FullPath), StringComparer.OrdinalIgnoreCase);

                        // In-place collection synchronization to prevent UI thread flooding & re-entrancy crashes
                        for (int i = Notes.Count - 1; i >= 0; i--)
                        {
                            if (!loadedKeys.Contains(Notes[i].FullPath))
                            {
                                Notes.RemoveAt(i);
                            }
                        }

                        for (int i = 0; i < loadedList.Count; i++)
                        {
                            var note = loadedList[i];
                            int existingIdx = -1;
                            for (int j = 0; j < Notes.Count; j++)
                            {
                                if (string.Equals(Notes[j].FullPath, note.FullPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    existingIdx = j;
                                    break;
                                }
                            }

                            if (existingIdx >= 0)
                            {
                                var existing = Notes[existingIdx];
                                if (existing.LastModified != note.LastModified || existing.Content != note.Content || existing.Title != note.Title)
                                {
                                    Notes[existingIdx] = note;
                                }
                                if (existingIdx != i && i < Notes.Count)
                                {
                                    Notes.Move(existingIdx, i);
                                }
                            }
                            else
                            {
                                if (i <= Notes.Count)
                                    Notes.Insert(i, note);
                                else
                                    Notes.Add(note);
                            }
                        }

                        _searchEngine.Clear();
                        foreach (var note in Notes)
                        {
                            _searchEngine.IndexDocument(note);
                        }
                        NotesCountText = $"{Notes.Count} notes";

                        if (curId.HasValue && !string.IsNullOrEmpty(curPath) && File.Exists(curPath))
                        {
                            var updatedNote = Notes.FirstOrDefault(n => n.Id == curId.Value || string.Equals(n.FullPath, curPath, StringComparison.OrdinalIgnoreCase));
                            if (updatedNote != null && CurrentNote != null && !_isModified)
                            {
                                CurrentNote = updatedNote;
                                var (_, body) = SeparateTitleAndBody(updatedNote.Content, updatedNote.Title);
                                EditorContent = body;
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExecuteAutoSyncAsync] Error: {ex.Message}");
            }
            finally
            {
                _isAutoSyncRunning = false;
            }
        }
    }

    /// <summary>Represents a destination folder in the Move dialog picker.</summary>
    public class FolderDestinationItem
    {
        public string Name     { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
    }
}
