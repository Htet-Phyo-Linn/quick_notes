using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using QuickNote.Avalonia.Themes;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Infrastructure.Cloud;

namespace QuickNote.Avalonia.Views
{
    public record KeepColor(string Name, string Background, string Border, string Accent, string Text)
    {
        public IBrush BackgroundBrush { get; } = new SolidColorBrush(Color.Parse(Background));
        public IBrush BorderBrush { get; } = new SolidColorBrush(Color.Parse(Border));
        public IBrush AccentBrush { get; } = new SolidColorBrush(Color.Parse(Accent));
        public IBrush TextBrush { get; } = new SolidColorBrush(Color.Parse(Text));
    }

    public static class KeepPalette
    {
        public static readonly KeepColor DarkDefault = new KeepColor("Default", "#161c26", "#283548", "#3390ec", "#f8fafc");
        public static readonly KeepColor LightDefault = new KeepColor("Default", "#ffffff", "#e2e8f0", "#2481cc", "#0f172a");

        public static readonly KeepColor DarkWindowsDefault = new KeepColor("Default", "#161b26", "#283244", "#2563eb", "#f8fafc");
        public static readonly KeepColor LightWindowsDefault = new KeepColor("Default", "#ffffff", "#cbd5e1", "#2563eb", "#0f172a");

        public static readonly KeepColor[] Colors = new[]
        {
            DarkDefault,
            new KeepColor("Coral",   "#5c2b29", "#773330", "#f28b82", "#fce8e6"),
            new KeepColor("Peach",   "#614a19", "#805f1f", "#fbbc04", "#fef7e0"),
            new KeepColor("Sand",    "#635d19", "#80771f", "#fff475", "#fefde8"),
            new KeepColor("Mint",    "#345920", "#447728", "#ccff90", "#f4fde8"),
            new KeepColor("Sage",    "#16504b", "#1f6d66", "#a7ffeb", "#e6fcf8"),
            new KeepColor("Fog",     "#2d555e", "#3c7380", "#cbf0f8", "#e8f8fc"),
            new KeepColor("Storm",   "#1e3a5f", "#2a4e80", "#aecbfa", "#e8f0fe"),
            new KeepColor("Dusk",    "#42275e", "#5a3580", "#d7aefb", "#f3e8fd"),
            new KeepColor("Blossom", "#5b2245", "#7a2e5d", "#fdcfe8", "#fce8f3"),
            new KeepColor("Clay",    "#442f19", "#5e4022", "#e6c9a8", "#fbf5ee"),
            new KeepColor("Chalk",   "#3c3f41", "#505357", "#e8eaed", "#f1f3f4"),
        };

