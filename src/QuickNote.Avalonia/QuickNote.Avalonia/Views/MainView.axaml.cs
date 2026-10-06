using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using QuickNote.Avalonia.Helpers;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Models;
using QuickNote.Core.Utilities;

namespace QuickNote.Avalonia.Views
{
    // Lifecycle wiring, sidebar/layout management, and keyboard shortcuts.
    public partial class MainView : UserControl
    {
        private MainViewModel? _viewModel;
        private bool _isUpdatingEditorText;

        public MainView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        // ── DataContext wiring ────────────────────────────────────────
        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.ScrollToLineRequested    -= OnScrollToLineRequested;
                _viewModel.HeadingNavigationRequested -= OnHeadingNavigationRequested;
                _viewModel.MarkdownRenderRequested   -= OnMarkdownRenderRequested;
                _viewModel.RightMarkdownRenderRequested -= OnRightMarkdownRenderRequested;
                _viewModel.ExportRequested           -= OnExportRequested;
                _viewModel.FocusEditorRequested      -= OnFocusEditorRequested;
                _viewModel.PropertyChanged           -= OnViewModelPropertyChanged;
            }

            _viewModel = DataContext as MainViewModel;

            if (_viewModel != null)
            {
                _viewModel.ScrollToLineRequested    += OnScrollToLineRequested;
                _viewModel.HeadingNavigationRequested += OnHeadingNavigationRequested;
                _viewModel.MarkdownRenderRequested   += OnMarkdownRenderRequested;
                _viewModel.RightMarkdownRenderRequested += OnRightMarkdownRenderRequested;
                _viewModel.ExportRequested           += OnExportRequested;
                _viewModel.FocusEditorRequested      += OnFocusEditorRequested;
                _viewModel.PropertyChanged           += OnViewModelPropertyChanged;

                if (NotionEditor != null)
                {
                    NotionEditor.NotesRoot = _viewModel.NotesRoot;
                    NotionEditor.CurrentNoteFolderPath = _viewModel.CurrentNote?.FolderPath;
                    NotionEditor.IsSpellCheckEnabled = _viewModel.IsSpellCheckEnabled;
                    NotionEditor.IgnoredWords = _viewModel.IgnoredWords;
                }

                if (NotionEditorRight != null)
                {
                    NotionEditorRight.NotesRoot = _viewModel.NotesRoot;
                    NotionEditorRight.CurrentNoteFolderPath = _viewModel.RightCurrentNote?.FolderPath;
                    NotionEditorRight.IsSpellCheckEnabled = _viewModel.IsSpellCheckEnabled;
                    NotionEditorRight.IgnoredWords = _viewModel.IgnoredWords;
                }

                if (_viewModel.EditorContent != null)
                {
                    _isUpdatingEditorText = true;
                    try
                    {
                        NotionEditor?.LoadMarkdown(_viewModel.EditorContent, force: true);
                        if (TextEditor != null)
                            TextEditor.Text = _viewModel.EditorContent;
                    }
                    finally { _isUpdatingEditorText = false; }
                }

                if (_viewModel.IsSplitViewActive && _viewModel.RightEditorContent != null)
                {
                    NotionEditorRight?.LoadMarkdown(_viewModel.RightEditorContent, force: true);
                }

                UpdatePreview(_viewModel.EditorContent ?? string.Empty);
                UpdateLayoutModes();
                UpdateSplitViewLayout();
                UpdateSidebarVisibility();
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (NotionEditor != null)
            {
                if (_viewModel != null)
                {
                    NotionEditor.NotesRoot = _viewModel.NotesRoot;
                    NotionEditor.CurrentNoteFolderPath = _viewModel.CurrentNote?.FolderPath;
                    NotionEditor.IsSpellCheckEnabled = _viewModel.IsSpellCheckEnabled;
                    NotionEditor.IgnoredWords = _viewModel.IgnoredWords;
                }
                NotionEditor.MarkdownChanged      += NotionEditor_MarkdownChanged;
                NotionEditor.ShortcutTriggered    += OnEditorShortcutTriggered;
                NotionEditor.EditorFocused        += OnNotionEditorFocused;
                NotionEditor.WordIgnored          += async (word) => {
                    if (_viewModel != null) {
                        await _viewModel.AddIgnoredWord(word);
                        await NotionEditor.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
                        if (NotionEditorRight != null)
                            await NotionEditorRight.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
                    }
                };
                if (_viewModel?.EditorContent != null)
                    NotionEditor.LoadMarkdown(_viewModel.EditorContent, force: true);
            }

            if (NotionEditorRight != null)
            {
                if (_viewModel != null)
                {
                    NotionEditorRight.NotesRoot = _viewModel.NotesRoot;
                    NotionEditorRight.CurrentNoteFolderPath = _viewModel.RightCurrentNote?.FolderPath;
                    NotionEditorRight.IsSpellCheckEnabled = _viewModel.IsSpellCheckEnabled;
                    NotionEditorRight.IgnoredWords = _viewModel.IgnoredWords;
                }
                NotionEditorRight.MarkdownChanged      += NotionEditorRight_MarkdownChanged;
                NotionEditorRight.ShortcutTriggered    += OnEditorRightShortcutTriggered;
                NotionEditorRight.EditorFocused        += OnNotionEditorRightFocused;
                NotionEditorRight.WordIgnored          += async (word) => {
                    if (_viewModel != null) {
                        await _viewModel.AddIgnoredWord(word);
                        if (NotionEditor != null) await NotionEditor.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
                        if (NotionEditorRight != null) await NotionEditorRight.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
                    }
                };
                if (_viewModel?.RightEditorContent != null)
                    NotionEditorRight.LoadMarkdown(_viewModel.RightEditorContent, force: true);
            }

            if (TextEditor != null)
            {
                TextEditor.TextChanged += TextEditor_TextChanged;
                TextEditor.GotFocus    += async (s, e) => {
                    if (_viewModel != null) {
                        _viewModel.ActiveEditorPane = EditorPane.Left;
                        await _viewModel.CommitAllEditingTags();
                        await CommitAllInlineTreeRenames();
                    }
                };
                if (_viewModel?.EditorContent != null)
                {
                    _isUpdatingEditorText = true;
                    try   { TextEditor.Text = _viewModel.EditorContent; }
                    finally { _isUpdatingEditorText = false; }
                }
            }

            if (TitleBox != null)
            {
                TitleBox.GotFocus += async (s, e) => {
                    if (_viewModel != null) {
                        _viewModel.ActiveEditorPane = EditorPane.Left;
                        await _viewModel.CommitAllEditingTags();
                        await CommitAllInlineTreeRenames();
                    }
                };
            }

            if (RightTitleBox != null)
            {
                RightTitleBox.GotFocus += async (s, e) => {
                    if (_viewModel != null) {
                        _viewModel.ActiveEditorPane = EditorPane.Right;
                        await _viewModel.CommitAllEditingTags();
                        await CommitAllInlineTreeRenames();
                    }
                };
            }

            if (SearchBox != null)
            {
                SearchBox.GotFocus += async (s, e) => {
                    if (_viewModel != null) {
                        await _viewModel.CommitAllEditingTags();
                        await CommitAllInlineTreeRenames();
                    }
                };
            }

            AddHandler(InputElement.PointerPressedEvent, MainView_PointerPressed, RoutingStrategies.Tunnel);

            if (FolderTreeView != null)
            {
                FolderTreeView.AddHandler(InputElement.PointerPressedEvent,
                    FolderTreeView_PointerPressed, RoutingStrategies.Tunnel);
                FolderTreeView.AddHandler(InputElement.PointerMovedEvent,
                    FolderTreeView_PointerMoved, RoutingStrategies.Tunnel);
                FolderTreeView.AddHandler(InputElement.ContextRequestedEvent,
                    FolderTreeView_ContextRequested, RoutingStrategies.Tunnel);
                FolderTreeView.AddHandler(DragDrop.DragOverEvent, FolderTreeView_DragOver);
                FolderTreeView.AddHandler(DragDrop.DropEvent,     FolderTreeView_Drop);
            }

            UpdateLayoutModes();
            UpdateSidebarVisibility();
            if (_viewModel != null)
                UpdatePreview(_viewModel.EditorContent ?? string.Empty);

            var window = TopLevel.GetTopLevel(this) as Window;
            if (window != null)
            {
                window.Deactivated += async (s, e) =>
                {
                    if (_viewModel != null)
                    {
                        if (NotionEditor != null) await NotionEditor.FlushPendingChangesAsync();
                        if (NotionEditorRight != null) await NotionEditorRight.FlushPendingChangesAsync();
                        if (_viewModel.IsModified) await _viewModel.SaveNoteAsync();
                        if (_viewModel.IsSplitViewActive && _viewModel.IsRightModified) await _viewModel.SaveRightNoteAsync();
                    }
                };
                window.Closing += async (s, e) =>
                {
                    if (_viewModel != null)
                    {
                        if (NotionEditor != null) await NotionEditor.FlushPendingChangesAsync();
                        if (NotionEditorRight != null) await NotionEditorRight.FlushPendingChangesAsync();
                        if (_viewModel.IsModified) await _viewModel.SaveNoteAsync();
                        if (_viewModel.IsSplitViewActive && _viewModel.IsRightModified) await _viewModel.SaveRightNoteAsync();
                    }
                };
            }
        }

