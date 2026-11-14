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

        public event PropertyChangedEventHandler? PropertyChanged;
        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public partial class MobileMainView : UserControl, INotifyPropertyChanged
    {
        private MainViewModel? _viewModel;
        private string _selectedCategory = "All Notes";
        private string _searchQuery = string.Empty;
        private string _activeNoteTitle = string.Empty;
        private bool _isGridView = true;

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
        public bool IsNotesEmpty => FilteredNotes.Count == 0;

        public MobileMainView()
        {
            InitializeComponent();
            NotesFeedScreen.DataContext = this;
            NoteEditorScreen.DataContext = this;
            GoogleDriveAccountModal.DataContext = this;
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

        // ── Category Chips & Note Filtering ──────────────────────────────
        private void RebuildCategoryChips()
        {
            if (CategoryChipsPanel == null || _viewModel == null) return;

            CategoryChipsPanel.Children.Clear();

            var categories = new List<string> { "All Notes" };
            var folderNames = _viewModel.Notes
                .Select(n => Path.GetFileName(n.FolderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct()
                .OrderBy(f => f);

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
                    Tag = cat
                };
                btn.Click += (s, e) =>
                {
                    _selectedCategory = cat;
                    RebuildCategoryChips();
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
                BorderThickness = new Thickness(0)
            };
            addFolderBtn.Click += async (s, e) =>
            {
                if (_viewModel != null)
                {
                    await _viewModel.NewFolderAsync();
                    RebuildCategoryChips();
                }
            };
            CategoryChipsPanel.Children.Add(addFolderBtn);
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
                ApplyFilters();
            }
        }

        private async void OnDeleteCurrentNoteClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                await _viewModel.Delete();
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
                if (NoteEditorScreen.IsVisible)
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
                    if (url.Contains("code=") || url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
                    {
                        var code = ExtractQueryParam(url, "code");
                        if (!string.IsNullOrEmpty(code))
                        {
                            e.Cancel = true;
                            GoogleDriveAuthModal.IsVisible = false;
                            _ = HandleAuthCodeAsync(code);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MobileMainView] Auth navigation starting error: {ex.Message}");
            }
        }

        private async void OnAuthWebViewNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
        {
            try
            {
                if (e.Request != null)
                {
                    var url = e.Request.ToString();
                    if (url.Contains("code="))
                    {
                        var code = ExtractQueryParam(url, "code");
                        if (!string.IsNullOrEmpty(code))
                        {
                            GoogleDriveAuthModal.IsVisible = false;
                            _ = HandleAuthCodeAsync(code);
                            return;
                        }
                    }
                }

                var href = await AuthWebView.InvokeScript("window.location.href");
                if (!string.IsNullOrEmpty(href) && href.Contains("code="))
                {
                    var code = ExtractQueryParam(href, "code");
                    if (!string.IsNullOrEmpty(code))
                    {
                        GoogleDriveAuthModal.IsVisible = false;
                        _ = HandleAuthCodeAsync(code);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MobileMainView] Auth navigation completed error: {ex.Message}");
            }
        }

        private async Task HandleAuthCodeAsync(string code)
        {
            try
            {
                var verifier = _pendingCodeVerifier;
                _pendingCodeVerifier = null;

                if (!string.IsNullOrEmpty(verifier) && _viewModel?.CloudSyncService != null)
                {
                    await _viewModel.CloudSyncService.CompleteSignInAsync(code, verifier);
                    await _viewModel.Refresh();
                    UpdateModalAccountStatus();
                    ApplyFilters();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MobileMainView] HandleAuthCodeAsync error: {ex.Message}");
            }
        }

        private async void OnSyncNowClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.CloudSyncService != null)
            {
                await _viewModel.CloudSyncService.TriggerSyncAsync();
                await _viewModel.Refresh();
                UpdateModalAccountStatus();
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
