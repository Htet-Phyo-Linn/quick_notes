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
    public class MobileNoteItem : INotifyPropertyChanged
    {
        public NoteDocument Note { get; }

        public MobileNoteItem(NoteDocument note)
        {
            Note = note;
        }

        public string Title => !string.IsNullOrWhiteSpace(Note?.Title)
            ? Note.Title
            : (Path.GetFileNameWithoutExtension(Note?.FileName) ?? "Untitled Note");

        public string Snippet
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Note?.Content)) return "Empty note";
                var lines = Note.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var firstLine = lines.Length > 0 ? lines[0].TrimStart('#', ' ', '-', '*', '>') : "Empty note";
                return firstLine.Length > 90 ? firstLine.Substring(0, 90) + "..." : firstLine;
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
            ? Note.LastModified.ToString("MMM d, h:mm tt")
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

        public ObservableCollection<MobileNoteItem> FilteredNotes { get; } = new();

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

        public string CloudSyncStatusText => _viewModel?.CloudSyncStatusText ?? "OneDrive: Ready";

        public string NoteCountStatus => $"{FilteredNotes.Count} note{(FilteredNotes.Count == 1 ? "" : "s")}";

        public bool IsNotesEmpty => FilteredNotes.Count == 0;

        public MobileMainView()
        {
            InitializeComponent();
            NotesFeedScreen.DataContext = this;
            NoteEditorScreen.DataContext = this;
            DataContextChanged += OnDataContextChanged;
            KeyDown += OnViewKeyDown;
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
                await _viewModel.Refresh();
                RebuildCategoryChips();
                ApplyFilters();
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
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
