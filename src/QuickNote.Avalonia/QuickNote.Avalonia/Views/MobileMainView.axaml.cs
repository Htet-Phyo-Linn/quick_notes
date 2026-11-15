using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Models;

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

        public static KeepColor GetColor(string? nameOrId, bool isDarkMode = true)
        {
            if (!string.IsNullOrWhiteSpace(nameOrId))
            {
                var match = Colors.FirstOrDefault(c => string.Equals(c.Name, nameOrId, StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(c.Background, nameOrId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    if (string.Equals(match.Name, "Default", StringComparison.OrdinalIgnoreCase))
                    {
                        return isDarkMode ? DarkDefault : LightDefault;
                    }
                    return match;
                }
            }

            return isDarkMode ? DarkDefault : LightDefault;
        }

        public static KeepColor GetDefaultColor(bool isDarkMode = true) => isDarkMode ? DarkDefault : LightDefault;
    }

    public class MobileNoteItem : INotifyPropertyChanged
    {
        private static readonly IBrush SelectedBorderBrush = new SolidColorBrush(Color.Parse("#3390ec"));
        private static readonly IBrush SelectedCheckBgBrush = new SolidColorBrush(Color.Parse("#3390ec"));
        private static readonly IBrush SelectedCheckBorderBrushConst = new SolidColorBrush(Color.Parse("#3390ec"));
        private static readonly IBrush UnselectedCheckBorderBrushDark = new SolidColorBrush(Color.Parse("#4b5563"));
        private static readonly IBrush UnselectedCheckBorderBrushLight = new SolidColorBrush(Color.Parse("#cbd5e1"));
        private static readonly Thickness SelectedBorderThicknessValue = new Thickness(2);
        private static readonly Thickness NormalBorderThicknessValue = new Thickness(1);

        private static readonly IBrush DarkSnippetBrush = new SolidColorBrush(Color.Parse("#8c97a8"));
        private static readonly IBrush LightSnippetBrush = new SolidColorBrush(Color.Parse("#64748b"));
        private static readonly IBrush DarkDateBrush = new SolidColorBrush(Color.Parse("#64748b"));
        private static readonly IBrush LightDateBrush = new SolidColorBrush(Color.Parse("#94a3b8"));
        private static readonly IBrush DarkFolderBadgeBg = new SolidColorBrush(Color.FromArgb(40, 51, 144, 236));
        private static readonly IBrush LightFolderBadgeBg = new SolidColorBrush(Color.FromArgb(28, 36, 129, 204));
        private static readonly IBrush DarkFolderBadgeFg = new SolidColorBrush(Color.Parse("#60a5fa"));
        private static readonly IBrush LightFolderBadgeFg = new SolidColorBrush(Color.Parse("#2563eb"));
        private static readonly IBrush DarkTagBadgeBg = new SolidColorBrush(Color.Parse("#1e2533"));
        private static readonly IBrush LightTagBadgeBg = new SolidColorBrush(Color.Parse("#f1f5f9"));
        private static readonly IBrush DarkTagBadgeFg = new SolidColorBrush(Color.Parse("#94a3b8"));
        private static readonly IBrush LightTagBadgeFg = new SolidColorBrush(Color.Parse("#475569"));

        public NoteDocument Note { get; }
        private KeepColor _color;
        private bool _isDarkMode = true;

        public MobileNoteItem(NoteDocument note, bool isDarkMode = true)
        {
            Note = note;
            _isDarkMode = isDarkMode;
            _color = ResolveColor(note, isDarkMode);
            ApplyBrushes();
        }

        private void ApplyBrushes()
        {
            CardBackground = _color.BackgroundBrush;
            CardBorderBrush = _color.BorderBrush;
            CardAccentBrush = _color.AccentBrush;
            CardTextBrush = _color.TextBrush;
        }

        public void UpdateTheme(bool isDarkMode)
        {
            _isDarkMode = isDarkMode;
            _color = ResolveColor(Note, isDarkMode);
            ApplyBrushes();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardBackground)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardBorderBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardAccentBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardTextBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveBorderBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnippetBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FolderBadgeBg)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FolderBadgeFg)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TagBadgeBg)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TagBadgeFg)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionCheckBorderBrush)));
        }

        private static KeepColor ResolveColor(NoteDocument note, bool isDarkMode)
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
                            return KeepPalette.GetColor(parts[1].Trim(), isDarkMode);
                        }
                    }
                }
            }

            // Always single unified clean liquid glass default color (no random colors)
            return KeepPalette.GetDefaultColor(isDarkMode);
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

        public string Title => !string.IsNullOrWhiteSpace(Note?.Title)
            ? Note.Title
            : (Path.GetFileNameWithoutExtension(Note?.FileName) ?? "Untitled Note");

        public string Snippet
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Note?.Content)) return "Empty note";
                var text = Note.Content;
                if (text.StartsWith("---"))
                {
                    var end = text.IndexOf("---", 3);
                    if (end > 3) text = text.Substring(end + 3).TrimStart();
                }
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var previewLines = lines.Take(5).Select(l => l.TrimStart('#', ' ', '-', '*', '>'));
                var combined = string.Join("\n", previewLines).Trim();
                if (string.IsNullOrWhiteSpace(combined)) return "Empty note";
                return combined.Length > 160 ? combined.Substring(0, 160) + "..." : combined;
            }
        }

        public string FolderName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Note?.FolderPath)) return "Notes";
                var name = Path.GetFileName(Note.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return string.IsNullOrWhiteSpace(name) ? "Notes" : name;
            }
        }

        public string FormattedDate => Note != null && Note.LastModified != DateTime.MinValue
            ? Note.LastModified.ToLocalTime().ToString("MMM d")
            : "Just now";

        public string[] Tags => Note?.Tags ?? Array.Empty<string>();
        public bool HasTags => Tags != null && Tags.Length > 0;

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

        public IBrush EffectiveBorderBrush => IsSelected
            ? SelectedBorderBrush
            : CardBorderBrush;

        public Thickness EffectiveBorderThickness => IsSelected
            ? SelectedBorderThicknessValue
            : NormalBorderThicknessValue;

        public IBrush SelectionCheckBackground => IsSelected
            ? SelectedCheckBgBrush
            : Brushes.Transparent;

        public IBrush SelectionCheckBorderBrush => IsSelected
            ? SelectedCheckBorderBrushConst
            : (_isDarkMode ? UnselectedCheckBorderBrushDark : UnselectedCheckBorderBrushLight);

        public event PropertyChangedEventHandler? PropertyChanged;
        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public partial class MobileMainView : UserControl, INotifyPropertyChanged
    {
        private MainViewModel? _viewModel;
        private string _selectedCategory = "All Notes";
        private string? _selectedTag = null;
        private string _searchQuery = string.Empty;
        private string _activeNoteTitle = string.Empty;
        private bool _isGridView = true;
        private string? _pendingDeleteFolderName = null;

        public ObservableCollection<MobileNoteItem> FilteredNotes { get; } = new();

        public bool IsGridView
        {
            get => _isGridView;
            set
            {
                if (_isGridView != value)
                {
                    _isGridView = value;
                    OnPropertyChanged(nameof(IsGridView));
                    OnPropertyChanged(nameof(GridColumns));
                    OnPropertyChanged(nameof(ViewModeIcon));
                }
            }
        }

        public int GridColumns => _isGridView ? 2 : 1;

        public StreamGeometry ViewModeIcon => (Resources.TryGetResource(_isGridView ? "IconListView" : "IconGridView", null, out var res) && res is StreamGeometry geom) ? geom : new StreamGeometry();

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
                    ApplyFilters();
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
                    OnPropertyChanged(nameof(ActiveNoteTitle));
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
        public bool IsNotesEmpty => FilteredNotes.Count == 0;

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
        public string SelectedTagText => $"#{_selectedTag}";

        // ── Telegram Liquid Glass Theming (Issue 3) ──────────────────────
        private bool _isDarkMode = true;
        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode != value)
                {
                    _isDarkMode = value;
                    if (_viewModel != null) _viewModel.IsDarkMode = value;
                    if (Application.Current != null)
                    {
                        Application.Current.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
                    }
                    OnPropertyChanged(nameof(IsDarkMode));
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

                    foreach (var note in FilteredNotes)
                    {
                        note.UpdateTheme(_isDarkMode);
                    }

                    if (MobileNotionEditor != null)
                    {
                        _ = MobileNotionEditor.SetThemeAsync(_isDarkMode);
                    }

                    RebuildTagChips();
                    if (LeftDrawerOverlay?.IsVisible == true)
                    {
                        RebuildDrawerFolders();
                        RebuildDrawerTags();
                    }
                }
            }
        }

        // Dark Mode: Deep Midnight Space + Frosted Liquid Glass
        private static readonly IBrush DarkAppBackground = new SolidColorBrush(Color.Parse("#0b0e14"));
        private static readonly IBrush DarkGlassSurface = new SolidColorBrush(Color.FromArgb(235, 22, 28, 38));
        private static readonly IBrush DarkGlassHeader = new SolidColorBrush(Color.FromArgb(240, 20, 26, 36));
        private static readonly IBrush DarkGlassBorder = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        private static readonly IBrush DarkTextPrimary = new SolidColorBrush(Color.Parse("#f8fafc"));
        private static readonly IBrush DarkTextSecondary = new SolidColorBrush(Color.Parse("#8c97a8"));
        private static readonly IBrush DarkTextMuted = new SolidColorBrush(Color.Parse("#5a6678"));
        private static readonly IBrush DarkAccent = new SolidColorBrush(Color.Parse("#3390ec")); // Telegram Electric Blue
        private static readonly IBrush DarkDrawerBg = new SolidColorBrush(Color.FromArgb(245, 17, 22, 34));
        private static readonly IBrush DarkModalBg = new SolidColorBrush(Color.FromArgb(248, 20, 26, 36));
        private static readonly IBrush DarkChipBg = new SolidColorBrush(Color.FromArgb(200, 26, 32, 44));
        private static readonly IBrush DarkChipText = new SolidColorBrush(Color.Parse("#cbd5e1"));
        private static readonly IBrush DarkFloatingBarBg = new SolidColorBrush(Color.FromArgb(235, 22, 28, 38));
        private static readonly IBrush DarkSelectionBarBg = new SolidColorBrush(Color.FromArgb(240, 27, 39, 59));
        private static readonly IBrush DarkToolbarBg = new SolidColorBrush(Color.FromArgb(245, 20, 26, 36));

        // Light Mode: Ethereal Daylight Canvas + Frosted White Liquid Glass
        private static readonly IBrush LightAppBackground = new SolidColorBrush(Color.Parse("#f0f4f9"));
        private static readonly IBrush LightGlassSurface = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255));
        private static readonly IBrush LightGlassHeader = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255));
        private static readonly IBrush LightGlassBorder = new SolidColorBrush(Color.FromArgb(35, 0, 0, 0));
        private static readonly IBrush LightTextPrimary = new SolidColorBrush(Color.Parse("#0f172a"));
        private static readonly IBrush LightTextSecondary = new SolidColorBrush(Color.Parse("#64748b"));
        private static readonly IBrush LightTextMuted = new SolidColorBrush(Color.Parse("#94a3b8"));
        private static readonly IBrush LightAccent = new SolidColorBrush(Color.Parse("#2481cc")); // Telegram Electric Blue
        private static readonly IBrush LightDrawerBg = new SolidColorBrush(Color.FromArgb(250, 255, 255, 255));
        private static readonly IBrush LightModalBg = new SolidColorBrush(Color.FromArgb(250, 255, 255, 255));
        private static readonly IBrush LightChipBg = new SolidColorBrush(Color.Parse("#e2e8f0"));
        private static readonly IBrush LightChipText = new SolidColorBrush(Color.Parse("#334155"));
        private static readonly IBrush LightFloatingBarBg = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255));
        private static readonly IBrush LightSelectionBarBg = new SolidColorBrush(Color.FromArgb(245, 224, 237, 255));
        private static readonly IBrush LightToolbarBg = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255));

        public IBrush AppBackgroundBrush => _isDarkMode ? DarkAppBackground : LightAppBackground;
        public IBrush GlassSurfaceBrush => _isDarkMode ? DarkGlassSurface : LightGlassSurface;
        public IBrush GlassHeaderBackgroundBrush => _isDarkMode ? DarkGlassHeader : LightGlassHeader;
        public IBrush GlassBorderBrush => _isDarkMode ? DarkGlassBorder : LightGlassBorder;
        public IBrush TextPrimaryBrush => _isDarkMode ? DarkTextPrimary : LightTextPrimary;
        public IBrush TextSecondaryBrush => _isDarkMode ? DarkTextSecondary : LightTextSecondary;
        public IBrush TextMutedBrush => _isDarkMode ? DarkTextMuted : LightTextMuted;
        public IBrush AccentBrush => _isDarkMode ? DarkAccent : LightAccent;
        public IBrush DrawerBackgroundBrush => _isDarkMode ? DarkDrawerBg : LightDrawerBg;
        public IBrush ModalBackgroundBrush => _isDarkMode ? DarkModalBg : LightModalBg;
        public IBrush ChipSelectedBackgroundBrush => DarkAccent;
        public IBrush ChipUnselectedBackgroundBrush => _isDarkMode ? DarkChipBg : LightChipBg;
        public IBrush ChipUnselectedTextBrush => _isDarkMode ? DarkChipText : LightChipText;
        public IBrush FloatingBarBackgroundBrush => _isDarkMode ? DarkFloatingBarBg : LightFloatingBarBg;
        public IBrush SelectionBarBackgroundBrush => _isDarkMode ? DarkSelectionBarBg : LightSelectionBarBg;
        public IBrush FabBackgroundBrush => DarkAccent;
        public IBrush EditorToolbarBackgroundBrush => _isDarkMode ? DarkToolbarBg : LightToolbarBg;

        private static readonly StreamGeometry FallbackSunGeom = StreamGeometry.Parse("M12 7c-2.76 0-5 2.24-5 5s2.24 5 5 5 5-2.24 5-5-2.24-5-5-5zM2 13h2c.55 0 1-.45 1-1s-.45-1-1-1H2c-.55 0-1 .45-1 1s.45 1 1 1zm18 0h2c.55 0 1-.45 1-1s-.45-1-1-1h-2c-.55 0-1 .45-1 1s.45 1 1 1zM11 2v2c0 .55.45 1 1 1s1-.45 1-1V2c0-.55-.45-1-1-1s-1 .45-1 1zm0 18v2c0 .55.45 1 1 1s1-.45 1-1v-2c0-.55-.45-1-1-1s-1 .45-1 1zM5.99 4.58c-.39-.39-1.03-.39-1.41 0s-.39 1.03 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0s.39-1.03 0-1.41L5.99 4.58zm12.37 12.37c-.39-.39-1.03-.39-1.41 0s-.39 1.03 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0s.39-1.03 0-1.41l-1.06-1.06zm1.06-10.96c.39-.39.39-1.03 0-1.41s-1.03-.39-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06zM7.05 18.36c.39-.39.39-1.03 0-1.41s-1.03-.39-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06z");
        private static readonly StreamGeometry FallbackMoonGeom = StreamGeometry.Parse("M12.3 2a10 10 0 0 0-1.9 19.8 10 10 0 0 0 10.9-10.9A10.02 10.02 0 0 1 12.3 2z");

        public StreamGeometry ThemeIconGeometry => _isDarkMode ? FallbackSunGeom : FallbackMoonGeom;
        public string ThemeToggleText => _isDarkMode ? "Light Mode" : "Dark Mode";

        public void OnToggleThemeClick(object? sender, RoutedEventArgs e)
        {
            IsDarkMode = !IsDarkMode;
        }

        // ── Note Selection Mode (Issue 1) ─────────────────────────────────
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
                }
            }
        }

        public string SelectionCountText
        {
            get
            {
                var count = FilteredNotes.Count(n => n.IsSelected);
                return $"{count} selected";
            }
        }

        public string SelectAllButtonText
        {
            get
            {
                var selectedCount = FilteredNotes.Count(n => n.IsSelected);
                return (selectedCount > 0 && selectedCount == FilteredNotes.Count) ? "Deselect All" : "Select All";
            }
        }

        public MobileMainView()
        {
            InitializeComponent();
            NotesFeedScreen.DataContext = this;
            NoteEditorScreen.DataContext = this;
            GoogleDriveAccountModal.DataContext = this;
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.DataContext = this;
            if (NewFolderModal != null) NewFolderModal.DataContext = this;
            if (TrashBinScreen != null) TrashBinScreen.DataContext = this;
            if (ConfirmDeleteFolderModal != null) ConfirmDeleteFolderModal.DataContext = this;
            if (ConfirmEmptyTrashModal != null) ConfirmEmptyTrashModal.DataContext = this;
            if (MoveNotesModal != null) MoveNotesModal.DataContext = this;
            if (MoveFoldersModal != null) MoveFoldersModal.DataContext = this;
            if (ConfirmBatchDeleteFoldersModal != null) ConfirmBatchDeleteFoldersModal.DataContext = this;
            if (AuthWebView != null)
            {
                AuthWebView.NavigationStarted += OnAuthWebViewNavigationStarted;
                AuthWebView.NavigationCompleted += OnAuthWebViewNavigationCompleted;
            }
            DataContextChanged += OnDataContextChanged;
            KeyDown += OnViewKeyDown;

            // Connect Android back press and dynamic keyboard height bridge
            AndroidPlatformBridge.KeyboardHeightChanged += OnKeyboardHeightChanged;
            AndroidPlatformBridge.HandleBackPress = OnBackPressedInternal;

            InitColorPaletteDrawer();
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

        private bool OnBackPressedInternal()
        {
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

        private void InitColorPaletteDrawer()
        {
            if (ColorPalettePanel == null) return;
            ColorPalettePanel.Children.Clear();
            foreach (var keepColor in KeepPalette.Colors)
            {
                var circle = new Button
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Background = new SolidColorBrush(Color.Parse(keepColor.Background)),
                    BorderBrush = new SolidColorBrush(Color.Parse(keepColor.Border)),
                    BorderThickness = new Thickness(2),
                    Margin = new Thickness(2),
                    Tag = keepColor.Name
                };
                ToolTip.SetTip(circle, keepColor.Name);
                circle.Click += (s, e) =>
                {
                    if (s is Button b && b.Tag is string colorName)
                    {
                        OnSelectNoteColor(colorName);
                    }
                };
                ColorPalettePanel.Children.Add(circle);
            }
        }

        private void OnToggleColorPickerClick(object? sender, RoutedEventArgs e)
        {
            if (ColorPickerBar != null)
            {
                ColorPickerBar.IsVisible = !ColorPickerBar.IsVisible;
            }
        }

        private async void OnSelectNoteColor(string colorName)
        {
            if (_viewModel?.CurrentNote == null) return;
            var content = _viewModel.EditorContent ?? string.Empty;

            string newContent;
            if (content.StartsWith("---"))
            {
                var end = content.IndexOf("---", 3);
                if (end > 3)
                {
                    var header = content.Substring(3, end - 3);
                    var body = content.Substring(end + 3);
                    var lines = header.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
                    var colorLineIdx = lines.FindIndex(l => l.Trim().StartsWith("color:", StringComparison.OrdinalIgnoreCase));
                    if (colorLineIdx >= 0)
                    {
                        lines[colorLineIdx] = $"color: {colorName}";
                    }
                    else
                    {
                        lines.Add($"color: {colorName}");
                    }
                    newContent = $"---{string.Join(Environment.NewLine, lines)}---{body}";
                }
                else
                {
                    newContent = $"---{Environment.NewLine}color: {colorName}{Environment.NewLine}---{Environment.NewLine}{content}";
                }
            }
            else
            {
                newContent = $"---{Environment.NewLine}color: {colorName}{Environment.NewLine}---{Environment.NewLine}{content}";
            }

            _viewModel.EditorContent = newContent;
            await _viewModel.SaveNoteAsync();
            if (ColorPickerBar != null)
            {
                ColorPickerBar.IsVisible = false;
            }
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
            }

            _viewModel = DataContext as MainViewModel;

            NotesFeedScreen.DataContext = this;
            NoteEditorScreen.DataContext = this;
            GoogleDriveAccountModal.DataContext = this;

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                _viewModel.Notes.CollectionChanged += OnNotesCollectionChanged;

                if (MobileNotionEditor != null)
                {
                    MobileNotionEditor.NotesRoot = _viewModel.NotesRoot;
                    MobileNotionEditor.CurrentNoteFolderPath = _viewModel.CurrentNote?.FolderPath;
                    MobileNotionEditor.CurrentNotePath = _viewModel.CurrentNote?.FullPath;
                    MobileNotionEditor.MarkdownChanged += OnEditorMarkdownChanged;

                    if (!string.IsNullOrEmpty(_viewModel.EditorContent))
                    {
                        MobileNotionEditor.LoadMarkdown(_viewModel.EditorContent, force: true);
                    }
                }

                RebuildTagChips();
                ApplyFilters();
                ShowNotesFeed();
                TriggerAutoSyncInBackground(refreshAfterSync: true);
            }
        }

        private void OnNotesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RebuildTagChips();
                ApplyFilters();
            });
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null || MobileNotionEditor == null) return;

            if (e.PropertyName == nameof(MainViewModel.EditorContent))
            {
                if (MobileNotionEditor.GetMarkdown() != _viewModel.EditorContent)
                {
                    MobileNotionEditor.LoadMarkdown(_viewModel.EditorContent ?? string.Empty, force: true);
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.CurrentNote))
            {
                if (_viewModel.CurrentNote != null)
                {
                    ActiveNoteTitle = _viewModel.CurrentNote.Title;
                    MobileNotionEditor.CurrentNoteFolderPath = _viewModel.CurrentNote.FolderPath;
                    MobileNotionEditor.CurrentNotePath = _viewModel.CurrentNote.FullPath;
                    MobileNotionEditor.LoadMarkdown(_viewModel.EditorContent ?? string.Empty, force: true);
                    ShowNoteEditor();
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.NoteTitle))
            {
                if (_activeNoteTitle != _viewModel.NoteTitle)
                {
                    ActiveNoteTitle = _viewModel.NoteTitle ?? string.Empty;
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
            if (ColorPickerBar != null)
            {
                ColorPickerBar.IsVisible = false;
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
            }
            RebuildEditorTags();
        }

        private async void OnEditorBackClick(object? sender, RoutedEventArgs e)
        {
            await SaveAndCloseEditorAsync();
        }

        private async Task SaveAndCloseEditorAsync()
        {
            if (MobileNotionEditor != null)
            {
                await MobileNotionEditor.FlushPendingChangesAsync();
            }
            if (_viewModel != null)
            {
                await _viewModel.SaveNoteAsync();
                TriggerAutoSyncInBackground();
            }
            ShowNotesFeed();
        }

        private void TriggerAutoSyncInBackground(bool refreshAfterSync = false)
        {
            if (_viewModel?.CloudSyncService != null && _viewModel.CloudSyncService.IsAuthenticated)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await _viewModel.CloudSyncService.TriggerSyncAsync();
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            if (refreshAfterSync)
                            {
                                await _viewModel.RefreshFolderTreeAsync();
                                await _viewModel.Refresh();
                                RebuildTagChips();
                                RebuildDrawerFolders();
                                RebuildDrawerTags();
                                ApplyFilters();
                            }
                            OnPropertyChanged(nameof(LastSyncTimeText));
                            OnPropertyChanged(nameof(CloudSyncStatusText));
                            OnPropertyChanged(nameof(TotalNotesCountText));
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MobileMainView] Background auto-sync error: {ex.Message}");
                    }
                });
            }
        }

        // ── Tag Editor & Tag Management in NoteEditorScreen ──────────────────────────
        private void RebuildEditorTags()
        {
            if (EditorTagsPanel == null) return;
            EditorTagsPanel.Children.Clear();

            var currentTags = _viewModel?.CurrentNote?.Tags ?? Array.Empty<string>();
            foreach (var tag in currentTags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                var clean = tag.TrimStart('#').Trim();
                if (string.IsNullOrEmpty(clean)) continue;

                var tagPill = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#21262d")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#30363d")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Height = 24,
                    Padding = new Thickness(8, 0, 4, 0),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                var pillStack = new StackPanel
                {
                    Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 4,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                pillStack.Children.Add(new TextBlock
                {
                    Text = "#",
                    Foreground = new SolidColorBrush(Color.Parse("#58a6ff")),
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                });
                pillStack.Children.Add(new TextBlock
                {
                    Text = clean,
                    Foreground = new SolidColorBrush(Color.Parse("#c9d1d9")),
                    FontSize = 11,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                });
                var removeBtn = new Button
                {
                    Content = "✕",
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Color.Parse("#8b949e")),
                    FontSize = 9,
                    Height = 18,
                    Padding = new Thickness(2, 0),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Tag = clean
                };
                removeBtn.Click += async (s, e) =>
                {
                    if (s is Button b && b.Tag is string tToRemove)
                    {
                        await RemoveTagFromCurrentNoteAsync(tToRemove);
                    }
                };
                pillStack.Children.Add(removeBtn);
                tagPill.Child = pillStack;
                EditorTagsPanel.Children.Add(tagPill);
            }
        }

        private void OnShowAddTagClick(object? sender, RoutedEventArgs e)
        {
            if (BtnShowAddTag != null) BtnShowAddTag.IsVisible = false;
            if (EditorAddTagContainer != null)
            {
                EditorAddTagContainer.IsVisible = true;
                if (EditorNewTagTextBox != null)
                {
                    EditorNewTagTextBox.Text = string.Empty;
                    EditorNewTagTextBox.Focus();
                }
            }
        }

        private void OnCancelAddTagClick(object? sender, RoutedEventArgs e)
        {
            if (EditorAddTagContainer != null) EditorAddTagContainer.IsVisible = false;
            if (BtnShowAddTag != null) BtnShowAddTag.IsVisible = true;
        }

        private async void OnConfirmAddTagClick(object? sender, RoutedEventArgs e)
        {
            await ConfirmAddTagFromInputAsync();
        }

        private async void OnEditorNewTagKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ConfirmAddTagFromInputAsync();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                OnCancelAddTagClick(sender, e);
            }
        }

        private async Task ConfirmAddTagFromInputAsync()
        {
            var tagText = EditorNewTagTextBox?.Text?.Trim().TrimStart('#');
            if (!string.IsNullOrWhiteSpace(tagText) && _viewModel?.CurrentNote != null)
            {
                var existing = _viewModel.CurrentNote.Tags?.ToList() ?? new List<string>();
                if (!existing.Any(t => string.Equals(t.TrimStart('#'), tagText, StringComparison.OrdinalIgnoreCase)))
                {
                    existing.Add(tagText);
                    _viewModel.CurrentNote.Tags = existing.ToArray();
                    await _viewModel.SaveNoteAsync();
                    RebuildEditorTags();
                    RebuildTagChips();
                    RebuildDrawerTags();
                    ApplyFilters();
                }
            }
            OnCancelAddTagClick(this, new RoutedEventArgs());
        }

        private async Task RemoveTagFromCurrentNoteAsync(string tagName)
        {
            if (_viewModel?.CurrentNote == null) return;
            var existing = _viewModel.CurrentNote.Tags?.ToList() ?? new List<string>();
            existing.RemoveAll(t => string.Equals(t.TrimStart('#'), tagName, StringComparison.OrdinalIgnoreCase));
            _viewModel.CurrentNote.Tags = existing.ToArray();
            await _viewModel.SaveNoteAsync();
            RebuildEditorTags();
            RebuildTagChips();
            RebuildDrawerTags();
            ApplyFilters();
        }

        // ── Folder Discovery & Navigation ──────────────────────────────
        private HashSet<string> DiscoverAllFolders()
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var root = _viewModel?.NotesRoot;
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    var dirs = Directory.GetDirectories(root);
                    foreach (var dir in dirs)
                    {
                        var name = Path.GetFileName(dir);
                        if (!string.IsNullOrEmpty(name) && !name.StartsWith(".") && !string.Equals(name, "Assets", StringComparison.OrdinalIgnoreCase))
                        {
                            folders.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error discovering disk folders: {ex.Message}");
            }

            if (_viewModel?.Notes != null)
            {
                foreach (var n in _viewModel.Notes)
                {
                    var f = Path.GetFileName(n.FolderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!string.IsNullOrWhiteSpace(f)) folders.Add(f);
                }
            }

            return folders;
        }

        private List<string> DiscoverAllFolderPaths()
        {
            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var root = _viewModel?.NotesRoot;
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    var dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
                    foreach (var dir in dirs)
                    {
                        var rel = Path.GetRelativePath(root, dir).Replace('\\', '/');
                        var parts = rel.Split('/');
                        if (!parts.Any(p => p.StartsWith(".") || string.Equals(p, "Assets", StringComparison.OrdinalIgnoreCase)))
                        {
                            list.Add(rel);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error discovering folder paths: {ex.Message}");
            }

            if (_viewModel?.Notes != null && !string.IsNullOrEmpty(_viewModel.NotesRoot))
            {
                foreach (var note in _viewModel.Notes)
                {
                    if (!string.IsNullOrWhiteSpace(note.FolderPath) && Directory.Exists(note.FolderPath))
                    {
                        var rel = Path.GetRelativePath(_viewModel.NotesRoot, note.FolderPath).Replace('\\', '/');
                        if (!string.IsNullOrEmpty(rel) && rel != ".")
                        {
                            list.Add(rel);
                        }
                    }
                }
            }

            return list.OrderBy(f => f).ToList();
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

        // ── New Folder Dialog Handlers (Issue 2: Parent folder selector) ────────
        private string? _selectedParentFolderPath = null;

        private void ShowNewFolderDialog()
        {
            _selectedParentFolderPath = null;
            if (SelectedParentFolderLabel != null)
            {
                SelectedParentFolderLabel.Text = "/ (Root - Top level)";
            }
            if (ParentFolderDropdownContainer != null)
            {
                ParentFolderDropdownContainer.IsVisible = false;
            }
            if (NewFolderNameTextBox != null)
            {
                NewFolderNameTextBox.Text = string.Empty;
            }
            if (NewFolderModal != null)
            {
                NewFolderModal.IsVisible = true;
                NewFolderNameTextBox?.Focus();
            }
        }

        private void OnToggleParentFolderDropdownClick(object? sender, RoutedEventArgs e)
        {
            if (ParentFolderDropdownContainer == null) return;
            ParentFolderDropdownContainer.IsVisible = !ParentFolderDropdownContainer.IsVisible;
            if (ParentFolderDropdownContainer.IsVisible)
            {
                PopulateParentFoldersList(ParentFolderSearchBox?.Text);
            }
        }

        private void OnParentFolderSearchKeyUp(object? sender, KeyEventArgs e)
        {
            PopulateParentFoldersList(ParentFolderSearchBox?.Text);
        }

        private void PopulateParentFoldersList(string? filter)
        {
            if (ParentFolderItemsPanel == null) return;
            ParentFolderItemsPanel.Children.Clear();

            var cleanFilter = filter?.Trim() ?? string.Empty;

            // 1. Root option
            if (string.IsNullOrEmpty(cleanFilter) || "root top level".Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
            {
                var rootBtn = CreateParentFolderOptionButton(null, "/ (Root - Top level)");
                ParentFolderItemsPanel.Children.Add(rootBtn);
            }

            // 2. All discovered folders
            var allFolders = DiscoverAllFolderPaths();
            foreach (var f in allFolders)
            {
                if (string.IsNullOrEmpty(cleanFilter) || f.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
                {
                    var folderBtn = CreateParentFolderOptionButton(f, f);
                    ParentFolderItemsPanel.Children.Add(folderBtn);
                }
            }
        }

        private Button CreateParentFolderOptionButton(string? folderPath, string displayTitle)
        {
            var isSelected = string.Equals(_selectedParentFolderPath, folderPath, StringComparison.OrdinalIgnoreCase) ||
                             (string.IsNullOrEmpty(_selectedParentFolderPath) && folderPath == null);

            var btn = new Button
            {
                Background = isSelected ? new SolidColorBrush(Color.Parse("#252d3d")) : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };

            var icon = new PathIcon
            {
                Data = (StreamGeometry)Resources["IconFolder"]!,
                Width = 14,
                Height = 14,
                Foreground = isSelected ? new SolidColorBrush(Color.Parse("#3b82f6")) : new SolidColorBrush(Color.Parse("#9ca3af")),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var text = new TextBlock
            {
                Text = displayTitle,
                FontSize = 12,
                FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = isSelected ? new SolidColorBrush(Color.Parse("#60a5fa")) : Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(text, 1);

            grid.Children.Add(icon);
            grid.Children.Add(text);
            btn.Content = grid;

            btn.Click += (s, e) =>
            {
                _selectedParentFolderPath = folderPath;
                if (SelectedParentFolderLabel != null)
                {
                    SelectedParentFolderLabel.Text = displayTitle;
                }
                if (ParentFolderDropdownContainer != null)
                {
                    ParentFolderDropdownContainer.IsVisible = false;
                }
            };

            return btn;
        }

        private async void OnConfirmNewFolderClick(object? sender, RoutedEventArgs e)
        {
            var folderName = NewFolderNameTextBox?.Text?.Trim();
            if (NewFolderModal != null) NewFolderModal.IsVisible = false;
            if (string.IsNullOrWhiteSpace(folderName)) return;

            // Sanitize folder name
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                folderName = folderName.Replace(c, '_');
            }

            try
            {
                var root = _viewModel?.NotesRoot;
                if (!string.IsNullOrEmpty(root))
                {
                    string targetDir;
                    if (!string.IsNullOrEmpty(_selectedParentFolderPath))
                    {
                        targetDir = Path.Combine(root, _selectedParentFolderPath.Replace('/', Path.DirectorySeparatorChar), folderName);
                    }
                    else
                    {
                        targetDir = Path.Combine(root, folderName);
                    }

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    if (_viewModel != null)
                    {
                        await _viewModel.RefreshFolderTreeAsync();
                    }
                    _selectedCategory = folderName;
                    RebuildTagChips();
                    if (LeftDrawerOverlay?.IsVisible == true)
                    {
                        RebuildDrawerFolders();
                    }
                    ApplyFilters();
                    TriggerAutoSyncInBackground();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Failed creating folder '{folderName}': {ex.Message}");
            }
        }

        private void OnCancelNewFolderClick(object? sender, RoutedEventArgs e)
        {
            if (NewFolderModal != null) NewFolderModal.IsVisible = false;
        }

        // ── Left Sidebar / Navigation Drawer ──────────────────────────────
        private void OnToggleLeftDrawerClick(object? sender, RoutedEventArgs e)
        {
            if (LeftDrawerOverlay == null) return;
            LeftDrawerOverlay.IsVisible = !LeftDrawerOverlay.IsVisible;
            if (LeftDrawerOverlay.IsVisible)
            {
                RebuildDrawerFolders();
                RebuildDrawerTags();
                UpdateTrashBadge();
                OnPropertyChanged(nameof(TotalNotesCountText));
                OnPropertyChanged(nameof(ConnectedAccountEmail));
            }
        }

        private void OnCloseLeftDrawerClick(object? sender, RoutedEventArgs e)
        {
            if (_isFolderSelectionMode)
            {
                ExitFolderSelectionMode();
            }
            if (LeftDrawerOverlay != null)
            {
                LeftDrawerOverlay.IsVisible = false;
            }
        }

        private void OnDrawerAllNotesClick(object? sender, RoutedEventArgs e)
        {
            if (_isFolderSelectionMode) return;
            _selectedCategory = "All Notes";
            _selectedTag = null;
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
            RebuildTagChips();
            OnPropertyChanged(nameof(SelectedTag));
            OnPropertyChanged(nameof(HasSelectedTag));
            OnPropertyChanged(nameof(SelectedTagText));
            ApplyFilters();
        }

        private void OnDrawerTrashBinClick(object? sender, RoutedEventArgs e)
        {
            if (_isFolderSelectionMode) return;
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
            NotesFeedScreen.IsVisible = false;
            NoteEditorScreen.IsVisible = false;
            if (MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = false;
            }
            if (TrashBinScreen != null)
            {
                TrashBinScreen.IsVisible = true;
                _ = LoadTrashItemsAsync();
            }
        }

        private void OnCloseTrashBinClick(object? sender, RoutedEventArgs e)
        {
            if (TrashBinScreen != null)
            {
                TrashBinScreen.IsVisible = false;
            }
            ShowNotesFeed();
        }

        private async void OnRestoreAllTrashClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.TrashBinService == null) return;
            try
            {
                var items = (await _viewModel.TrashBinService.GetTrashItemsAsync()).ToList();
                foreach (var item in items)
                {
                    await _viewModel.TrashBinService.RestoreItemAsync(item);
                }
                await _viewModel.RefreshFolderTreeAsync();
                await _viewModel.Refresh();
                RebuildTagChips();
                RebuildDrawerFolders();
                RebuildDrawerTags();
                UpdateTrashBadge();
                ApplyFilters();
                OnPropertyChanged(nameof(TotalNotesCountText));
                await LoadTrashItemsAsync();
                TriggerAutoSyncInBackground();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] OnRestoreAllTrashClick error: {ex.Message}");
            }
        }

        private void OnDrawerNewFolderClick(object? sender, RoutedEventArgs e)
        {
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
            ShowNewFolderDialog();
        }

        private void OnOpenCloudSyncClick(object? sender, RoutedEventArgs e)
        {
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
            UpdateModalAccountStatus();
            if (GoogleDriveAccountModal != null)
            {
                GoogleDriveAccountModal.IsVisible = true;
            }
        }

        // ── Folder Multi-Select Mode in Left Drawer (Issue 1) ─────────────
        private bool _isFolderSelectionMode = false;
        private readonly HashSet<string> _selectedFolders = new(StringComparer.OrdinalIgnoreCase);

        private void EnterFolderSelectionModeWithFolder(string folder)
        {
            _isFolderSelectionMode = true;
            _selectedFolders.Clear();
            _selectedFolders.Add(folder);
            UpdateFolderSelectionState();
            RebuildDrawerFolders();
        }

        private void ExitFolderSelectionMode()
        {
            _isFolderSelectionMode = false;
            _selectedFolders.Clear();
            UpdateFolderSelectionState();
            RebuildDrawerFolders();
        }

        private void UpdateFolderSelectionState()
        {
            if (DrawerNormalHeader != null) DrawerNormalHeader.IsVisible = !_isFolderSelectionMode;
            if (DrawerFolderSelectionHeader != null) DrawerFolderSelectionHeader.IsVisible = _isFolderSelectionMode;
            if (DrawerNormalFooter != null) DrawerNormalFooter.IsVisible = !_isFolderSelectionMode;
            if (DrawerFolderSelectionFooter != null) DrawerFolderSelectionFooter.IsVisible = _isFolderSelectionMode;

            if (DrawerFolderSelectionCountText != null)
            {
                DrawerFolderSelectionCountText.Text = $"{_selectedFolders.Count} selected";
            }

            if (DrawerFolderSelectAllBtnText != null)
            {
                var all = DiscoverAllFolderPaths();
                bool allSelected = all.Count > 0 && all.All(f => _selectedFolders.Contains(f));
                DrawerFolderSelectAllBtnText.Text = allSelected ? "None" : "All";
            }
        }

        private void OnCancelFolderSelectionClick(object? sender, RoutedEventArgs e)
        {
            ExitFolderSelectionMode();
        }

        private void OnToggleSelectAllFoldersClick(object? sender, RoutedEventArgs e)
        {
            var all = DiscoverAllFolderPaths();
            bool allSelected = all.Count > 0 && all.All(f => _selectedFolders.Contains(f));

            if (allSelected)
            {
                _selectedFolders.Clear();
            }
            else
            {
                foreach (var f in all) _selectedFolders.Add(f);
            }

            UpdateFolderSelectionState();
            RebuildDrawerFolders();
        }

        private void OnBatchMoveFoldersClick(object? sender, RoutedEventArgs e)
        {
            if (_selectedFolders.Count == 0) return;
            if (MoveFoldersModalTitle != null)
            {
                MoveFoldersModalTitle.Text = $"Move {_selectedFolders.Count} Folder(s)";
            }
            if (MoveFoldersModal != null)
            {
                MoveFoldersModal.IsVisible = true;
                PopulateMoveFoldersDestinations(null);
            }
        }

        private void OnCancelMoveFoldersClick(object? sender, RoutedEventArgs e)
        {
            if (MoveFoldersModal != null) MoveFoldersModal.IsVisible = false;
        }

        private void OnMoveFoldersSearchKeyUp(object? sender, KeyEventArgs e)
        {
            PopulateMoveFoldersDestinations(MoveFoldersSearchBox?.Text);
        }

        private void PopulateMoveFoldersDestinations(string? filter)
        {
            if (MoveFoldersDestinationsPanel == null) return;
            MoveFoldersDestinationsPanel.Children.Clear();

            var cleanFilter = filter?.Trim() ?? string.Empty;

            // 1. Root option
            if (string.IsNullOrEmpty(cleanFilter) || "root top level".Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
            {
                var rootBtn = CreateMoveFolderDestinationButton(null, "/ (Root - Top level)");
                MoveFoldersDestinationsPanel.Children.Add(rootBtn);
            }

            // 2. Discover available destination folders
            var allFolders = DiscoverAllFolderPaths();
            foreach (var folder in allFolders)
            {
                // Disallow moving a folder into itself or its own descendants
                bool isInvalidDest = _selectedFolders.Any(sf =>
                    string.Equals(sf, folder, StringComparison.OrdinalIgnoreCase) ||
                    folder.StartsWith(sf + "/", StringComparison.OrdinalIgnoreCase));

                if (!isInvalidDest)
                {
                    if (string.IsNullOrEmpty(cleanFilter) || folder.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        var btn = CreateMoveFolderDestinationButton(folder, folder);
                        MoveFoldersDestinationsPanel.Children.Add(btn);
                    }
                }
            }
        }

        private Button CreateMoveFolderDestinationButton(string? targetRelPath, string displayTitle)
        {
            var btn = new Button
            {
                Background = new SolidColorBrush(Color.Parse("#181820")),
                BorderBrush = new SolidColorBrush(Color.Parse("#2e2e38")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var icon = new PathIcon
            {
                Data = (StreamGeometry)Resources["IconFolder"]!,
                Width = 16,
                Height = 16,
                Foreground = new SolidColorBrush(Color.Parse("#3b82f6")),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var text = new TextBlock
            {
                Text = displayTitle,
                FontSize = 13,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(text, 1);

            grid.Children.Add(icon);
            grid.Children.Add(text);
            btn.Content = grid;

            btn.Click += async (s, e) =>
            {
                try
                {
                    var root = _viewModel?.NotesRoot;
                    if (!string.IsNullOrEmpty(root))
                    {
                        foreach (var selFolder in _selectedFolders.ToList())
                        {
                            var srcDir = Path.Combine(root, selFolder.Replace('/', Path.DirectorySeparatorChar));
                            var folderName = Path.GetFileName(srcDir);
                            var destParent = string.IsNullOrEmpty(targetRelPath)
                                ? root
                                : Path.Combine(root, targetRelPath.Replace('/', Path.DirectorySeparatorChar));
                            var destDir = Path.Combine(destParent, folderName);

                            if (Directory.Exists(srcDir) && !string.Equals(srcDir, destDir, StringComparison.OrdinalIgnoreCase))
                            {
                                if (!Directory.Exists(destParent))
                                {
                                    Directory.CreateDirectory(destParent);
                                }
                                if (!Directory.Exists(destDir))
                                {
                                    Directory.Move(srcDir, destDir);
                                }
                            }
                        }

                        if (MoveFoldersModal != null) MoveFoldersModal.IsVisible = false;
                        ExitFolderSelectionMode();

                        if (_viewModel != null)
                        {
                            await _viewModel.RefreshFolderTreeAsync();
                            await _viewModel.Refresh();
                        }

                        RebuildDrawerFolders();
                        ApplyFilters();
                        TriggerAutoSyncInBackground();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MobileMainView] Error moving folders: {ex.Message}");
                    if (MoveFoldersModal != null) MoveFoldersModal.IsVisible = false;
                }
            };

            return btn;
        }

        private void OnBatchDeleteFoldersClick(object? sender, RoutedEventArgs e)
        {
            if (_selectedFolders.Count == 0) return;
            if (ConfirmBatchDeleteFoldersMessageText != null)
            {
                ConfirmBatchDeleteFoldersMessageText.Text = $"Delete {_selectedFolders.Count} selected folder(s) and move their notes to the Trash Bin?";
            }
            if (ConfirmBatchDeleteFoldersModal != null)
            {
                ConfirmBatchDeleteFoldersModal.IsVisible = true;
            }
        }

        private void OnCancelBatchDeleteFoldersClick(object? sender, RoutedEventArgs e)
        {
            if (ConfirmBatchDeleteFoldersModal != null)
            {
                ConfirmBatchDeleteFoldersModal.IsVisible = false;
            }
        }

        private async void OnConfirmBatchDeleteFoldersClick(object? sender, RoutedEventArgs e)
        {
            if (ConfirmBatchDeleteFoldersModal != null) ConfirmBatchDeleteFoldersModal.IsVisible = false;
            var selectedFoldersList = _selectedFolders.ToList();
            ExitFolderSelectionMode();

            try
            {
                var root = _viewModel?.NotesRoot;
                if (!string.IsNullOrEmpty(root) && _viewModel != null)
                {
                    foreach (var folder in selectedFoldersList)
                    {
                        var folderPath = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
                        if (Directory.Exists(folderPath))
                        {
                            await _viewModel.TrashBinService.MoveToTrashAsync(folderPath);
                        }
                    }

                    _selectedCategory = "All Notes";
                    await _viewModel.RefreshFolderTreeAsync();
                    await _viewModel.Refresh();
                    RebuildTagChips();
                    if (LeftDrawerOverlay?.IsVisible == true)
                    {
                        RebuildDrawerFolders();
                        RebuildDrawerTags();
                        UpdateTrashBadge();
                    }
                    ApplyFilters();
                    OnPropertyChanged(nameof(TotalNotesCountText));
                    TriggerAutoSyncInBackground();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error in batch delete folders: {ex.Message}");
            }
        }

        private void RebuildDrawerFolders()
        {
            if (DrawerFoldersPanel == null || _viewModel == null || LeftDrawerOverlay?.IsVisible != true) return;
            DrawerFoldersPanel.Children.Clear();

            var allFolderPaths = DiscoverAllFolderPaths();
            if (allFolderPaths.Count == 0)
            {
                var emptyLabel = new TextBlock
                {
                    Text = "No folders created yet",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#6b7280")),
                    Margin = new Thickness(10, 8)
                };
                DrawerFoldersPanel.Children.Add(emptyLabel);
                return;
            }

            foreach (var folderPath in allFolderPaths)
            {
                var folderName = Path.GetFileName(folderPath);
                int depth = folderPath.Split('/').Length - 1;
                bool isSelectedCategory = string.Equals(_selectedCategory, folderName, StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(_selectedCategory, folderPath, StringComparison.OrdinalIgnoreCase);
                bool isFolderSelectedInBatch = _selectedFolders.Contains(folderPath);

                int noteCount = _viewModel.Notes.Count(n =>
                {
                    var f = Path.GetFileName(n.FolderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    return string.Equals(f, folderName, StringComparison.OrdinalIgnoreCase);
                });

                var folderRow = new Border
                {
                    Background = isFolderSelectedInBatch
                        ? (IsDarkMode ? new SolidColorBrush(Color.Parse("#1e293b")) : new SolidColorBrush(Color.Parse("#dbeafe")))
                        : (isSelectedCategory 
                            ? (IsDarkMode ? new SolidColorBrush(Color.Parse("#1a2230")) : new SolidColorBrush(Color.Parse("#e2e8f0"))) 
                            : Brushes.Transparent),
                    BorderBrush = isFolderSelectedInBatch 
                        ? AccentBrush 
                        : (isSelectedCategory ? GlassBorderBrush : Brushes.Transparent),
                    BorderThickness = new Thickness(isFolderSelectedInBatch || isSelectedCategory ? 1 : 0),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8),
                    Margin = new Thickness(depth * 14, 0, 0, 2),
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
                };

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                };

                // Check indicator (in selection mode) or folder icon
                Control leadingIcon;
                if (_isFolderSelectionMode)
                {
                    var checkBadge = new Border
                    {
                        Width = 18,
                        Height = 18,
                        CornerRadius = new CornerRadius(9),
                        Background = isFolderSelectedInBatch ? AccentBrush : Brushes.Transparent,
                        BorderBrush = isFolderSelectedInBatch ? AccentBrush : TextSecondaryBrush,
                        BorderThickness = new Thickness(1.5),
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                    };
                    if (isFolderSelectedInBatch)
                    {
                        checkBadge.Child = new PathIcon
                        {
                            Data = (StreamGeometry)Resources["IconCheck"]!,
                            Width = 10,
                            Height = 10,
                            Foreground = Brushes.White,
                            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                        };
                    }
                    leadingIcon = checkBadge;
                }
                else
                {
                    leadingIcon = new PathIcon
                    {
                        Data = (StreamGeometry)Resources["IconFolder"]!,
                        Width = 18,
                        Height = 18,
                        Foreground = isSelectedCategory ? AccentBrush : TextSecondaryBrush,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                    };
                }
                Grid.SetColumn(leadingIcon, 0);

                var folderText = new TextBlock
                {
                    Text = folderName,
                    FontSize = 14,
                    FontWeight = (isSelectedCategory || isFolderSelectedInBatch) ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = isFolderSelectedInBatch
                        ? AccentBrush
                        : (isSelectedCategory ? TextPrimaryBrush : TextSecondaryBrush),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(folderText, 1);

                if (_isFolderSelectionMode)
                {
                    // In selection mode: Direct Delete button on the right of the folder row
                    var deleteRowBtn = new Button
                    {
                        Background = Brushes.Transparent,
                        Padding = new Thickness(6, 4),
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                    };
                    deleteRowBtn.Content = new PathIcon
                    {
                        Data = (StreamGeometry)Resources["IconTrash"]!,
                        Width = 15,
                        Height = 15,
                        Foreground = new SolidColorBrush(Color.Parse("#ef4444"))
                    };
                    var capturedDelFolder = folderPath;
                    deleteRowBtn.Click += (s, e) =>
                    {
                        _selectedFolders.Clear();
                        _selectedFolders.Add(capturedDelFolder);
                        OnBatchDeleteFoldersClick(s, e);
                    };
                    Grid.SetColumn(deleteRowBtn, 2);
                    grid.Children.Add(deleteRowBtn);
                }
                else
                {
                    var badgeBorder = new Border
                    {
                        Background = ChipUnselectedBackgroundBrush,
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(8, 2),
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                    };
                    var badgeText = new TextBlock
                    {
                        Text = noteCount.ToString(),
                        FontSize = 11,
                        Foreground = TextSecondaryBrush
                    };
                    badgeBorder.Child = badgeText;
                    Grid.SetColumn(badgeBorder, 2);
                    grid.Children.Add(badgeBorder);
                }

                grid.Children.Add(leadingIcon);
                grid.Children.Add(folderText);

                folderRow.Child = grid;

                // Long press hold detection for folder selection
                Point? pressStart = null;
                var holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                bool wasLongPress = false;
                var capturedFolder = folderPath;

                folderRow.PointerPressed += (s, pe) =>
                {
                    if (_isFolderSelectionMode) return;
                    wasLongPress = false;
                    pressStart = pe.GetPosition(DrawerFoldersPanel);
                    holdTimer.Stop();
                    holdTimer.Start();
                };

                folderRow.PointerMoved += (s, pe) =>
                {
                    if (pressStart.HasValue)
                    {
                        var cur = pe.GetPosition(DrawerFoldersPanel);
                        if (Math.Abs(cur.X - pressStart.Value.X) > 12 || Math.Abs(cur.Y - pressStart.Value.Y) > 12)
                        {
                            holdTimer.Stop();
                            pressStart = null;
                        }
                    }
                };

                folderRow.PointerReleased += (s, pe) =>
                {
                    holdTimer.Stop();
                    pressStart = null;
                };

                folderRow.PointerCaptureLost += (s, pe) =>
                {
                    holdTimer.Stop();
                    pressStart = null;
                };

                holdTimer.Tick += (ts, te) =>
                {
                    holdTimer.Stop();
                    pressStart = null;
                    wasLongPress = true;
                    EnterFolderSelectionModeWithFolder(capturedFolder);
                };

                folderRow.Tapped += (s, e) =>
                {
                    if (wasLongPress)
                    {
                        wasLongPress = false;
                        return;
                    }

                    if (_isFolderSelectionMode)
                    {
                        if (_selectedFolders.Contains(capturedFolder))
                            _selectedFolders.Remove(capturedFolder);
                        else
                            _selectedFolders.Add(capturedFolder);

                        UpdateFolderSelectionState();
                        RebuildDrawerFolders();
                        return;
                    }

                    _selectedCategory = folderName;
                    if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
                    ApplyFilters();
                };

                DrawerFoldersPanel.Children.Add(folderRow);
            }
        }

        private void OnPromptDeleteFolder(string folderName)
        {
            _pendingDeleteFolderName = folderName;
            if (ConfirmDeleteFolderMessageText != null)
            {
                ConfirmDeleteFolderMessageText.Text = $"Delete folder '{folderName}' and move its notes to the Trash Bin?";
            }
            if (ConfirmDeleteFolderModal != null)
            {
                ConfirmDeleteFolderModal.IsVisible = true;
            }
        }

        private async void OnConfirmDeleteFolderClick(object? sender, RoutedEventArgs e)
        {
            var folderToDelete = _pendingDeleteFolderName;
            _pendingDeleteFolderName = null;
            if (ConfirmDeleteFolderModal != null) ConfirmDeleteFolderModal.IsVisible = false;

            if (string.IsNullOrEmpty(folderToDelete) || _viewModel == null) return;

            try
            {
                var root = _viewModel.NotesRoot;
                if (!string.IsNullOrEmpty(root))
                {
                    var folderPath = Path.Combine(root, folderToDelete);
                    if (Directory.Exists(folderPath))
                    {
                        await _viewModel.TrashBinService.MoveToTrashAsync(folderPath);
                    }

                    if (string.Equals(_selectedCategory, folderToDelete, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedCategory = "All Notes";
                    }

                    await _viewModel.RefreshFolderTreeAsync();
                    await _viewModel.Refresh();
                    RebuildTagChips();
                    if (LeftDrawerOverlay?.IsVisible == true)
                    {
                        RebuildDrawerFolders();
                        RebuildDrawerTags();
                        UpdateTrashBadge();
                    }
                    ApplyFilters();
                    OnPropertyChanged(nameof(TotalNotesCountText));
                    TriggerAutoSyncInBackground();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error deleting folder: {ex.Message}");
            }
        }

        private void OnCancelDeleteFolderClick(object? sender, RoutedEventArgs e)
        {
            if (ConfirmDeleteFolderModal != null) ConfirmDeleteFolderModal.IsVisible = false;
            _pendingDeleteFolderName = null;
        }

        // ── Tags Navigation & Drawer ──────────────────────────────────────
        private void RebuildDrawerTags()
        {
            if (DrawerTagsPanel == null || _viewModel?.Notes == null || LeftDrawerOverlay?.IsVisible != true) return;
            DrawerTagsPanel.Children.Clear();

            var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in _viewModel.Notes)
            {
                if (note.Tags != null)
                {
                    foreach (var t in note.Tags)
                    {
                        var clean = t.TrimStart('#').Trim();
                        if (!string.IsNullOrWhiteSpace(clean))
                        {
                            tagCounts[clean] = tagCounts.TryGetValue(clean, out var count) ? count + 1 : 1;
                        }
                    }
                }
            }

            if (tagCounts.Count == 0)
            {
                var emptyLabel = new TextBlock
                {
                    Text = "No tags found",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#6b7280")),
                    Margin = new Thickness(10, 8)
                };
                DrawerTagsPanel.Children.Add(emptyLabel);
                return;
            }

            foreach (var kvp in tagCounts.OrderBy(k => k.Key))
            {
                var tagName = kvp.Key;
                var count = kvp.Value;
                var isSelected = string.Equals(_selectedTag, tagName, StringComparison.OrdinalIgnoreCase);

                var btn = new Button
                {
                    Background = isSelected 
                        ? (IsDarkMode ? new SolidColorBrush(Color.Parse("#1a2230")) : new SolidColorBrush(Color.Parse("#e2e8f0")))
                        : Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8),
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
                };

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                };

                var tagIcon = new TextBlock
                {
                    Text = "#",
                    FontSize = 14,
                    FontWeight = FontWeight.Bold,
                    Foreground = isSelected ? AccentBrush : TextSecondaryBrush,
                    Margin = new Thickness(4, 0, 10, 0),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(tagIcon, 0);

                var tagText = new TextBlock
                {
                    Text = tagName,
                    FontSize = 14,
                    FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = isSelected ? TextPrimaryBrush : TextSecondaryBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(tagText, 1);

                var badgeBorder = new Border
                {
                    Background = ChipUnselectedBackgroundBrush,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                var badgeText = new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 11,
                    Foreground = TextSecondaryBrush
                };
                badgeBorder.Child = badgeText;
                Grid.SetColumn(badgeBorder, 2);

                grid.Children.Add(tagIcon);
                grid.Children.Add(tagText);
                grid.Children.Add(badgeBorder);

                btn.Content = grid;
                btn.Click += (s, e) =>
                {
                    SelectedTag = tagName;
                    if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
                };

                DrawerTagsPanel.Children.Add(btn);
            }
        }

        private void OnClearSelectedTagClick(object? sender, RoutedEventArgs e)
        {
            SelectedTag = null;
        }

        // ── In-App Trash Bin Handlers ─────────────────────────────────────
        private async void UpdateTrashBadge()
        {
            try
            {
                if (_viewModel?.TrashBinService != null && DrawerTrashBadgeText != null && LeftDrawerOverlay?.IsVisible == true)
                {
                    var items = await _viewModel.TrashBinService.GetTrashItemsAsync();
                    DrawerTrashBadgeText.Text = items.Count().ToString();
                }
            }
            catch { }
        }

        private async Task LoadTrashItemsAsync()
        {
            if (TrashItemsPanel == null || _viewModel?.TrashBinService == null) return;
            TrashItemsPanel.Children.Clear();

            try
            {
                var items = (await _viewModel.TrashBinService.GetTrashItemsAsync()).OrderByDescending(i => i.DeletedAt).ToList();

                if (DrawerTrashBadgeText != null)
                {
                    DrawerTrashBadgeText.Text = items.Count.ToString();
                }

                if (items.Count == 0)
                {
                    if (TrashEmptyState != null) TrashEmptyState.IsVisible = true;
                    return;
                }

                if (TrashEmptyState != null) TrashEmptyState.IsVisible = false;

                foreach (var item in items)
                {
                    var card = new Border
                    {
                        Background = new SolidColorBrush(Color.Parse("#1e1e24")),
                        BorderBrush = new SolidColorBrush(Color.Parse("#2e2e38")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(14, 12),
                        Margin = new Thickness(0, 0, 0, 4)
                    };

                    var grid = new Grid
                    {
                        RowDefinitions = new RowDefinitions("Auto,Auto"),
                        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                    };

                    var icon = new PathIcon
                    {
                        Data = (StreamGeometry)Resources[item.IsDirectory ? "IconFolder" : "IconDocument"]!,
                        Width = 20,
                        Height = 20,
                        Foreground = new SolidColorBrush(Color.Parse(item.IsDirectory ? "#3b82f6" : "#9ca3af")),
                        Margin = new Thickness(0, 2, 12, 0),
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top
                    };
                    Grid.SetRowSpan(icon, 2);
                    Grid.SetColumn(icon, 0);

                    var titleText = new TextBlock
                    {
                        Text = !string.IsNullOrWhiteSpace(item.Title) ? item.Title : Path.GetFileName(item.OriginalPath),
                        FontSize = 15,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Grid.SetRow(titleText, 0);
                    Grid.SetColumn(titleText, 1);

                    var pathText = new TextBlock
                    {
                        Text = $"{item.DeletedAt.ToLocalTime():MMM d, h:mm tt} • {Path.GetFileName(Path.GetDirectoryName(item.OriginalPath))}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.Parse("#9ca3af")),
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    Grid.SetRow(pathText, 1);
                    Grid.SetColumn(pathText, 1);

                    var actionsPanel = new StackPanel
                    {
                        Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8,
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                    };
                    Grid.SetRowSpan(actionsPanel, 2);
                    Grid.SetColumn(actionsPanel, 2);

                    var restoreBtn = new Button
                    {
                        Content = "Restore",
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        Background = new SolidColorBrush(Color.Parse("#2563eb")),
                        Foreground = Brushes.White,
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 6)
                    };
                    var capturedItem = item;
                    restoreBtn.Click += async (s, e) =>
                    {
                        await _viewModel.TrashBinService.RestoreItemAsync(capturedItem);
                        await _viewModel.RefreshFolderTreeAsync();
                        await _viewModel.Refresh();
                        RebuildTagChips();
                        RebuildDrawerFolders();
                        RebuildDrawerTags();
                        ApplyFilters();
                        OnPropertyChanged(nameof(TotalNotesCountText));
                        await LoadTrashItemsAsync();
                    };
                    actionsPanel.Children.Add(restoreBtn);

                    var permDelBtn = new Button
                    {
                        Background = Brushes.Transparent,
                        Foreground = new SolidColorBrush(Color.Parse("#ef4444")),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(6)
                    };
                    permDelBtn.Content = new PathIcon
                    {
                        Data = (StreamGeometry)Resources["IconTrash"]!,
                        Width = 16,
                        Height = 16,
                        Foreground = new SolidColorBrush(Color.Parse("#ef4444"))
                    };
                    permDelBtn.Click += async (s, e) =>
                    {
                        await _viewModel.TrashBinService.PermanentDeleteAsync(capturedItem);
                        await LoadTrashItemsAsync();
                    };
                    actionsPanel.Children.Add(permDelBtn);

                    grid.Children.Add(icon);
                    grid.Children.Add(titleText);
                    grid.Children.Add(pathText);
                    grid.Children.Add(actionsPanel);

                    card.Child = grid;
                    TrashItemsPanel.Children.Add(card);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error loading trash items: {ex.Message}");
            }
        }

        private void OnPromptEmptyTrashClick(object? sender, RoutedEventArgs e)
        {
            if (ConfirmEmptyTrashModal != null)
            {
                ConfirmEmptyTrashModal.IsVisible = true;
            }
        }

        private async void OnConfirmEmptyTrashClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.TrashBinService != null)
            {
                await _viewModel.TrashBinService.EmptyTrashAsync();
                await LoadTrashItemsAsync();
                await _viewModel.RefreshFolderTreeAsync();
                await _viewModel.Refresh();
                RebuildTagChips();
                RebuildDrawerFolders();
                RebuildDrawerTags();
                ApplyFilters();
            }
            if (ConfirmEmptyTrashModal != null)
            {
                ConfirmEmptyTrashModal.IsVisible = false;
            }
        }

        private void OnCancelEmptyTrashClick(object? sender, RoutedEventArgs e)
        {
            if (ConfirmEmptyTrashModal != null)
            {
                ConfirmEmptyTrashModal.IsVisible = false;
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
                    if (currentIndex != i)
                    {
                        FilteredNotes.Move(currentIndex, i);
                    }
                    existingItem.IsSelectionMode = _isSelectionMode;
                    existingItem.Refresh();
                }
                else
                {
                    var newItem = new MobileNoteItem(note, _isDarkMode)
                    {
                        IsSelectionMode = _isSelectionMode
                    };
                    FilteredNotes.Insert(i, newItem);
                }
            }

            OnPropertyChanged(nameof(NoteCountStatus));
            OnPropertyChanged(nameof(IsNotesEmpty));
            UpdateSelectionState();
        }

        private static string GetNoteKey(NoteDocument note)
        {
            if (note.Id != Guid.Empty) return note.Id.ToString();
            return !string.IsNullOrEmpty(note.FullPath) ? note.FullPath : (note.FileName ?? note.Title ?? "");
        }

        private void OnClearSearchClick(object? sender, RoutedEventArgs e)
        {
            SearchQuery = string.Empty;
        }

        // ── Note Long-Press & Multi-Select Handlers (Issue 1) ─────────────
        private DispatcherTimer? _noteHoldTimer;
        private bool _wasNoteLongPress = false;

        private void OnNoteCardPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (IsSelectionMode) return;
            _wasNoteLongPress = false;
            _noteHoldTimer?.Stop();
            _noteHoldTimer = null;

            if (sender is Control control && control.DataContext is MobileNoteItem item)
            {
                _noteHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                _noteHoldTimer.Tick += (s, ev) =>
                {
                    _noteHoldTimer?.Stop();
                    _noteHoldTimer = null;
                    _wasNoteLongPress = true;
                    EnterSelectionModeWithNote(item);
                };
                _noteHoldTimer.Start();
            }
        }

        private void OnNoteCardPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _noteHoldTimer?.Stop();
            _noteHoldTimer = null;
        }

        private void OnNoteCardPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _noteHoldTimer?.Stop();
            _noteHoldTimer = null;
        }

        private void EnterSelectionModeWithNote(MobileNoteItem item)
        {
            IsSelectionMode = true;
            item.IsSelected = true;
            UpdateSelectionState();
        }

        private void ExitSelectionMode()
        {
            IsSelectionMode = false;
            foreach (var n in FilteredNotes)
            {
                n.IsSelected = false;
            }
            UpdateSelectionState();
        }

        private void UpdateSelectionState()
        {
            OnPropertyChanged(nameof(SelectionCountText));
            OnPropertyChanged(nameof(SelectAllButtonText));
        }

        private void OnCancelSelectionClick(object? sender, RoutedEventArgs e)
        {
            ExitSelectionMode();
        }

        private void OnToggleSelectAllNotesClick(object? sender, RoutedEventArgs e)
        {
            bool allSelected = FilteredNotes.Count > 0 && FilteredNotes.All(n => n.IsSelected);
            foreach (var n in FilteredNotes)
            {
                n.IsSelected = !allSelected;
            }
            UpdateSelectionState();
        }

        private void OnNoteCardTapped(object? sender, TappedEventArgs e)
        {
            if (_wasNoteLongPress)
            {
                _wasNoteLongPress = false;
                return;
            }

            if ((sender as Control)?.DataContext is MobileNoteItem item)
            {
                if (IsSelectionMode)
                {
                    item.IsSelected = !item.IsSelected;
                    UpdateSelectionState();
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

        // ── Batch Note Operations (Move, Copy, Delete) ─────────────────────
        private void OnBatchMoveNotesClick(object? sender, RoutedEventArgs e)
        {
            var selectedNotes = FilteredNotes.Where(n => n.IsSelected).ToList();
            if (selectedNotes.Count == 0) return;

            if (MoveNotesModalTitle != null)
            {
                MoveNotesModalTitle.Text = $"Move {selectedNotes.Count} Note(s)";
            }
            if (MoveNotesModal != null)
            {
                MoveNotesModal.IsVisible = true;
                PopulateMoveNotesDestinations(null);
            }
        }

        private void OnCancelMoveNotesClick(object? sender, RoutedEventArgs e)
        {
            if (MoveNotesModal != null) MoveNotesModal.IsVisible = false;
        }

        private void OnMoveNotesSearchKeyUp(object? sender, KeyEventArgs e)
        {
            PopulateMoveNotesDestinations(MoveNotesSearchBox?.Text);
        }

        private void PopulateMoveNotesDestinations(string? filter)
        {
            if (MoveNotesDestinationsPanel == null) return;
            MoveNotesDestinationsPanel.Children.Clear();

            var cleanFilter = filter?.Trim() ?? string.Empty;

            // 1. Root option
            if (string.IsNullOrEmpty(cleanFilter) || "root top level".Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
            {
                var rootBtn = CreateMoveNoteDestinationButton(null, "/ (Root - Top level)");
                MoveNotesDestinationsPanel.Children.Add(rootBtn);
            }

            // 2. Discovered folders
            var allFolders = DiscoverAllFolderPaths();
            foreach (var folder in allFolders)
            {
                if (string.IsNullOrEmpty(cleanFilter) || folder.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
                {
                    var btn = CreateMoveNoteDestinationButton(folder, folder);
                    MoveNotesDestinationsPanel.Children.Add(btn);
                }
            }
        }

        private Button CreateMoveNoteDestinationButton(string? targetRelPath, string displayTitle)
        {
            var btn = new Button
            {
                Background = new SolidColorBrush(Color.Parse("#181820")),
                BorderBrush = new SolidColorBrush(Color.Parse("#2e2e38")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var icon = new PathIcon
            {
                Data = (StreamGeometry)Resources["IconFolder"]!,
                Width = 16,
                Height = 16,
                Foreground = new SolidColorBrush(Color.Parse("#3b82f6")),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var text = new TextBlock
            {
                Text = displayTitle,
                FontSize = 13,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(text, 1);

            grid.Children.Add(icon);
            grid.Children.Add(text);
            btn.Content = grid;

            btn.Click += async (s, e) =>
            {
                try
                {
                    var root = _viewModel?.NotesRoot;
                    if (!string.IsNullOrEmpty(root) && _viewModel != null)
                    {
                        var destDir = string.IsNullOrEmpty(targetRelPath)
                            ? root
                            : Path.Combine(root, targetRelPath.Replace('/', Path.DirectorySeparatorChar));

                        if (!Directory.Exists(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }

                        var selectedItems = FilteredNotes.Where(n => n.IsSelected).ToList();
                        foreach (var item in selectedItems)
                        {
                            var srcFile = item.Note.FullPath;
                            if (File.Exists(srcFile))
                            {
                                var fileName = Path.GetFileName(srcFile);
                                var targetFile = Path.Combine(destDir, fileName);

                                if (!string.Equals(srcFile, targetFile, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (File.Exists(targetFile))
                                    {
                                        var baseName = Path.GetFileNameWithoutExtension(fileName);
                                        var ext = Path.GetExtension(fileName);
                                        int c = 1;
                                        while (File.Exists(Path.Combine(destDir, $"{baseName} ({c}){ext}"))) c++;
                                        targetFile = Path.Combine(destDir, $"{baseName} ({c}){ext}");
                                    }
                                    File.Move(srcFile, targetFile);

                                    item.Note.FullPath = targetFile;
                                    item.Note.FolderPath = destDir;
                                    item.Note.RelativePath = Path.GetRelativePath(root, targetFile);
                                }
                            }
                        }

                        if (MoveNotesModal != null) MoveNotesModal.IsVisible = false;
                        ExitSelectionMode();

                        await _viewModel.RefreshFolderTreeAsync();
                        await _viewModel.Refresh();
                        RebuildDrawerFolders();
                        ApplyFilters();
                        TriggerAutoSyncInBackground();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MobileMainView] Error moving notes: {ex.Message}");
                    if (MoveNotesModal != null) MoveNotesModal.IsVisible = false;
                }
            };

            return btn;
        }

        private async void OnBatchCopyNotesClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItems = FilteredNotes.Where(n => n.IsSelected).ToList();
                if (selectedItems.Count == 0 || _viewModel == null) return;

                var root = _viewModel.NotesRoot;
                foreach (var item in selectedItems)
                {
                    var src = item.Note.FullPath;
                    if (File.Exists(src))
                    {
                        var dir = Path.GetDirectoryName(src) ?? root;
                        var baseName = Path.GetFileNameWithoutExtension(src);
                        var ext = Path.GetExtension(src);

                        string copyPath = Path.Combine(dir, $"{baseName} (Copy){ext}");
                        int c = 2;
                        while (File.Exists(copyPath))
                        {
                            copyPath = Path.Combine(dir, $"{baseName} (Copy {c}){ext}");
                            c++;
                        }
                        File.Copy(src, copyPath);
                    }
                }

                ExitSelectionMode();
                await _viewModel.RefreshFolderTreeAsync();
                await _viewModel.Refresh();
                RebuildDrawerFolders();
                ApplyFilters();
                TriggerAutoSyncInBackground();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error copying notes: {ex.Message}");
            }
        }

        private void OnBatchDeleteNotesClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItems = FilteredNotes.Where(n => n.IsSelected).ToList();
                if (selectedItems.Count == 0 || _viewModel == null) return;

                // Optimistic UI update: Remove from UI and ViewModel immediately (0ms)
                foreach (var item in selectedItems)
                {
                    FilteredNotes.Remove(item);
                    _viewModel.Notes.Remove(item.Note);
                }

                ExitSelectionMode();
                OnPropertyChanged(nameof(NoteCountStatus));
                OnPropertyChanged(nameof(IsNotesEmpty));
                OnPropertyChanged(nameof(TotalNotesCountText));

                var pathsToDelete = selectedItems.Select(i => i.Note.FullPath).Where(p => !string.IsNullOrEmpty(p)).ToList();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var path in pathsToDelete)
                        {
                            if (File.Exists(path))
                            {
                                await _viewModel.TrashBinService.MoveToTrashAsync(path);
                            }
                        }
                        await _viewModel.RefreshFolderTreeAsync();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (LeftDrawerOverlay?.IsVisible == true)
                            {
                                RebuildDrawerFolders();
                                RebuildDrawerTags();
                                UpdateTrashBadge();
                            }
                            TriggerAutoSyncInBackground();
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MobileMainView] Error batch deleting notes in background: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error batch deleting notes: {ex.Message}");
            }
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

            ActiveNoteTitle = _viewModel.CurrentNote?.Title ?? "Untitled Note";
            ShowNoteEditor();
        }

        private void OnDeleteNoteCardClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is MobileNoteItem item && _viewModel != null)
            {
                // Optimistic UI update: Card vanishes immediately (0ms)
                FilteredNotes.Remove(item);
                _viewModel.Notes.Remove(item.Note);
                OnPropertyChanged(nameof(NoteCountStatus));
                OnPropertyChanged(nameof(IsNotesEmpty));
                OnPropertyChanged(nameof(TotalNotesCountText));

                var noteToDelete = item.Note;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (File.Exists(noteToDelete.FullPath))
                        {
                            await _viewModel.TrashBinService.MoveToTrashAsync(noteToDelete.FullPath);
                        }
                        await _viewModel.RefreshFolderTreeAsync();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (LeftDrawerOverlay?.IsVisible == true)
                            {
                                RebuildDrawerFolders();
                                RebuildDrawerTags();
                                UpdateTrashBadge();
                            }
                            TriggerAutoSyncInBackground();
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MobileMainView] Background delete note error: {ex.Message}");
                    }
                });
            }
        }

        private void OnDeleteCurrentNoteClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                var current = _viewModel.CurrentNote;
                if (current != null)
                {
                    var match = FilteredNotes.FirstOrDefault(n => n.Note.Id == current.Id || n.Note.FullPath == current.FullPath);
                    if (match != null) FilteredNotes.Remove(match);
                    _viewModel.Notes.Remove(current);
                }
                ShowNotesFeed();
                OnPropertyChanged(nameof(TotalNotesCountText));
                OnPropertyChanged(nameof(NoteCountStatus));
                OnPropertyChanged(nameof(IsNotesEmpty));

                var pathToDelete = current?.FullPath;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(pathToDelete) && File.Exists(pathToDelete))
                        {
                            await _viewModel.TrashBinService.MoveToTrashAsync(pathToDelete);
                        }
                        await _viewModel.RefreshFolderTreeAsync();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (LeftDrawerOverlay?.IsVisible == true)
                            {
                                RebuildDrawerFolders();
                                RebuildDrawerTags();
                                UpdateTrashBadge();
                            }
                            TriggerAutoSyncInBackground();
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MobileMainView] Background delete current note error: {ex.Message}");
                    }
                });
            }
        }

        private async void OnSyncClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                if (_viewModel.CloudSyncService != null)
                {
                    await _viewModel.CloudSyncService.TriggerSyncAsync();
                }
                await _viewModel.Refresh();
                RebuildTagChips();
                ApplyFilters();
                UpdateModalAccountStatus();
            }
        }

        // ── Hardware Back Button ──────────────────────────────────────────
        private void OnViewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back || e.Key == Key.Escape)
            {
                if (ConfirmBatchDeleteFoldersModal?.IsVisible == true)
                {
                    e.Handled = true;
                    ConfirmBatchDeleteFoldersModal.IsVisible = false;
                }
                else if (MoveFoldersModal?.IsVisible == true)
                {
                    e.Handled = true;
                    MoveFoldersModal.IsVisible = false;
                }
                else if (MoveNotesModal?.IsVisible == true)
                {
                    e.Handled = true;
                    MoveNotesModal.IsVisible = false;
                }
                else if (ParentFolderDropdownContainer?.IsVisible == true)
                {
                    e.Handled = true;
                    ParentFolderDropdownContainer.IsVisible = false;
                }
                else if (ConfirmDeleteFolderModal?.IsVisible == true)
                {
                    e.Handled = true;
                    ConfirmDeleteFolderModal.IsVisible = false;
                    _pendingDeleteFolderName = null;
                }
                else if (ConfirmEmptyTrashModal?.IsVisible == true)
                {
                    e.Handled = true;
                    ConfirmEmptyTrashModal.IsVisible = false;
                }
                else if (TrashBinScreen?.IsVisible == true)
                {
                    e.Handled = true;
                    TrashBinScreen.IsVisible = false;
                }
                else if (_isFolderSelectionMode)
                {
                    e.Handled = true;
                    ExitFolderSelectionMode();
                }
                else if (LeftDrawerOverlay?.IsVisible == true)
                {
                    e.Handled = true;
                    LeftDrawerOverlay.IsVisible = false;
                }
                else if (IsSelectionMode)
                {
                    e.Handled = true;
                    ExitSelectionMode();
                }
                else if (NoteEditorScreen.IsVisible)
                {
                    e.Handled = true;
                    _ = SaveAndCloseEditorAsync();
                }
            }
        }

        // ── Mobile Formatting Toolbar Actions ─────────────────────────────
        private async void OnFormatH1Click(object? sender, RoutedEventArgs e) => await ExecFormat("h1");
        private async void OnFormatH2Click(object? sender, RoutedEventArgs e) => await ExecFormat("h2");
        private async void OnFormatBoldClick(object? sender, RoutedEventArgs e) => await ExecFormat("bold");
        private async void OnFormatItalicClick(object? sender, RoutedEventArgs e) => await ExecFormat("italic");
        private async void OnFormatBulletClick(object? sender, RoutedEventArgs e) => await ExecFormat("bullet");
        private async void OnFormatNumberedClick(object? sender, RoutedEventArgs e) => await ExecFormat("number");
        private async void OnFormatTodoClick(object? sender, RoutedEventArgs e) => await ExecFormat("todo");
        private async void OnFormatCodeClick(object? sender, RoutedEventArgs e) => await ExecFormat("code");
        private async void OnFormatQuoteClick(object? sender, RoutedEventArgs e) => await ExecFormat("quote");

        private async Task ExecFormat(string action)
        {
            if (MobileNotionEditor != null)
            {
                await MobileNotionEditor.ExecuteEditorActionAsync(action);
                await MobileNotionEditor.FocusEditorAsync();
            }
        }

        // ── Google Drive Cloud Sync Modals ────────────────────────────────
        private string? _pendingCodeVerifier;

        public void OnCloudSyncBadgeTapped(object? sender, TappedEventArgs e)
        {
            UpdateModalAccountStatus();
            GoogleDriveAccountModal.IsVisible = true;
        }

        private void UpdateModalAccountStatus()
        {
            bool signedIn = IsSignedInToGoogle;
            BtnConnectGoogle.IsVisible = !signedIn;
            BtnSyncNow.IsVisible = signedIn;
            BtnDisconnectGoogle.IsVisible = signedIn;
            OnPropertyChanged(nameof(ConnectedAccountEmail));
            OnPropertyChanged(nameof(LastSyncTimeText));
            OnPropertyChanged(nameof(CloudSyncStatusText));
            OnPropertyChanged(nameof(IsSignedInToGoogle));
            OnPropertyChanged(nameof(CloudSyncIconBrush));
        }

        private void OnCloseAccountModalClick(object? sender, RoutedEventArgs e)
        {
            GoogleDriveAccountModal.IsVisible = false;
        }

        private void OnConnectGoogleClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.CloudSyncService == null) return;

            GoogleDriveAccountModal.IsVisible = false;
            var url = _viewModel.CloudSyncService.GetAuthorizationUrl(out _pendingCodeVerifier);
            if (!string.IsNullOrEmpty(url))
            {
                GoogleDriveAuthModal.IsVisible = true;
                AuthWebView.Navigate(new Uri(url));
            }
        }

        private void OnCloseAuthModalClick(object? sender, RoutedEventArgs e)
        {
            GoogleDriveAuthModal.IsVisible = false;
        }

        private void OnAuthWebViewNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
        {
            try
            {
                if (e.Request != null)
                {
                    var url = e.Request.ToString();
                    Console.WriteLine($"[MobileMainView] Auth NavigationStarting: {url}");
                    if (url.Contains("code=") || url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
                    {
                        var code = ExtractQueryParam(url, "code");
                        if (!string.IsNullOrEmpty(code))
                        {
                            Console.WriteLine($"[MobileMainView] Intercepted OAuth code in NavigationStarting: {code.Substring(0, Math.Min(code.Length, 10))}...");
                            e.Cancel = true;
                            GoogleDriveAuthModal.IsVisible = false;
                            _ = HandleAuthCodeAsync(code);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Auth navigation starting error: {ex.Message}");
            }
        }

        private async void OnAuthWebViewNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
        {
            try
            {
                if (e.Request != null)
                {
                    var url = e.Request.ToString();
                    Console.WriteLine($"[MobileMainView] Auth NavigationCompleted: {url} (Success: {e.IsSuccess})");
                    if (url.Contains("code="))
                    {
                        var code = ExtractQueryParam(url, "code");
                        if (!string.IsNullOrEmpty(code))
                        {
                            Console.WriteLine($"[MobileMainView] Intercepted OAuth code in NavigationCompleted: {code.Substring(0, Math.Min(code.Length, 10))}...");
                            GoogleDriveAuthModal.IsVisible = false;
                            _ = HandleAuthCodeAsync(code);
                            return;
                        }
                    }
                }

                var href = await AuthWebView.InvokeScript("window.location.href");
                if (!string.IsNullOrEmpty(href))
                {
                    Console.WriteLine($"[MobileMainView] Auth WebView window.location.href: {href}");
                    if (href.Contains("code="))
                    {
                        var code = ExtractQueryParam(href, "code");
                        if (!string.IsNullOrEmpty(code))
                        {
                            Console.WriteLine($"[MobileMainView] Intercepted OAuth code from JS href: {code.Substring(0, Math.Min(code.Length, 10))}...");
                            GoogleDriveAuthModal.IsVisible = false;
                            _ = HandleAuthCodeAsync(code);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Auth navigation completed error: {ex.Message}");
            }
        }

        private async Task HandleAuthCodeAsync(string code)
        {
            try
            {
                Console.WriteLine($"[MobileMainView] HandleAuthCodeAsync starting...");
                var verifier = _pendingCodeVerifier;
                _pendingCodeVerifier = null;

                if (!string.IsNullOrEmpty(verifier) && _viewModel?.CloudSyncService != null)
                {
                    var success = await _viewModel.CloudSyncService.CompleteSignInAsync(code, verifier);
                    Console.WriteLine($"[MobileMainView] CompleteSignInAsync result: {success}");
                    await _viewModel.Refresh();
                    UpdateModalAccountStatus();
                    RebuildTagChips();
                    RebuildDrawerFolders();
                    ApplyFilters();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] HandleAuthCodeAsync error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private async void OnSyncNowClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.CloudSyncService != null)
            {
                Console.WriteLine($"[MobileMainView] OnSyncNowClick triggered");
                var result = await _viewModel.CloudSyncService.TriggerSyncAsync();
                Console.WriteLine($"[MobileMainView] TriggerSyncAsync result: {result.Message}");
                await _viewModel.Refresh();
                UpdateModalAccountStatus();
                RebuildTagChips();
                RebuildDrawerFolders();
                ApplyFilters();
            }
        }

        private async void OnDisconnectGoogleClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.CloudSyncService != null)
            {
                await _viewModel.CloudSyncService.SignOutAsync();
                UpdateModalAccountStatus();
                ApplyFilters();
            }
        }

        private static string? ExtractQueryParam(string url, string paramName)
        {
            try
            {
                var qIndex = url.IndexOf('?');
                if (qIndex < 0) return null;
                var query = url.Substring(qIndex + 1);
                var hashIndex = query.IndexOf('#');
                if (hashIndex >= 0) query = query.Substring(0, hashIndex);
                var pairs = query.Split('&');
                foreach (var p in pairs)
                {
                    var parts = p.Split(new[] { '=' }, 2);
                    if (parts.Length == 2 && string.Equals(parts[0], paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        return Uri.UnescapeDataString(parts[1]);
                    }
                }
            }
            catch { }
            return null;
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