        public static KeepColor GetColor(string? nameOrId, bool isDarkMode = true, bool isWindowsTheme = false)
        {
            if (!string.IsNullOrWhiteSpace(nameOrId))
            {
                var match = Colors.FirstOrDefault(c => string.Equals(c.Name, nameOrId, StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(c.Background, nameOrId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    if (string.Equals(match.Name, "Default", StringComparison.OrdinalIgnoreCase))
                    {
                        return GetDefaultColor(isDarkMode, isWindowsTheme);
                    }
                    return match;
                }
            }

            return GetDefaultColor(isDarkMode, isWindowsTheme);
        }

        public static KeepColor GetDefaultColor(bool isDarkMode = true, bool isWindowsTheme = false)
        {
            var cur = ThemeManager.Instance.CurrentTheme;
            if (cur != null)
            {
                return new KeepColor(
                    "Default",
                    cur.GetColor("AppCardBackground", isDarkMode ? "#161c26" : "#ffffff"),
                    cur.GetColor("AppBorder", isDarkMode ? "#283548" : "#cbd5e1"),
                    cur.AccentColor,
                    cur.GetColor("AppTextPrimary", isDarkMode ? "#f8fafc" : "#0f172a")
                );
            }
            return isWindowsTheme ? (isDarkMode ? DarkWindowsDefault : LightWindowsDefault) : (isDarkMode ? DarkDefault : LightDefault);
        }
    }

    public class MobileNoteItem : INotifyPropertyChanged
    {
        private static readonly IBrush SelectedBorderBrush = new SolidColorBrush(Color.Parse("#3390ec"));
        private static readonly IBrush SelectedCheckBgBrush = new SolidColorBrush(Color.Parse("#3390ec"));
        private static readonly IBrush SelectedBorderBrushWin = new SolidColorBrush(Color.Parse("#2563eb"));
        private static readonly IBrush SelectedCheckBgBrushWin = new SolidColorBrush(Color.Parse("#2563eb"));
        private static readonly IBrush SelectedCheckBorderBrushConst = new SolidColorBrush(Color.Parse("#3390ec"));
        private static readonly IBrush UnselectedCheckBorderBrushDark = new SolidColorBrush(Color.Parse("#4b5563"));
        private static readonly IBrush UnselectedCheckBorderBrushLight = new SolidColorBrush(Color.Parse("#cbd5e1"));
        private static readonly Thickness SelectedBorderThicknessValue = new Thickness(2);
        private static readonly Thickness NormalBorderThicknessValue = new Thickness(1);

        private static readonly IBrush DarkSnippetBrush = new SolidColorBrush(Color.Parse("#8c97a8"));
        private static readonly IBrush LightSnippetBrush = new SolidColorBrush(Color.Parse("#475569"));
        private static readonly IBrush DarkDateBrush = new SolidColorBrush(Color.Parse("#64748b"));
        private static readonly IBrush LightDateBrush = new SolidColorBrush(Color.Parse("#64748b"));
        private static readonly IBrush DarkFolderBadgeBg = new SolidColorBrush(Color.FromArgb(40, 51, 144, 236));
        private static readonly IBrush LightFolderBadgeBg = new SolidColorBrush(Color.FromArgb(28, 36, 129, 204));
        private static readonly IBrush DarkFolderBadgeFg = new SolidColorBrush(Color.Parse("#60a5fa"));
        private static readonly IBrush LightFolderBadgeFg = new SolidColorBrush(Color.Parse("#2563eb"));
        private static readonly IBrush DarkTagBadgeBg = new SolidColorBrush(Color.Parse("#1e2533"));
        private static readonly IBrush LightTagBadgeBg = new SolidColorBrush(Color.Parse("#f1f5f9"));
        private static readonly IBrush DarkTagBadgeFg = new SolidColorBrush(Color.Parse("#94a3b8"));
        private static readonly IBrush LightTagBadgeFg = new SolidColorBrush(Color.Parse("#475569"));

        public NoteDocument Note { get; private set; }
        private KeepColor _color;
        private bool _isDarkMode = true;
        private bool _isWindowsTheme = false;

        public MobileNoteItem(NoteDocument note, bool isDarkMode = true, bool isWindowsTheme = false)
        {
            Note = note;
            _isDarkMode = isDarkMode;
            _isWindowsTheme = isWindowsTheme;
            _color = ResolveColor(note, isDarkMode, isWindowsTheme);
            ApplyBrushes();
            PrecomputeFields();
        }

        public void UpdateNote(NoteDocument newNote)
        {
            Note = newNote;
            _color = ResolveColor(newNote, _isDarkMode, _isWindowsTheme);
            ApplyBrushes();
            Refresh();
        }

        private void ApplyBrushes()
        {
            CardBackground = _color.BackgroundBrush;
            CardBorderBrush = _isWindowsTheme
                ? (_isDarkMode ? new SolidColorBrush(Color.Parse("#283244")) : new SolidColorBrush(Color.Parse("#cbd5e1")))
                : _color.BorderBrush;
            CardAccentBrush = _color.AccentBrush;
            CardTextBrush = _color.TextBrush;
        }

        public void UpdateTheme(bool isDarkMode, bool isWindowsTheme = false)
        {
            _isDarkMode = isDarkMode;
            _isWindowsTheme = isWindowsTheme;
            _color = ResolveColor(Note, isDarkMode, isWindowsTheme);
            ApplyBrushes();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardBackground)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardBorderBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardAccentBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardTextBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveBorderBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveBorderThickness)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnippetBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FolderBadgeBg)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FolderBadgeFg)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TagBadgeBg)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TagBadgeFg)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionCheckBackground)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionCheckBorderBrush)));
        }

        private static KeepColor ResolveColor(NoteDocument note, bool isDarkMode, bool isWindowsTheme = false)
        {
            if (!string.IsNullOrWhiteSpace(note.Content) && note.Content.StartsWith("---"))
            {
                var end = note.Content.IndexOf("---", 3);
                if (end > 3)
                {
                    var header = note.Content.Substring(3, end - 3);
                    foreach (var line in header.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split(new[] { ':' }, 2);
                        if (parts.Length == 2 && parts[0].Trim().Equals("color", StringComparison.OrdinalIgnoreCase))
                        {
                            return KeepPalette.GetColor(parts[1].Trim(), isDarkMode, isWindowsTheme);
                        }
                    }
                }
            }

            return KeepPalette.GetDefaultColor(isDarkMode, isWindowsTheme);
        }

        public IBrush CardBackground { get; private set; } = null!;
        public IBrush CardBorderBrush { get; private set; } = null!;
        public IBrush CardAccentBrush { get; private set; } = null!;
        public IBrush CardTextBrush { get; private set; } = null!;
        public string ColorName => _color.Name;

        public IBrush SnippetBrush => _isDarkMode ? DarkSnippetBrush : LightSnippetBrush;
        public IBrush DateBrush => _isDarkMode ? DarkDateBrush : LightDateBrush;
        public IBrush FolderBadgeBg => _isDarkMode ? DarkFolderBadgeBg : LightFolderBadgeBg;
        public IBrush FolderBadgeFg => _isDarkMode ? DarkFolderBadgeFg : LightFolderBadgeFg;
        public IBrush TagBadgeBg => _isDarkMode ? DarkTagBadgeBg : LightTagBadgeBg;
        public IBrush TagBadgeFg => _isDarkMode ? DarkTagBadgeFg : LightTagBadgeFg;

        private string _title = string.Empty;
        private string _snippet = string.Empty;
        private string _folderName = string.Empty;
        private string _formattedDate = string.Empty;
        private string[] _tags = Array.Empty<string>();
        private bool _hasTags = false;

        public void PrecomputeFields()
        {
            _title = !string.IsNullOrWhiteSpace(Note?.Title)
                ? Note.Title
                : (Path.GetFileNameWithoutExtension(Note?.FileName) ?? "Untitled Note");

            _folderName = ComputeFolderName(Note);
            var render = !string.IsNullOrWhiteSpace(Note?.RenderText) && Note.RenderText != "Empty note"
                ? Note.RenderText
                : (!string.IsNullOrWhiteSpace(Note?.Snippet) && Note.Snippet != "Empty note" ? Note.Snippet : null);
            _snippet = render ?? ComputeSnippet(Note);
            _formattedDate = Note != null && Note.LastModified != DateTime.MinValue
                ? Note.LastModified.ToLocalTime().ToString("MMM d")
                : "Just now";

            _tags = Note?.Tags ?? Array.Empty<string>();
            _hasTags = _tags != null && _tags.Length > 0;
        }

        private static string ComputeFolderName(NoteDocument? note)
        {
            if (string.IsNullOrWhiteSpace(note?.FolderPath)) return "Notes";
            var name = Path.GetFileName(note.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? "Notes" : name;
        }

        private static string ComputeSnippet(NoteDocument? note)
        {
            if (note == null || string.IsNullOrWhiteSpace(note.Content)) return "Empty note";
            return QuickNote.Core.Markdown.MarkdownParser.ComputeRenderText(note.Content, note.Title);
        }

        public string Title => _title;
        public string Snippet => _snippet;
        public string FolderName => _folderName;
        public bool HasFolderName => !string.IsNullOrWhiteSpace(_folderName) && _folderName != "Notes";
        public string FormattedDate => _formattedDate;
        public string[] Tags => _tags;
        public bool HasTags => _hasTags;

        private bool _isSelectionMode = false;
        public bool IsSelectionMode
        {
            get => _isSelectionMode;
            set
            {
                if (_isSelectionMode != value)
                {
                    _isSelectionMode = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectionMode)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveBorderBrush)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveBorderThickness)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionCheckBackground)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionCheckBorderBrush)));
                }
            }
        }

        private bool _isSelected = false;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveBorderBrush)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveBorderThickness)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionCheckBackground)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionCheckBorderBrush)));
                }
            }
        }

        public IBrush EffectiveBorderBrush
        {
            get
            {
                if (IsSelected) return _isWindowsTheme ? SelectedBorderBrushWin : SelectedBorderBrush;
                return CardBorderBrush;
            }
        }

        public Thickness EffectiveBorderThickness => IsSelected
            ? SelectedBorderThicknessValue
            : NormalBorderThicknessValue;

        public IBrush SelectionCheckBackground => IsSelected
            ? (_isWindowsTheme ? SelectedCheckBgBrushWin : SelectedCheckBgBrush)
            : Brushes.Transparent;

        public IBrush SelectionCheckBorderBrush => IsSelected
            ? SelectedCheckBorderBrushConst
            : (_isDarkMode ? UnselectedCheckBorderBrushDark : UnselectedCheckBorderBrushLight);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Refresh()
        {
            PrecomputeFields();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    public partial class MobileMainView : UserControl, INotifyPropertyChanged
    {
        private MainViewModel? _viewModel;
        private string _selectedCategory = "All Notes";
        private string? _selectedTag = null;
        private string _searchQuery = string.Empty;
        private string _activeNoteTitle = string.Empty;
        private bool _isUpdatingTitleFromViewModel;
        private bool _isGridView = true;
        private string? _pendingDeleteFolderName = null;

        private static readonly ObservableCollection<FolderNode> _emptyFolderNodes = new();
        public ObservableCollection<FolderNode> FolderNodes => _viewModel?.FolderNodes ?? _emptyFolderNodes;

        public ObservableCollection<MobileNoteItem> FilteredNotes { get; } = new();

        public bool HasActiveFilter => HasSearchQuery || HasSelectedTag;
        public bool IsGridFeedVisible => IsGridView;
        public bool IsFolderTreeVisible => !IsGridView && !HasActiveFilter;
        public bool IsFlatFileListVisible => !IsGridView && HasActiveFilter;

        public bool IsGridView
        {
            get => _isGridView;
            set
            {
                if (_isGridView != value)
                {
                    _isGridView = value;
                    if (!_isGridView)
                    {
                        CollapseAllFolders();
                    }
                    OnPropertyChanged(nameof(IsGridView));
                    OnPropertyChanged(nameof(GridColumns));
                    OnPropertyChanged(nameof(ViewModeIcon));
                    OnPropertyChanged(nameof(IsGridFeedVisible));
                    OnPropertyChanged(nameof(IsFolderTreeVisible));
                    OnPropertyChanged(nameof(IsFlatFileListVisible));
                    OnPropertyChanged(nameof(IsNotesEmpty));
                }
            }
        }

        public void CollapseAllFolders()
        {
            if (_viewModel?.FolderNodes != null)
            {
                foreach (var node in _viewModel.FolderNodes)
                {
                    CollapseFolderRecursive(node);
                }
            }
        }

        private static void CollapseFolderRecursive(FolderNode node)
        {
            if (node.IsDirectory)
            {
                node.IsExpanded = false;
                foreach (var child in node.Children)
                {
                    CollapseFolderRecursive(child);
                }
            }
        }

        public int GridColumns => _isGridView ? 2 : 1;

        public StreamGeometry ViewModeIcon => (Resources.TryGetResource(_isGridView ? "IconListView" : "IconGridView", null, out var res) && res is StreamGeometry geom) ? geom : new StreamGeometry();

        private DispatcherTimer? _searchDebounceTimer;

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged(nameof(SearchQuery));
                    OnPropertyChanged(nameof(HasSearchQuery));

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        _searchDebounceTimer?.Stop();
                        _searchDebounceTimer = null;
                        ApplyFilters();
                    }
                    else
                    {
                        _searchDebounceTimer?.Stop();
                        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                        _searchDebounceTimer.Tick += (s, e) =>
                        {
                            _searchDebounceTimer?.Stop();
                            _searchDebounceTimer = null;
                            ApplyFilters();
                        };
                        _searchDebounceTimer.Start();
                    }
                }
            }
        }

        public bool HasSearchQuery => !string.IsNullOrWhiteSpace(_searchQuery);

        public string ActiveNoteTitle
        {
            get => _activeNoteTitle;
            set
            {
                if (_activeNoteTitle != value)
                {
                    _activeNoteTitle = value;
                    if (_isUpdatingTitleFromViewModel)
                    {
                        OnPropertyChanged(nameof(ActiveNoteTitle));
                    }
                    if (_viewModel != null && _viewModel.NoteTitle != value)
                    {
                        _viewModel.NoteTitle = value;
                    }
                }
            }
        }

        public string CloudSyncStatusText => _viewModel?.CloudSyncStatusText ?? "Google Drive: Connect";
        public string ConnectedAccountEmail => _viewModel?.CloudSyncService?.UserEmail ?? "Not connected";
        public string LastSyncTimeText => _viewModel?.CloudSyncService?.LastSyncTime?.ToLocalTime().ToString("MMM d, h:mm tt") ?? "Never";
        public bool IsSignedInToGoogle => _viewModel?.CloudSyncService?.IsAuthenticated == true;
        public IBrush CloudSyncIconBrush => IsSignedInToGoogle
            ? new SolidColorBrush(Color.Parse("#10b981"))
            : new SolidColorBrush(Color.Parse("#9ca3af"));

        public string NoteCountStatus => $"{FilteredNotes.Count} note{(FilteredNotes.Count == 1 ? "" : "s")}";
        public string TotalNotesCountText => _viewModel?.Notes?.Count.ToString() ?? "0";

        public bool HasCurrentNoteLastModified => _viewModel?.HasCurrentNoteLastModified ?? false;
        public string CurrentNoteLastModifiedText => _viewModel?.CurrentNoteLastModifiedText ?? string.Empty;
        public string CurrentNoteLastModifiedFullText => _viewModel?.CurrentNoteLastModifiedFullText ?? string.Empty;
        public bool IsNotesEmpty
        {
            get
            {
                if (HasActiveFilter) return FilteredNotes.Count == 0;
                if (IsGridView) return FilteredNotes.Count == 0;
                return FolderNodes.Count == 0;
            }
        }

        public string? SelectedTag
        {
            get => _selectedTag;
            set
            {
                if (_selectedTag != value)
                {
                    _selectedTag = value;
                    OnPropertyChanged(nameof(SelectedTag));
                    OnPropertyChanged(nameof(HasSelectedTag));
                    OnPropertyChanged(nameof(SelectedTagText));
                    ApplyFilters();
                }
            }
        }

        public bool HasSelectedTag => !string.IsNullOrEmpty(_selectedTag);
        public string SelectedTagText => string.IsNullOrEmpty(_selectedTag) ? "" : $"#{_selectedTag}";

        // ── Dynamic Theme & Styling Context ───────────────────────────────
        private bool _isWindowsTheme = false;
        private bool _isDarkMode = true;

        public bool IsWindowsTheme => _isWindowsTheme;
        public bool IsAndroidThemeActive => !_isWindowsTheme;
        public bool IsWindowsThemeActive => _isWindowsTheme;

        public IBrush AndroidCardBorderBrush => !_isWindowsTheme ? AccentBrush : GlassBorderBrush;
        public Thickness AndroidCardBorderThickness => !_isWindowsTheme ? new Thickness(2) : new Thickness(1);

        public IBrush WindowsCardBorderBrush => IsWindowsTheme ? AccentBrush : GlassBorderBrush;
        public Thickness WindowsCardBorderThickness => IsWindowsTheme ? new Thickness(2) : new Thickness(1);

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode != value)
                {
                    _isDarkMode = value;
                    ThemeManager.Instance.ToggleDarkLightMode();
                }
            }
        }

        private void ApplyThemeVariantUpdates()
        {
            _isDarkMode = ThemeManager.Instance.IsCurrentThemeDark;
            _isWindowsTheme = ThemeManager.Instance.CurrentTheme?.Id.StartsWith("windows", StringComparison.OrdinalIgnoreCase) ?? false;

            OnPropertyChanged(nameof(IsDarkMode));
            OnPropertyChanged(nameof(IsWindowsTheme));
            OnPropertyChanged(nameof(IsAndroidThemeActive));
            OnPropertyChanged(nameof(IsWindowsThemeActive));
            OnPropertyChanged(nameof(AndroidCardBorderBrush));
            OnPropertyChanged(nameof(AndroidCardBorderThickness));
            OnPropertyChanged(nameof(WindowsCardBorderBrush));
            OnPropertyChanged(nameof(WindowsCardBorderThickness));
            OnPropertyChanged(nameof(ThemeIconGeometry));
            OnPropertyChanged(nameof(ThemeToggleText));
            OnPropertyChanged(nameof(AppBackgroundBrush));
            OnPropertyChanged(nameof(GlassSurfaceBrush));
            OnPropertyChanged(nameof(GlassHeaderBackgroundBrush));
            OnPropertyChanged(nameof(GlassBorderBrush));
            OnPropertyChanged(nameof(TextPrimaryBrush));
            OnPropertyChanged(nameof(TextSecondaryBrush));
            OnPropertyChanged(nameof(TextMutedBrush));
            OnPropertyChanged(nameof(AccentBrush));
            OnPropertyChanged(nameof(DrawerBackgroundBrush));
            OnPropertyChanged(nameof(ModalBackgroundBrush));
            OnPropertyChanged(nameof(ChipSelectedBackgroundBrush));
            OnPropertyChanged(nameof(ChipUnselectedBackgroundBrush));
            OnPropertyChanged(nameof(ChipUnselectedTextBrush));
            OnPropertyChanged(nameof(FloatingBarBackgroundBrush));
            OnPropertyChanged(nameof(SelectionBarBackgroundBrush));
            OnPropertyChanged(nameof(FabBackgroundBrush));
            OnPropertyChanged(nameof(EditorToolbarBackgroundBrush));
            OnPropertyChanged(nameof(ThemeTabBackgroundBrush));
            OnPropertyChanged(nameof(ThemeTabForegroundBrush));
            OnPropertyChanged(nameof(DictionaryTabBackgroundBrush));
            OnPropertyChanged(nameof(DictionaryTabForegroundBrush));

            foreach (var note in FilteredNotes)
            {
                note.UpdateTheme(_isDarkMode, _isWindowsTheme);
            }

            if (MobileNotionEditor != null && ThemeManager.Instance.CurrentTheme != null)
            {
                _ = MobileNotionEditor.SetThemeSchemaAsync(ThemeManager.Instance.CurrentTheme);
            }

            RebuildTagChips();
            RebuildIgnoredWordsChips();
        }

        public IBrush AppBackgroundBrush => ThemeManager.Instance.GetBrush("AppBackground");
        public IBrush GlassSurfaceBrush => ThemeManager.Instance.GetBrush("AppCardBackground");
        public IBrush CardBackgroundBrush => GlassSurfaceBrush;
        public IBrush GlassHeaderBackgroundBrush => ThemeManager.Instance.GetBrush("AppHeaderBackground");
        public IBrush GlassBorderBrush => ThemeManager.Instance.GetBrush("AppBorder");
        public IBrush TextPrimaryBrush => ThemeManager.Instance.GetBrush("AppTextPrimary");
        public IBrush TextSecondaryBrush => ThemeManager.Instance.GetBrush("AppTextSecondary");
        public IBrush TextMutedBrush => ThemeManager.Instance.GetBrush("AppTextSecondary");
        public IBrush AccentBrush => ThemeManager.Instance.GetBrush("AppAccent", "#3390ec");
        public IBrush DrawerBackgroundBrush => ThemeManager.Instance.GetBrush("AppBackground");
        public IBrush ModalBackgroundBrush => ThemeManager.Instance.GetBrush("AppCardBackground");
        public IBrush ChipSelectedBackgroundBrush => AccentBrush;
        public IBrush ChipUnselectedBackgroundBrush => ThemeManager.Instance.GetBrush("AppSelectedBackground");
        public IBrush ChipUnselectedTextBrush => ThemeManager.Instance.GetBrush("AppTextSecondary");
        public IBrush FloatingBarBackgroundBrush => ThemeManager.Instance.GetBrush("AppHeaderBackground");
        public IBrush SelectionBarBackgroundBrush => ThemeManager.Instance.GetBrush("AppSelectedBackground");
        public IBrush FabBackgroundBrush => AccentBrush;
        public IBrush EditorToolbarBackgroundBrush => ThemeManager.Instance.GetBrush("AppHeaderBackground");

        private static readonly StreamGeometry FallbackSunGeom = StreamGeometry.Parse("M12 7c-2.76 0-5 2.24-5 5s2.24 5 5 5 5-2.24 5-5-2.24-5-5-5zM2 13h2c.55 0 1-.45 1-1s-.45-1-1-1H2c-.55 0-1 .45-1 1s.45 1 1 1zm18 0h2c.55 0 1-.45 1-1s-.45-1-1-1h-2c-.55 0-1 .45-1 1s.45 1 1 1zM11 2v2c0 .55.45 1 1 1s1-.45 1-1V2c0-.55-.45-1-1-1s-1 .45-1 1zm0 18v2c0 .55.45 1 1 1s1-.45 1-1v-2c0-.55-.45-1-1-1s-1 .45-1 1zM5.99 4.58c-.39-.39-1.03-.39-1.41 0s-.39 1.03 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0s.39-1.03 0-1.41L5.99 4.58zm12.37 12.37c-.39-.39-1.03-.39-1.41 0s-.39 1.03 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0s.39-1.03 0-1.41l-1.06-1.06zm1.06-10.96c.39-.39.39-1.03 0-1.41s-1.03-.39-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06zM7.05 18.36c.39-.39.39-1.03 0-1.41s-1.03-.39-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06z");
        private static readonly StreamGeometry FallbackMoonGeom = StreamGeometry.Parse("M12.3 2a10 10 0 0 0-1.9 19.8 10 10 0 0 0 10.9-10.9A10.02 10.02 0 0 1 12.3 2z");

        public StreamGeometry ThemeIconGeometry => _isDarkMode ? FallbackSunGeom : FallbackMoonGeom;
        public string ThemeToggleText => _isDarkMode ? "Light Mode" : "Dark Mode";

        public void OnToggleThemeClick(object? sender, RoutedEventArgs e)
        {
            IsDarkMode = !IsDarkMode;
        }

        // ── Note Selection Mode ───────────────────────────────────────────
        private bool _isSelectionMode = false;
        public bool IsSelectionMode
        {
            get => _isSelectionMode;
            set
            {
                if (_isSelectionMode != value)
                {
                    _isSelectionMode = value;
                    OnPropertyChanged(nameof(IsSelectionMode));
                    OnPropertyChanged(nameof(SelectionCountText));
                    OnPropertyChanged(nameof(SelectAllButtonText));
                    foreach (var note in FilteredNotes)
                    {
                        note.IsSelectionMode = value;
                        if (!value) note.IsSelected = false;
                    }
                    if (!value)
                    {
                        UnselectAllFolderNodes(FolderNodes);
                    }
                }
            }
        }

        public int GetTotalSelectedCount()
        {
            if (IsFolderTreeVisible)
            {
                return CountSelectedFolderNodes(FolderNodes);
            }
            return FilteredNotes.Count(n => n.IsSelected);
        }

        private int CountSelectedFolderNodes(IEnumerable<FolderNode> nodes)
        {
            int count = 0;
            foreach (var node in nodes)
            {
                if (node.IsSelected) count++;
                if (node.Children?.Count > 0)
                {
                    count += CountSelectedFolderNodes(node.Children);
                }
            }
            return count;
        }

        public string SelectionCountText => $"{GetTotalSelectedCount()} selected";

        private void OnModalContentPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
        }

        public string SelectAllButtonText
        {
            get
            {
                if (IsFolderTreeVisible)
                {
                    var all = GetAllFolderNodesFlat(FolderNodes);
                    bool allSelected = all.Count > 0 && all.All(n => n.IsSelected);
                    return allSelected ? "Deselect All" : "Select All";
                }
                var selectedCount = FilteredNotes.Count(n => n.IsSelected);
                return (selectedCount > 0 && selectedCount == FilteredNotes.Count) ? "Deselect All" : "Select All";
            }
        }

        public MobileMainView()
        {
            InitializeComponent();
            AssignViewDataContext();
            if (AuthWebView != null)
            {
                AuthWebView.NavigationStarted += OnAuthWebViewNavigationStarted;
                AuthWebView.NavigationCompleted += OnAuthWebViewNavigationCompleted;
            }
            if (MobileNotionEditor != null)
            {
                MobileNotionEditor.WordIgnored += OnEditorWordIgnored;
            }
            DataContextChanged += OnDataContextChanged;
            KeyDown += OnViewKeyDown;

            // Connect Android platform bridge
            AndroidPlatformBridge.KeyboardHeightChanged += OnKeyboardHeightChanged;
            AndroidPlatformBridge.HandleBackPress = OnBackPressedInternal;
            AndroidPlatformBridge.NetworkConnected += OnAndroidNetworkConnected;
            AndroidPlatformBridge.AppPaused += OnAppPaused;
            AndroidPlatformBridge.GetCurrentNotePath = () => _viewModel?.CurrentNote?.FullPath;
            AndroidPlatformBridge.GetIsEditorOpen = () => NoteEditorScreen?.IsVisible == true;
            AndroidPlatformBridge.GetSearchQuery = () => SearchQuery;

            // Hook real-time sync logger
            SyncLogger.LogAdded += OnSyncLogAdded;

            // Hook theme changes
            ThemeManager.Instance.ThemeChanged += (t) => Dispatcher.UIThread.Post(ApplyThemeVariantUpdates);

            // Pre-warm Milkdown WebView in the background so opening any note has 0ms cold latency
            Dispatcher.UIThread.Post(() =>
            {
                try { MobileNotionEditor?.Prewarm(); } catch { }
            }, DispatcherPriority.Background);
        }

        private void AssignViewDataContext()
        {
            if (NotesFeedScreen != null) NotesFeedScreen.DataContext = this;
            if (NoteEditorScreen != null) NoteEditorScreen.DataContext = this;
            if (GoogleDriveAccountModal != null) GoogleDriveAccountModal.DataContext = this;
            if (GoogleDriveAuthModal != null) GoogleDriveAuthModal.DataContext = this;
            if (NoteEditorMenuModal != null) NoteEditorMenuModal.DataContext = this;
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.DataContext = this;
            if (NewFolderModal != null) NewFolderModal.DataContext = this;
            if (TrashBinScreen != null) TrashBinScreen.DataContext = this;
            if (ConfirmDeleteFolderModal != null) ConfirmDeleteFolderModal.DataContext = this;
            if (ConfirmEmptyTrashModal != null) ConfirmEmptyTrashModal.DataContext = this;
            if (MoveNotesModal != null) MoveNotesModal.DataContext = this;
            if (MoveFoldersModal != null) MoveFoldersModal.DataContext = this;
            if (ConfirmBatchDeleteFoldersModal != null) ConfirmBatchDeleteFoldersModal.DataContext = this;
            if (SettingsModal != null) SettingsModal.DataContext = this;
            if (VersionHistoryModal != null) VersionHistoryModal.DataContext = this;
        }

        private void OnKeyboardHeightChanged(int heightDp)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (FormattingBar != null)
                {
                    FormattingBar.Margin = new Thickness(0, 0, 0, heightDp);
                }
            });
        }

        private void OnAndroidNetworkConnected()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel?.CloudSyncService?.IsAuthenticated == true &&
                    !_viewModel.CloudSyncService.IsSyncing &&
                    !_isLocalSyncInProgress)
                {
                    Console.WriteLine("[MobileMainView] Android network connected, requesting sync with 5s debounce");
                    _viewModel.RequestAutoSync(5000);
                }
            });
        }

        private void OnAppPaused()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    if (NoteEditorScreen?.IsVisible == true && _viewModel != null)
                    {
                        await _viewModel.SaveNoteAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MobileMainView] OnAppPaused auto-save failed: {ex.Message}");
                }
            });
        }

        private bool OnBackPressedInternal()
        {
            if (VersionHistoryModal?.IsVisible == true)
            {
                CloseVersionHistoryModal();
                return true;
            }
            if (SettingsModal?.IsVisible == true)
            {
                SettingsModal.IsVisible = false;
                if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
                {
                    MobileNotionEditor.IsVisible = true;
                }
                return true;
            }
            if (NoteEditorMenuModal?.IsVisible == true)
            {
                NoteEditorMenuModal.IsVisible = false;
                if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
                {
                    MobileNotionEditor.IsVisible = true;
                }
                return true;
            }
            if (ConfirmBatchDeleteFoldersModal?.IsVisible == true)
            {
                ConfirmBatchDeleteFoldersModal.IsVisible = false;
                return true;
            }
            if (MoveFoldersModal?.IsVisible == true)
            {
                MoveFoldersModal.IsVisible = false;
                return true;
            }
            if (MoveNotesModal?.IsVisible == true)
            {
                MoveNotesModal.IsVisible = false;
                if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
                {
                    MobileNotionEditor.IsVisible = true;
                }
                return true;
            }
            if (ParentFolderDropdownContainer?.IsVisible == true)
            {
                ParentFolderDropdownContainer.IsVisible = false;
                return true;
            }
            if (ConfirmDeleteFolderModal?.IsVisible == true)
            {
                ConfirmDeleteFolderModal.IsVisible = false;
                _pendingDeleteFolderName = null;
                return true;
            }
            if (ConfirmEmptyTrashModal?.IsVisible == true)
            {
                ConfirmEmptyTrashModal.IsVisible = false;
                return true;
            }
            if (NewFolderModal?.IsVisible == true)
            {
                NewFolderModal.IsVisible = false;
                return true;
            }
            if (TrashBinScreen?.IsVisible == true)
            {
                OnCloseTrashBinClick(this, new RoutedEventArgs());
                return true;
            }
            if (GoogleDriveAuthModal?.IsVisible == true)
            {
                GoogleDriveAuthModal.IsVisible = false;
                return true;
            }
            if (GoogleDriveAccountModal?.IsVisible == true)
            {
                GoogleDriveAccountModal.IsVisible = false;
                return true;
            }
            if (_isFolderSelectionMode)
            {
                ExitFolderSelectionMode();
                return true;
            }
            if (LeftDrawerOverlay?.IsVisible == true)
            {
                LeftDrawerOverlay.IsVisible = false;
                return true;
            }
            if (IsSelectionMode)
            {
                ExitSelectionMode();
                return true;
            }
            if (NoteEditorScreen?.IsVisible == true)
            {
                _ = SaveAndCloseEditorAsync();
                return true;
            }
            return false;
        }

        private void OnToggleViewModeClick(object? sender, RoutedEventArgs e)
        {
            IsGridView = !IsGridView;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.Notes.CollectionChanged -= OnNotesCollectionChanged;
                _viewModel.FolderNodes.CollectionChanged -= OnFolderNodesCollectionChanged;
                _viewModel.IgnoredWords.CollectionChanged -= OnIgnoredWordsCollectionChanged;
                if (_viewModel.CloudSyncService != null)
                {
                    _viewModel.CloudSyncService.SyncStatusChanged -= OnCloudSyncStatusChanged;
                }
            }

            _viewModel = DataContext as MainViewModel;

            AssignViewDataContext();

            if (_viewModel != null)
            {
                if (_viewModel.SettingsService != null)
                {
                    var style = _viewModel.SettingsService.Settings.MobileThemeStyle;
                    _isWindowsTheme = string.Equals(style, "Windows", StringComparison.OrdinalIgnoreCase);
                }

                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                _viewModel.Notes.CollectionChanged += OnNotesCollectionChanged;
                _viewModel.FolderNodes.CollectionChanged += OnFolderNodesCollectionChanged;
                CollapseAllFolders();
                OnPropertyChanged(nameof(FolderNodes));
                _viewModel.IgnoredWords.CollectionChanged += OnIgnoredWordsCollectionChanged;
                if (_viewModel.CloudSyncService != null)
                {
                    _viewModel.CloudSyncService.SyncStatusChanged += OnCloudSyncStatusChanged;
                }

                if (MobileNotionEditor != null)
                {
                    MobileNotionEditor.NotesRoot = _viewModel.NotesRoot;
                    MobileNotionEditor.CurrentNoteFolderPath = _viewModel.CurrentNote?.FolderPath;
                    MobileNotionEditor.CurrentNotePath = _viewModel.CurrentNote?.FullPath;
                    MobileNotionEditor.ThemeVariant = _isWindowsTheme ? "windows" : "android";
                    _ = MobileNotionEditor.SetThemeVariantAsync(MobileNotionEditor.ThemeVariant);
                    _ = MobileNotionEditor.SetThemeAsync(_isDarkMode);
                    _ = MobileNotionEditor.SetSpellCheckAsync(_viewModel.IsSpellCheckEnabled);
                    _ = MobileNotionEditor.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
                    MobileNotionEditor.MarkdownChanged -= OnEditorMarkdownChanged;
                    MobileNotionEditor.MarkdownChanged += OnEditorMarkdownChanged;
                    MobileNotionEditor.WordIgnored -= OnEditorWordIgnored;
                    MobileNotionEditor.WordIgnored += OnEditorWordIgnored;
                }

                RebuildTagChips();
                RebuildIgnoredWordsChips();
                ApplyFilters();
                ShowNotesFeed();

                // Restore saved state if process was killed in background
                if (AndroidPlatformBridge.RestoredState != null)
                {
                    var saved = AndroidPlatformBridge.RestoredState;
                    AndroidPlatformBridge.RestoredState = null;

                    if (!string.IsNullOrEmpty(saved.SearchQuery))
                    {
                        SearchQuery = saved.SearchQuery;
                    }

                    if (saved.IsEditorOpen && !string.IsNullOrEmpty(saved.ActiveNotePath))
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            var note = _viewModel.Notes.FirstOrDefault(n => string.Equals(n.FullPath, saved.ActiveNotePath, StringComparison.OrdinalIgnoreCase));
                            if (note != null)
                            {
                                _viewModel.SelectNote(note);
                                ActiveNoteTitle = note.Title;
                                ShowNoteEditor();
                            }
                        }, DispatcherPriority.Background);
                    }
                }

                TriggerAutoSyncInBackground(refreshAfterSync: true);
            }
        }

        private bool _isNotesUpdatePending = false;

        private void OnNotesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_isNotesUpdatePending) return;
            _isNotesUpdatePending = true;

            Dispatcher.UIThread.Post(() =>
            {
                _isNotesUpdatePending = false;
                RebuildTagChips();
                ApplyFilters();
            }, DispatcherPriority.Background);
        }

        private void OnFolderNodesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CollapseAllFolders();
                OnPropertyChanged(nameof(FolderNodes));
                OnPropertyChanged(nameof(IsNotesEmpty));
            }, DispatcherPriority.Background);
        }

        private void OnFolderNodeRowTapped(object? sender, TappedEventArgs e)
        {
            if (_wasNoteLongPress)
            {
                _wasNoteLongPress = false;
                e.Handled = true;
                return;
            }

            if ((sender as Control)?.DataContext is FolderNode node)
            {
                if (IsSelectionMode)
                {
                    node.IsSelected = !node.IsSelected;
                    if (!node.IsDirectory)
                    {
                        var match = FilteredNotes.FirstOrDefault(n =>
                            (!string.IsNullOrEmpty(node.FullPath) && string.Equals(n.Note?.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase)) ||
                            string.Equals(n.Title, node.DisplayName, StringComparison.OrdinalIgnoreCase));
                        if (match != null) match.IsSelected = node.IsSelected;
                    }
                    UpdateSelectionState();
                    e.Handled = true;
                    return;
                }

                if (node.IsDirectory)
                {
                    node.IsExpanded = !node.IsExpanded;
                    e.Handled = true;
                }
                else
                {
                    e.Handled = true;
                    OpenNoteFromNode(node);
                }
            }
        }

        private void OnFlatFileNoteTapped(object? sender, TappedEventArgs e)
        {
            if (_wasNoteLongPress)
            {
                _wasNoteLongPress = false;
                e.Handled = true;
                return;
            }

            if ((sender as Control)?.DataContext is MobileNoteItem item)
            {
                if (IsSelectionMode)
                {
                    item.IsSelected = !item.IsSelected;
                    UpdateSelectionState();
                    e.Handled = true;
                    return;
                }

                if (_viewModel != null)
                {
                    _viewModel.SelectNote(item.Note);
                    ActiveNoteTitle = item.Note.Title;
                    ShowNoteEditor();
                }
            }
        }

        private void OpenNoteFromNode(FolderNode node)
        {
            if (node.IsDirectory || _viewModel == null) return;

            var note = node.Note ?? _viewModel.Notes.FirstOrDefault(n =>
                string.Equals(n.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Title, node.Title, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Title, node.Name, StringComparison.OrdinalIgnoreCase));

            if (note != null)
            {
                _viewModel.SelectNote(note);
                ActiveNoteTitle = note.Title;
                ShowNoteEditor();
            }
        }

        private async void OnEditorWordIgnored(string word)
        {
            if (_viewModel != null && !string.IsNullOrWhiteSpace(word))
            {
                await _viewModel.AddIgnoredWord(word);
                if (MobileNotionEditor != null)
                {
                    await MobileNotionEditor.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
                }
                RebuildIgnoredWordsChips();
                OnPropertyChanged(nameof(IgnoredWordsCountText));
                ShowToast($"Added \"{word}\" to custom dictionary", isProgress: false, isError: false);
            }
        }

        private void OnIgnoredWordsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (MobileNotionEditor != null && _viewModel != null)
                {
                    await MobileNotionEditor.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
                }
                RebuildIgnoredWordsChips();
                OnPropertyChanged(nameof(IgnoredWordsCountText));
            });
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null || MobileNotionEditor == null) return;

            if (e.PropertyName == nameof(MainViewModel.EditorContent))
            {
                if (MobileNotionEditor != null)
                {
                    if (MobileNotionEditor.GetMarkdown() != _viewModel.EditorContent)
                    {
                        MobileNotionEditor.LoadMarkdown(_viewModel.EditorContent ?? string.Empty, force: false);
                    }
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.CurrentNote))
            {
                OnPropertyChanged(nameof(HasCurrentNoteLastModified));
                OnPropertyChanged(nameof(CurrentNoteLastModifiedText));
                OnPropertyChanged(nameof(CurrentNoteLastModifiedFullText));
                if (_viewModel.CurrentNote != null)
                {
                    _isUpdatingTitleFromViewModel = true;
                    try
                    {
                        ActiveNoteTitle = _viewModel.CurrentNote.Title;
                    }
                    finally
                    {
                        _isUpdatingTitleFromViewModel = false;
                    }
                    if (MobileNotionEditor != null)
                    {
                        MobileNotionEditor.CurrentNoteFolderPath = _viewModel.CurrentNote.FolderPath;
                        MobileNotionEditor.CurrentNotePath = _viewModel.CurrentNote.FullPath;
                        MobileNotionEditor.LoadMarkdown(_viewModel.EditorContent ?? string.Empty, force: true);
                    }
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.CurrentNoteLastModifiedText) ||
                     e.PropertyName == nameof(MainViewModel.HasCurrentNoteLastModified))
            {
                OnPropertyChanged(nameof(HasCurrentNoteLastModified));
                OnPropertyChanged(nameof(CurrentNoteLastModifiedText));
                OnPropertyChanged(nameof(CurrentNoteLastModifiedFullText));
            }
            else if (e.PropertyName == nameof(MainViewModel.NoteTitle))
            {
                if (_activeNoteTitle != _viewModel.NoteTitle)
                {
                    _isUpdatingTitleFromViewModel = true;
                    try
                    {
                        ActiveNoteTitle = _viewModel.NoteTitle ?? string.Empty;
                    }
                    finally
                    {
                        _isUpdatingTitleFromViewModel = false;
                    }
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.CloudSyncStatusText))
            {
                OnPropertyChanged(nameof(CloudSyncStatusText));
                OnPropertyChanged(nameof(ConnectedAccountEmail));
                OnPropertyChanged(nameof(LastSyncTimeText));
                OnPropertyChanged(nameof(IsSignedInToGoogle));
                OnPropertyChanged(nameof(CloudSyncIconBrush));
            }
        }

        private void OnEditorMarkdownChanged(string markdown)
        {
            if (_viewModel != null && _viewModel.EditorContent != markdown)
            {
                _viewModel.EditorContent = markdown;
            }
        }

        // ── Navigation between Feed and Editor ─────────────────────────────
        public void ShowNotesFeed()
        {
            NoteEditorScreen.IsVisible = false;
            if (MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = false;
            }
            NotesFeedScreen.IsVisible = true;
            ApplyFilters();
        }

        public void ShowNoteEditor()
        {
            NotesFeedScreen.IsVisible = false;
            NoteEditorScreen.IsVisible = true;
            if (MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = true;
                _ = MobileNotionEditor.SetThemeVariantAsync(_isWindowsTheme ? "windows" : "android");
                _ = MobileNotionEditor.SetThemeAsync(_isDarkMode);
                if (_viewModel != null)
                {
                    _ = MobileNotionEditor.SetSpellCheckAsync(_viewModel.IsSpellCheckEnabled);
                    _ = MobileNotionEditor.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
                    MobileNotionEditor.CurrentNoteFolderPath = _viewModel.CurrentNote?.FolderPath;
                    MobileNotionEditor.CurrentNotePath = _viewModel.CurrentNote?.FullPath;
                    MobileNotionEditor.LoadMarkdown(_viewModel.EditorContent ?? string.Empty, force: true);
                }
            }
            RebuildEditorTags();
            OnPropertyChanged(nameof(HasCurrentNoteLastModified));
            OnPropertyChanged(nameof(CurrentNoteLastModifiedText));
            OnPropertyChanged(nameof(CurrentNoteLastModifiedFullText));
        }

        private async void OnEditorBackClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                await SaveAndCloseEditorAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error in OnEditorBackClick: {ex.Message}");
                ShowNotesFeed();
            }
        }

        private async Task SaveAndCloseEditorAsync()
        {
            try
            {
                if (MobileNotionEditor != null)
                {
                    await MobileNotionEditor.FlushPendingChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error flushing editor changes: {ex.Message}");
            }

            try
            {
                if (_viewModel != null)
                {
                    await _viewModel.SaveNoteAsync();
                    TriggerAutoSyncInBackground();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error saving note: {ex.Message}");
            }

            ShowNotesFeed();
        }

        private void TriggerAutoSyncInBackground(bool refreshAfterSync = false)
        {
            if (_viewModel != null)
            {
                _viewModel.NotifyUserActivity();
                _viewModel.RequestAutoSync(60000);
            }
        }

        // ── Tag Chips & Note Filtering (Main Page) ──────────────────────────────
        private void RebuildTagChips()
        {
            if (TagChipsPanel == null || _viewModel == null) return;

            TagChipsPanel.Children.Clear();

            // 1. "All Notes" chip
            bool isAllNotes = string.IsNullOrEmpty(_selectedTag);
            var allBtn = new Button
            {
                Content = "All Notes",
                FontSize = 13,
                FontWeight = isAllNotes ? FontWeight.SemiBold : FontWeight.Normal,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 6),
                Background = isAllNotes ? AccentBrush : ChipUnselectedBackgroundBrush,
                Foreground = isAllNotes ? Brushes.White : ChipUnselectedTextBrush,
                BorderBrush = isAllNotes ? Brushes.Transparent : GlassBorderBrush,
                BorderThickness = new Thickness(isAllNotes ? 0 : 1),
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            allBtn.Click += (s, e) =>
            {
                SelectedTag = null;
                RebuildTagChips();
            };
            TagChipsPanel.Children.Add(allBtn);

            // 2. Discover all unique tags across notes
            var tagsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_viewModel.Notes != null)
            {
                foreach (var note in _viewModel.Notes)
                {
                    if (note.Tags != null)
                    {
                        foreach (var t in note.Tags)
                        {
                            var clean = t.TrimStart('#').Trim();
                            if (!string.IsNullOrEmpty(clean)) tagsSet.Add(clean);
                        }
                    }
                }
            }

            foreach (var tag in tagsSet.OrderBy(t => t))
            {
                bool isSelected = string.Equals(_selectedTag, tag, StringComparison.OrdinalIgnoreCase);
                var tagChip = new Button
                {
                    Content = $"#{tag}",
                    FontSize = 13,
                    FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(14, 6),
                    Background = isSelected ? AccentBrush : ChipUnselectedBackgroundBrush,
                    Foreground = isSelected ? Brushes.White : ChipUnselectedTextBrush,
                    BorderBrush = isSelected ? Brushes.Transparent : GlassBorderBrush,
                    BorderThickness = new Thickness(isSelected ? 0 : 1),
                    HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Tag = tag
                };
                tagChip.Click += (s, e) =>
                {
                    SelectedTag = isSelected ? null : tag;
                    RebuildTagChips();
                };
                TagChipsPanel.Children.Add(tagChip);
            }
        }

        private void ApplyFilters()
        {
            if (_viewModel == null) return;

            var query = _searchQuery.Trim();
            var source = _viewModel.Notes.AsEnumerable();

            // Filter by folder category
            if (!string.Equals(_selectedCategory, "All Notes", StringComparison.OrdinalIgnoreCase))
            {
                source = source.Where(n =>
                {
                    var folder = Path.GetFileName(n.FolderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    return string.Equals(folder, _selectedCategory, StringComparison.OrdinalIgnoreCase);
                });
            }

            // Filter by Tag
            if (!string.IsNullOrEmpty(_selectedTag))
            {
                source = source.Where(n => n.Tags != null && n.Tags.Any(t => string.Equals(t.TrimStart('#'), _selectedTag.TrimStart('#'), StringComparison.OrdinalIgnoreCase)));
            }

            // Filter by search query
            if (!string.IsNullOrWhiteSpace(query))
            {
                source = source.Where(n =>
                    (n.Title != null && n.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (n.Content != null && n.Content.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (n.FileName != null && n.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

            var targetNotes = source.OrderByDescending(n => n.LastModified).ToList();

            try
            {
                // In-place synchronization: avoids destroying visual tree & re-layout stutter
                var targetKeys = new HashSet<string>(targetNotes.Select(GetNoteKey));

                // 1. Remove items no longer matching
                for (int i = FilteredNotes.Count - 1; i >= 0; i--)
                {
                    var curKey = GetNoteKey(FilteredNotes[i].Note);
                    if (!targetKeys.Contains(curKey))
                    {
                        FilteredNotes.RemoveAt(i);
                    }
                }

                // 2. Index remaining items
                var existingItems = new Dictionary<string, MobileNoteItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in FilteredNotes)
                {
                    var key = GetNoteKey(item.Note);
                    if (!string.IsNullOrEmpty(key) && !existingItems.ContainsKey(key))
                    {
                        existingItems[key] = item;
                    }
                }

                // 3. Align order and insert missing items
                for (int i = 0; i < targetNotes.Count; i++)
                {
                    var note = targetNotes[i];
                    var key = GetNoteKey(note);

                    if (existingItems.TryGetValue(key, out var existingItem))
                    {
                        int currentIndex = FilteredNotes.IndexOf(existingItem);
                        if (currentIndex >= 0 && currentIndex < FilteredNotes.Count && i < FilteredNotes.Count && currentIndex != i)
                        {
                            FilteredNotes.Move(currentIndex, i);
                        }
                        existingItem.IsSelectionMode = _isSelectionMode;
                        existingItem.UpdateNote(note);
                    }
                    else
                    {
                        var newItem = new MobileNoteItem(note, _isDarkMode, _isWindowsTheme)
                        {
                            IsSelectionMode = _isSelectionMode
                        };
                        if (i <= FilteredNotes.Count)
                        {
                            FilteredNotes.Insert(i, newItem);
                        }
                        else
                        {
                            FilteredNotes.Add(newItem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error during in-place filter sync: {ex.Message}. Falling back to full reload.");
                FilteredNotes.Clear();
                foreach (var note in targetNotes)
                {
                    FilteredNotes.Add(new MobileNoteItem(note, _isDarkMode, _isWindowsTheme)
                    {
                        IsSelectionMode = _isSelectionMode
                    });
                }
            }

            OnPropertyChanged(nameof(NoteCountStatus));
            OnPropertyChanged(nameof(HasActiveFilter));
            OnPropertyChanged(nameof(IsFolderTreeVisible));
            OnPropertyChanged(nameof(IsFlatFileListVisible));
            OnPropertyChanged(nameof(IsNotesEmpty));
            UpdateSelectionState();
        }

        private static string GetNoteKey(NoteDocument note)
        {
            if (!string.IsNullOrWhiteSpace(note.FullPath))
                return note.FullPath.ToLowerInvariant();
            if (note.Id != Guid.Empty)
                return note.Id.ToString();
            return (note.FileName ?? note.Title ?? string.Empty).ToLowerInvariant();
        }

        private void OnClearSearchClick(object? sender, RoutedEventArgs e)
        {
            SearchQuery = string.Empty;
        }

        private async void OnFabNewNoteClick(object? sender, RoutedEventArgs e)
        {
            await CreateNoteWithInitialContentAsync(null);
        }

        private async void OnNewChecklistNoteClick(object? sender, RoutedEventArgs e)
        {
            await CreateNoteWithInitialContentAsync("- [ ] \n- [ ] \n- [ ] ");
        }

        private async void OnNewCodeNoteClick(object? sender, RoutedEventArgs e)
        {
            await CreateNoteWithInitialContentAsync("```csharp\n// Code snippet\n\n```");
        }

        private async Task CreateNoteWithInitialContentAsync(string? initialContent)
        {
            if (_viewModel == null) return;

            string? targetFolder = null;
            if (!string.Equals(_selectedCategory, "All Notes", StringComparison.OrdinalIgnoreCase))
            {
                targetFolder = Path.Combine(_viewModel.NotesRoot, _selectedCategory);
                if (!Directory.Exists(targetFolder))
                {
                    try { Directory.CreateDirectory(targetFolder); } catch { }
                }
            }

            await _viewModel.NewNoteAsync(targetFolder);
            if (!string.IsNullOrEmpty(initialContent) && _viewModel.CurrentNote != null)
            {
                _viewModel.EditorContent = initialContent;
                await _viewModel.SaveNoteAsync();
            }

            _isUpdatingTitleFromViewModel = true;
            try
            {
                ActiveNoteTitle = _viewModel.CurrentNote?.Title ?? "Untitled Note";
            }
            finally
            {
                _isUpdatingTitleFromViewModel = false;
            }
            ShowNoteEditor();
            if (_viewModel?.CurrentNote?.Title == "Untitled Note" || string.IsNullOrWhiteSpace(_viewModel?.CurrentNote?.Title))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    NoteTitleTextBox?.Focus();
                    NoteTitleTextBox?.SelectAll();
                }, DispatcherPriority.Background);
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
