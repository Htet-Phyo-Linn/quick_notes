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
using Avalonia.Threading;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.Views
{
    public record KeepColor(string Name, string Background, string Border, string Accent, string Text);

    public static class KeepPalette
    {
        public static readonly KeepColor[] Colors = new[]
        {
            new KeepColor("Default", "#202124", "#3c4043", "#9aa0a6", "#e8eaed"),
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

        public static KeepColor GetColor(string? nameOrId)
        {
            if (!string.IsNullOrWhiteSpace(nameOrId))
            {
                var match = Colors.FirstOrDefault(c => string.Equals(c.Name, nameOrId, StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(c.Background, nameOrId, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            int idx = Math.Abs((nameOrId ?? "default").GetHashCode()) % Colors.Length;
            return Colors[idx];
        }
    }

    public class MobileNoteItem : INotifyPropertyChanged
    {
        public NoteDocument Note { get; }
        private readonly KeepColor _color;

        public MobileNoteItem(NoteDocument note)
        {
            Note = note;
            _color = ResolveColor(note);
            CardBackground = new SolidColorBrush(Color.Parse(_color.Background));
            CardBorderBrush = new SolidColorBrush(Color.Parse(_color.Border));
            CardAccentBrush = new SolidColorBrush(Color.Parse(_color.Accent));
            CardTextBrush = new SolidColorBrush(Color.Parse(_color.Text));
        }

        private static KeepColor ResolveColor(NoteDocument note)
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
                            return KeepPalette.GetColor(parts[1].Trim());
                        }
                    }
                }
            }

            var seed = note.Id != Guid.Empty ? note.Id.ToString() : (note.FileName ?? note.Title ?? "note");
            return KeepPalette.GetColor(seed);
        }

        public IBrush CardBackground { get; }
        public IBrush CardBorderBrush { get; }
        public IBrush CardAccentBrush { get; }
        public IBrush CardTextBrush { get; }
        public string ColorName => _color.Name;

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
            ? Note.LastModified.ToString("MMM d")
            : "Just now";

        public string[] Tags => Note?.Tags ?? Array.Empty<string>();
        public bool HasTags => Tags != null && Tags.Length > 0;

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
        public string LastSyncTimeText => _viewModel?.CloudSyncService?.LastSyncTime?.ToString("MMM d, h:mm tt") ?? "Never";
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
            if (AuthWebView != null)
            {
                AuthWebView.NavigationStarted += OnAuthWebViewNavigationStarted;
                AuthWebView.NavigationCompleted += OnAuthWebViewNavigationCompleted;
            }
            DataContextChanged += OnDataContextChanged;
            KeyDown += OnViewKeyDown;
            InitColorPaletteDrawer();
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

                RebuildCategoryChips();
                ApplyFilters();
                ShowNotesFeed();
            }
        }

        private void OnNotesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RebuildCategoryChips();
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
            }
            ShowNotesFeed();
        }

        // ── Folder Discovery & Navigation ──────────────────────────────
        private HashSet<string> DiscoverAllFolders()
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Enumerate on-disk directories under NotesRoot
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

            // 2. Include folders from existing notes
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

        // ── Category Chips & Note Filtering ──────────────────────────────
        private void RebuildCategoryChips()
        {
            if (CategoryChipsPanel == null || _viewModel == null) return;

            CategoryChipsPanel.Children.Clear();

            var categories = new List<string> { "All Notes" };
            var folderNames = DiscoverAllFolders().OrderBy(f => f);

            foreach (var f in folderNames)
            {
                if (f != null) categories.Add(f);
            }

            foreach (var cat in categories)
            {
                var isSelected = string.Equals(_selectedCategory, cat, StringComparison.OrdinalIgnoreCase);
                var btn = new Button
                {
                    Content = cat,
                    FontSize = 13,
                    FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(14, 6),
                    Background = isSelected ? new SolidColorBrush(Color.Parse("#2563eb")) : new SolidColorBrush(Color.Parse("#1e1e24")),
                    Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.Parse("#9ca3af")),
                    BorderThickness = new Thickness(0),
                    HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Tag = cat
                };
                btn.Click += (s, e) =>
                {
                    _selectedCategory = cat;
                    RebuildCategoryChips();
                    RebuildDrawerFolders();
                    ApplyFilters();
                };
                CategoryChipsPanel.Children.Add(btn);
            }

            // Add + Folder chip at the end
            var addFolderBtn = new Button
            {
                Content = "+ Folder",
                FontSize = 13,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(12, 6),
                Background = new SolidColorBrush(Color.Parse("#1e1e24")),
                Foreground = new SolidColorBrush(Color.Parse("#3b82f6")),
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            addFolderBtn.Click += (s, e) =>
            {
                ShowNewFolderDialog();
            };
            CategoryChipsPanel.Children.Add(addFolderBtn);
        }

        // ── New Folder Dialog Handlers ────────────────────────────────────
        private void ShowNewFolderDialog()
        {
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

        private async void OnConfirmNewFolderClick(object? sender, RoutedEventArgs e)
        {
            var folderName = NewFolderNameTextBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(folderName))
            {
                if (NewFolderModal != null) NewFolderModal.IsVisible = false;
                return;
            }

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
                    var targetDir = Path.Combine(root, folderName);
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    if (_viewModel != null)
                    {
                        await _viewModel.RefreshFolderTreeAsync();
                    }
                    _selectedCategory = folderName;
                    if (NewFolderModal != null) NewFolderModal.IsVisible = false;
                    RebuildCategoryChips();
                    RebuildDrawerFolders();
                    ApplyFilters();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Failed creating folder '{folderName}': {ex.Message}");
                if (NewFolderModal != null) NewFolderModal.IsVisible = false;
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
            if (LeftDrawerOverlay != null)
            {
                LeftDrawerOverlay.IsVisible = false;
            }
        }

        private void OnDrawerAllNotesClick(object? sender, RoutedEventArgs e)
        {
            _selectedCategory = "All Notes";
            _selectedTag = null;
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
            RebuildCategoryChips();
            RebuildDrawerFolders();
            RebuildDrawerTags();
            OnPropertyChanged(nameof(SelectedTag));
            OnPropertyChanged(nameof(HasSelectedTag));
            OnPropertyChanged(nameof(SelectedTagText));
            ApplyFilters();
        }

        private void OnDrawerTrashBinClick(object? sender, RoutedEventArgs e)
        {
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
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

        private void RebuildDrawerFolders()
        {
            if (DrawerFoldersPanel == null || _viewModel == null) return;
            DrawerFoldersPanel.Children.Clear();

            var allFolders = DiscoverAllFolders().OrderBy(f => f).ToList();
            if (allFolders.Count == 0)
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

            foreach (var folder in allFolders)
            {
                var isSelected = string.Equals(_selectedCategory, folder, StringComparison.OrdinalIgnoreCase);
                int noteCount = _viewModel.Notes.Count(n =>
                {
                    var f = Path.GetFileName(n.FolderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    return string.Equals(f, folder, StringComparison.OrdinalIgnoreCase);
                });

                var btn = new Button
                {
                    Background = isSelected ? new SolidColorBrush(Color.Parse("#252830")) : Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8),
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
                };

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto")
                };

                var folderIcon = new PathIcon
                {
                    Data = (StreamGeometry)Resources["IconFolder"]!,
                    Width = 18,
                    Height = 18,
                    Foreground = isSelected ? new SolidColorBrush(Color.Parse("#3b82f6")) : new SolidColorBrush(Color.Parse("#9ca3af")),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(folderIcon, 0);

                var folderText = new TextBlock
                {
                    Text = folder,
                    FontSize = 14,
                    FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.Parse("#d1d5db")),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(folderText, 1);

                var badgeBorder = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#282832")),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                var badgeText = new TextBlock
                {
                    Text = noteCount.ToString(),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse("#9ca3af"))
                };
                badgeBorder.Child = badgeText;
                Grid.SetColumn(badgeBorder, 2);

                var deleteFolderBtn = new Button
                {
                    Background = Brushes.Transparent,
                    Padding = new Thickness(6, 2),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                deleteFolderBtn.Content = new PathIcon
                {
                    Data = (StreamGeometry)Resources["IconTrash"]!,
                    Width = 14,
                    Height = 14,
                    Foreground = new SolidColorBrush(Color.Parse("#ef4444"))
                };
                var capturedFolder = folder;
                deleteFolderBtn.Click += (s, e) =>
                {
                    OnPromptDeleteFolder(capturedFolder);
                };
                Grid.SetColumn(deleteFolderBtn, 3);

                grid.Children.Add(folderIcon);
                grid.Children.Add(folderText);
                grid.Children.Add(badgeBorder);
                grid.Children.Add(deleteFolderBtn);

                btn.Content = grid;
                btn.Click += (s, e) =>
                {
                    _selectedCategory = folder;
                    if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
                    RebuildCategoryChips();
                    RebuildDrawerFolders();
                    ApplyFilters();
                };

                DrawerFoldersPanel.Children.Add(btn);
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
            if (string.IsNullOrEmpty(_pendingDeleteFolderName))
            {
                if (ConfirmDeleteFolderModal != null) ConfirmDeleteFolderModal.IsVisible = false;
                return;
            }

            try
            {
                var root = _viewModel?.NotesRoot;
                if (!string.IsNullOrEmpty(root) && _viewModel != null)
                {
                    var folderPath = Path.Combine(root, _pendingDeleteFolderName);
                    if (Directory.Exists(folderPath))
                    {
                        await _viewModel.TrashBinService.MoveToTrashAsync(folderPath);
                    }

                    if (string.Equals(_selectedCategory, _pendingDeleteFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedCategory = "All Notes";
                    }

                    await _viewModel.RefreshFolderTreeAsync();
                    await _viewModel.Refresh();
                    RebuildCategoryChips();
                    RebuildDrawerFolders();
                    RebuildDrawerTags();
                    UpdateTrashBadge();
                    ApplyFilters();
                    OnPropertyChanged(nameof(TotalNotesCountText));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error deleting folder: {ex.Message}");
            }
            finally
            {
                if (ConfirmDeleteFolderModal != null) ConfirmDeleteFolderModal.IsVisible = false;
                _pendingDeleteFolderName = null;
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
            if (DrawerTagsPanel == null || _viewModel?.Notes == null) return;
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
                    Background = isSelected ? new SolidColorBrush(Color.Parse("#252830")) : Brushes.Transparent,
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
                    Foreground = isSelected ? new SolidColorBrush(Color.Parse("#3b82f6")) : new SolidColorBrush(Color.Parse("#9ca3af")),
                    Margin = new Thickness(4, 0, 10, 0),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(tagIcon, 0);

                var tagText = new TextBlock
                {
                    Text = tagName,
                    FontSize = 14,
                    FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.Parse("#d1d5db")),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(tagText, 1);

                var badgeBorder = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#282832")),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                var badgeText = new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse("#9ca3af"))
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
                if (_viewModel?.TrashBinService != null && DrawerTrashBadgeText != null)
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
                        Text = $"{item.DeletedAt:MMM d, h:mm tt} • {Path.GetFileName(Path.GetDirectoryName(item.OriginalPath))}",
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
                        RebuildCategoryChips();
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
                RebuildCategoryChips();
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

            FilteredNotes.Clear();
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

            // Sort: newest first
            foreach (var note in source.OrderByDescending(n => n.LastModified))
            {
                FilteredNotes.Add(new MobileNoteItem(note));
            }

            OnPropertyChanged(nameof(NoteCountStatus));
            OnPropertyChanged(nameof(IsNotesEmpty));
        }

        private void OnClearSearchClick(object? sender, RoutedEventArgs e)
        {
            SearchQuery = string.Empty;
        }

        private void OnNoteCardTapped(object? sender, TappedEventArgs e)
        {
            if ((sender as Control)?.DataContext is MobileNoteItem item && _viewModel != null)
            {
                _viewModel.SelectNote(item.Note);
                ActiveNoteTitle = item.Note.Title;
                ShowNoteEditor();
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

        private async void OnDeleteNoteCardClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is MobileNoteItem item && _viewModel != null)
            {
                await _viewModel.DeleteNoteAsync(item.Note);
                RebuildDrawerFolders();
                RebuildDrawerTags();
                UpdateTrashBadge();
                ApplyFilters();
                OnPropertyChanged(nameof(TotalNotesCountText));
            }
        }

        private async void OnDeleteCurrentNoteClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                await _viewModel.Delete();
                RebuildDrawerFolders();
                RebuildDrawerTags();
                UpdateTrashBadge();
                ApplyFilters();
                OnPropertyChanged(nameof(TotalNotesCountText));
                ShowNotesFeed();
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
                RebuildCategoryChips();
                ApplyFilters();
                UpdateModalAccountStatus();
            }
        }

        // ── Hardware Back Button ──────────────────────────────────────────
        private void OnViewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back || e.Key == Key.Escape)
            {
                if (ConfirmDeleteFolderModal?.IsVisible == true)
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
                else if (LeftDrawerOverlay?.IsVisible == true)
                {
                    e.Handled = true;
                    LeftDrawerOverlay.IsVisible = false;
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
                    RebuildCategoryChips();
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
                RebuildCategoryChips();
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