        // ── Property-change routing ─────────────────────────────────
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsReadingMode)  ||
                e.PropertyName == nameof(MainViewModel.IsNotionMode))
            {
                UpdateLayoutModes();
                if (_viewModel?.IsReadingMode == true)
                {
                    UpdatePreview(_viewModel.EditorContent ?? _viewModel.CurrentNote?.Content ?? string.Empty);
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.IsSplitViewActive))
            {
                UpdateSplitViewLayout();
                if (_viewModel?.IsSplitViewActive == true && NotionEditorRight != null && _viewModel.RightEditorContent != null)
                {
                    NotionEditorRight.NotesRoot = _viewModel.NotesRoot;
                    NotionEditorRight.CurrentNoteFolderPath = _viewModel.RightCurrentNote?.FolderPath;
                    NotionEditorRight.CurrentNotePath = _viewModel.RightCurrentNote?.FullPath;
                    NotionEditorRight.LoadMarkdown(_viewModel.RightEditorContent, force: true);
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.IsLeftSidebarVisible)  ||
                     e.PropertyName == nameof(MainViewModel.IsRightSidebarVisible))
            {
                UpdateSidebarVisibility();
            }
            else if (e.PropertyName == nameof(MainViewModel.IsSpellCheckEnabled))
            {
                if (_viewModel != null)
                {
                    if (NotionEditor != null) _ = NotionEditor.SetSpellCheckAsync(_viewModel.IsSpellCheckEnabled);
                    if (NotionEditorRight != null) _ = NotionEditorRight.SetSpellCheckAsync(_viewModel.IsSpellCheckEnabled);
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.IsRenameDialogVisible))
            {
                if (_viewModel?.IsRenameDialogVisible == true)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        RenameInputBox?.Focus();
                        RenameInputBox?.SelectAll();
                    }, DispatcherPriority.Input);
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.IsProfileDialogVisible))
            {
                if (_viewModel?.IsProfileDialogVisible == true)
                {
                    _ = OpenSettingsWindowAsync();
                }
            }
        }

        private async Task OpenSettingsWindowAsync()
        {
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow == null || _viewModel == null) return;

            var settingsWindow = new SettingsWindow
            {
                DataContext = _viewModel
            };

            await settingsWindow.ShowDialog(parentWindow);
            _viewModel.IsProfileDialogVisible = false;

            if (NotionEditor != null)
            {
                await NotionEditor.SetSpellCheckAsync(_viewModel.IsSpellCheckEnabled);
                await NotionEditor.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
            }
            if (NotionEditorRight != null)
            {
                await NotionEditorRight.SetSpellCheckAsync(_viewModel.IsSpellCheckEnabled);
                await NotionEditorRight.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
            }
        }

        // ── Sidebar visibility ────────────────────────────────────────
        private GridLength _lastLeftSidebarWidth  = new GridLength(240);
        private GridLength _lastRightSidebarWidth = new GridLength(220);

        private void UpdateSidebarVisibility()
        {
            if (_viewModel == null ||
                MainWorkspaceGrid == null ||
                MainWorkspaceGrid.ColumnDefinitions.Count < 5) return;

            var leftCol         = MainWorkspaceGrid.ColumnDefinitions[0];
            var leftSplitterCol = MainWorkspaceGrid.ColumnDefinitions[1];
            var rightSplitterCol = MainWorkspaceGrid.ColumnDefinitions[3];
            var rightCol        = MainWorkspaceGrid.ColumnDefinitions[4];

            if (_viewModel.IsLeftSidebarVisible)
            {
                leftCol.Width         = _lastLeftSidebarWidth.Value > 50 ? _lastLeftSidebarWidth : new GridLength(240);
                leftSplitterCol.Width = GridLength.Auto;
                if (LeftSidebarBorder  != null) LeftSidebarBorder.IsVisible  = true;
                if (LeftGridSplitter   != null) LeftGridSplitter.IsVisible   = true;
            }
            else
            {
                if (leftCol.Width.Value > 50) _lastLeftSidebarWidth = leftCol.Width;
                leftCol.Width         = new GridLength(0);
                leftSplitterCol.Width = new GridLength(0);
                if (LeftSidebarBorder  != null) LeftSidebarBorder.IsVisible  = false;
                if (LeftGridSplitter   != null) LeftGridSplitter.IsVisible   = false;
            }

            if (_viewModel.IsRightSidebarVisible)
            {
                rightCol.Width         = _lastRightSidebarWidth.Value > 50 ? _lastRightSidebarWidth : new GridLength(220);
                rightSplitterCol.Width = GridLength.Auto;
                if (RightSidebarBorder != null) RightSidebarBorder.IsVisible = true;
                if (RightGridSplitter  != null) RightGridSplitter.IsVisible  = true;
            }
            else
            {
                if (rightCol.Width.Value > 50) _lastRightSidebarWidth = rightCol.Width;
                rightCol.Width         = new GridLength(0);
                rightSplitterCol.Width = new GridLength(0);
                if (RightSidebarBorder != null) RightSidebarBorder.IsVisible = false;
                if (RightGridSplitter  != null) RightGridSplitter.IsVisible  = false;
            }
        }

        // ── Editor layout modes ───────────────────────────────────────
        private void UpdateLayoutModes()
        {
            if (_viewModel == null ||
                EditorWorkspaceGrid == null ||
                EditorWorkspaceGrid.ColumnDefinitions.Count < 3) return;

            var editorCol  = EditorWorkspaceGrid.ColumnDefinitions[0];
            var splitterCol = EditorWorkspaceGrid.ColumnDefinitions[1];
            var previewCol = EditorWorkspaceGrid.ColumnDefinitions[2];

            if (_viewModel.IsReadingMode)
            {
                editorCol.Width  = new GridLength(0);
                splitterCol.Width = new GridLength(0);
                previewCol.Width = new GridLength(1, GridUnitType.Star);
                if (TextEditor         != null) TextEditor.IsVisible         = false;
                if (EditorSplitter     != null) EditorSplitter.IsVisible     = false;
                if (PreviewScrollViewer != null) PreviewScrollViewer.IsVisible = true;
            }
            else if (_viewModel.IsSplitMode)
            {
                editorCol.Width  = new GridLength(1, GridUnitType.Star);
                splitterCol.Width = GridLength.Auto;
                previewCol.Width = new GridLength(1, GridUnitType.Star);
                if (TextEditor         != null) TextEditor.IsVisible         = true;
                if (EditorSplitter     != null) EditorSplitter.IsVisible     = true;
                if (PreviewScrollViewer != null) PreviewScrollViewer.IsVisible = true;
            }
            else
            {
                // Normal Editor Mode: TextEditor visible with preview side-by-side or full
                editorCol.Width  = new GridLength(1, GridUnitType.Star);
                splitterCol.Width = GridLength.Auto;
                previewCol.Width = new GridLength(1, GridUnitType.Star);
                if (TextEditor         != null) TextEditor.IsVisible         = true;
                if (EditorSplitter     != null) EditorSplitter.IsVisible     = true;
                if (PreviewScrollViewer != null) PreviewScrollViewer.IsVisible = true;
            }
        }

        private void UpdateSplitViewLayout()
        {
            if (_viewModel == null || VisualEditorsGrid == null || VisualEditorsGrid.ColumnDefinitions.Count < 3) return;

            var splitterCol = VisualEditorsGrid.ColumnDefinitions[1];
            var rightCol    = VisualEditorsGrid.ColumnDefinitions[2];

            if (_viewModel.IsSplitViewActive)
            {
                splitterCol.Width = GridLength.Auto;
                rightCol.Width    = new GridLength(1, GridUnitType.Star);
                if (VisualSplitter  != null) VisualSplitter.IsVisible  = true;
                if (RightEditorPane != null) RightEditorPane.IsVisible = true;
            }
            else
            {
                splitterCol.Width = new GridLength(0);
                rightCol.Width    = new GridLength(0);
                if (VisualSplitter  != null) VisualSplitter.IsVisible  = false;
                if (RightEditorPane != null) RightEditorPane.IsVisible = false;
            }
        }

        // ── Keyboard shortcuts ────────────────────────────────────────
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.F)
            {
                ShowInNoteFindBar();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F)
            {
                FocusGlobalSearch();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.B)
            {
                _viewModel?.ToggleLeftSidebar();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && (e.Key == Key.OemBackslash || e.Key == Key.Oem5))
            {
                _viewModel?.ToggleSplitView();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.W)
            {
                if (_viewModel?.IsSplitViewActive == true && _viewModel.ActiveEditorPane == EditorPane.Right)
                {
                    _viewModel.CloseActiveRightTab();
                }
                else
                {
                    _viewModel?.CloseActiveTab();
                }
                e.Handled = true;
            }
        }

        private void TabScrollViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
                sv.Offset = new Vector(Math.Max(0, sv.Offset.X - (delta * 60)), sv.Offset.Y);
                e.Handled = true;
            }
        }

        // ── Export handlers ──────────────────────────────────────────
        private async void OnExportRequested(string format)
        {
            try
            {
                switch (format?.ToLowerInvariant())
                {
                    case "md":
                        await ExportMarkdownAsync();
                        break;
                    case "docx":
                        await ExportDocxAsync();
                        break;
                    case "pdf":
                        await ExportPdfAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                if (_viewModel != null)
                {
                    _viewModel.StatusText = $"Export error: {ex.Message}";
                }
            }
        }

        private async Task ExportMarkdownAsync()
        {
            if (_viewModel?.CurrentNote == null && string.IsNullOrWhiteSpace(_viewModel?.EditorContent))
            {
                if (_viewModel != null) _viewModel.StatusText = "No note is currently open to export.";
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var noteTitle = !string.IsNullOrWhiteSpace(_viewModel?.NoteTitle)
                ? _viewModel.NoteTitle
                : (_viewModel?.CurrentNote?.Title ?? "Untitled");

            var sanitizedTitle = FileNameSanitizer.Sanitize(noteTitle);
            if (string.IsNullOrWhiteSpace(sanitizedTitle)) sanitizedTitle = "Note";

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Note as Markdown",
                SuggestedFileName = sanitizedTitle + ".md",
                DefaultExtension = "md",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Markdown Files (*.md)") { Patterns = new[] { "*.md" } },
                    new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
                }
            });

            if (file != null)
            {
                var localPath = file.Path.LocalPath;
                var content = _viewModel?.EditorContent ?? _viewModel?.CurrentNote?.Content ?? string.Empty;
                await File.WriteAllTextAsync(localPath, content, System.Text.Encoding.UTF8);
                if (_viewModel != null) _viewModel.StatusText = $"Successfully exported Markdown to: {Path.GetFileName(localPath)}";
            }
        }

        private async Task ExportDocxAsync()
        {
            if (_viewModel?.CurrentNote == null && string.IsNullOrWhiteSpace(_viewModel?.EditorContent))
            {
                if (_viewModel != null) _viewModel.StatusText = "No note is currently open to export.";
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var noteTitle = !string.IsNullOrWhiteSpace(_viewModel?.NoteTitle)
                ? _viewModel.NoteTitle
                : (_viewModel?.CurrentNote?.Title ?? "Untitled");

            var sanitizedTitle = FileNameSanitizer.Sanitize(noteTitle);
            if (string.IsNullOrWhiteSpace(sanitizedTitle)) sanitizedTitle = "Note";

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Note as Microsoft Word Document",
                SuggestedFileName = sanitizedTitle + ".docx",
                DefaultExtension = "docx",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Word Document (*.docx)") { Patterns = new[] { "*.docx" } },
                    new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
                }
            });

            if (file != null)
            {
                var localPath = file.Path.LocalPath;
                var tempNote = new NoteDocument
                {
                    Title = noteTitle,
                    Content = _viewModel?.EditorContent ?? _viewModel?.CurrentNote?.Content ?? string.Empty,
                    FolderPath = _viewModel?.CurrentNote?.FolderPath ?? string.Empty
                };
                await DocxExportHelper.ExportToDocxAsync(tempNote, localPath, _viewModel?.NotesRoot);
                if (_viewModel != null) _viewModel.StatusText = $"Successfully exported Word document to: {Path.GetFileName(localPath)}";
            }
        }

        private async Task ExportPdfAsync()
        {
            if (_viewModel?.CurrentNote == null && string.IsNullOrWhiteSpace(_viewModel?.EditorContent))
            {
                if (_viewModel != null) _viewModel.StatusText = "No note is currently open to export.";
                return;
            }

            if (NotionEditor != null)
            {
                var content = _viewModel?.EditorContent ?? _viewModel?.CurrentNote?.Content ?? string.Empty;
                NotionEditor.LoadMarkdown(content, force: true);
                await Task.Delay(150);
                await NotionEditor.PrintOrExportPdfAsync();
                if (_viewModel != null) _viewModel.StatusText = "Opened PDF Export / Print dialog.";
            }
        }

        private void NewIgnoredWord_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _viewModel != null)
            {
                e.Handled = true;
                _ = _viewModel.AddIgnoredWord(null);
            }
        }
    }
}