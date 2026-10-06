using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuickNote.Avalonia.Helpers;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.Views
{
    // Editor-related event handlers: content sync, preview, heading navigation, shortcuts.
    public partial class MainView
    {
        // ── Editor content sync ─────────────────────────────────────
        private bool _isOriginatingFromNotion;
        private bool _isOriginatingFromNotionRight;

        private void NotionEditor_MarkdownChanged(string markdown)
        {
            if (_viewModel == null || _isUpdatingEditorText) return;
            _isUpdatingEditorText = true;
            _isOriginatingFromNotion = true;
            try
            {
                _viewModel.EditorContent = markdown;
                if (TextEditor != null && TextEditor.Text != markdown)
                    TextEditor.Text = markdown;
            }
            finally
            {
                _isUpdatingEditorText = false;
                _isOriginatingFromNotion = false;
            }
        }

        private void NotionEditorRight_MarkdownChanged(string markdown)
        {
            if (_viewModel == null || _isUpdatingEditorText) return;
            _isUpdatingEditorText = true;
            _isOriginatingFromNotionRight = true;
            try
            {
                _viewModel.RightEditorContent = markdown;
            }
            finally
            {
                _isUpdatingEditorText = false;
                _isOriginatingFromNotionRight = false;
            }
        }

        private void TextEditor_TextChanged(object? sender, EventArgs e)
        {
            if (_viewModel == null || _isUpdatingEditorText || TextEditor == null) return;
            _isUpdatingEditorText = true;
            try
            {
                _viewModel.EditorContent = TextEditor.Text;
                if (NotionEditor != null) NotionEditor.LoadMarkdown(TextEditor.Text, force: false);
            }
            finally { _isUpdatingEditorText = false; }
        }

        private void OnMarkdownRenderRequested(string markdown)
        {
            // If the render request was triggered by NotionEditor's own keystroke,
            // never echo it back to NotionEditor! It already contains the authoritative state.
            if (_isOriginatingFromNotion)
            {
                if (_viewModel?.IsReadingMode == true)
                {
                    UpdatePreview(markdown);
                }
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel == null) return;
                _isUpdatingEditorText = true;
                try
                {
                    if (NotionEditor != null)
                    {
                        NotionEditor.NotesRoot = _viewModel.NotesRoot;
                        NotionEditor.CurrentNoteFolderPath = _viewModel.CurrentNote?.FolderPath;
                        NotionEditor.CurrentNotePath = _viewModel.CurrentNote?.FullPath;
                        NotionEditor.LoadMarkdown(markdown, force: true);
                    }
                    if (TextEditor != null && TextEditor.Text != markdown)
                        TextEditor.Text = markdown;
                }
                finally { _isUpdatingEditorText = false; }
                UpdatePreview(markdown);
            }, DispatcherPriority.Render);
        }

        private void OnRightMarkdownRenderRequested(string markdown)
        {
            // If the render request was triggered by NotionEditorRight's own keystroke,
            // never echo it back to NotionEditorRight! It already contains the authoritative state.
            if (_isOriginatingFromNotionRight) return;

            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel == null || NotionEditorRight == null) return;
                _isUpdatingEditorText = true;
                try
                {
                    NotionEditorRight.NotesRoot = _viewModel.NotesRoot;
                    NotionEditorRight.CurrentNoteFolderPath = _viewModel.RightCurrentNote?.FolderPath;
                    NotionEditorRight.CurrentNotePath = _viewModel.RightCurrentNote?.FullPath;
                    NotionEditorRight.LoadMarkdown(markdown, force: true);
                }
                finally { _isUpdatingEditorText = false; }
            }, DispatcherPriority.Render);
        }

        private void UpdatePreview(string markdown)
        {
            if (PreviewContainer != null)
                PreviewContainer.Child = MarkdigAvaloniaRenderer.Render(
                    markdown,
                    _viewModel?.CurrentNote?.FolderPath,
                    _viewModel?.NotesRoot);
        }

        private async void OnFocusEditorRequested()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (_viewModel?.IsNotionMode == true)
                {
                    if (_viewModel.IsSplitViewActive && _viewModel.ActiveEditorPane == EditorPane.Right && NotionEditorRight != null)
                    {
                        await NotionEditorRight.FocusEditorAsync();
                    }
                    else if (NotionEditor != null)
                    {
                        await NotionEditor.FocusEditorAsync();
                    }
                }
                else if (TextEditor != null)
                {
                    TextEditor.Focus();
                }
            });
        }

        // ── Heading / outline navigation ────────────────────────────
        private async void OnHeadingNavigationRequested(Heading heading)
        {
            if (heading == null) return;

            if (_viewModel?.IsNotionMode == true)
            {
                if (_viewModel.IsSplitViewActive && _viewModel.ActiveEditorPane == EditorPane.Right && NotionEditorRight != null)
                {
                    await NotionEditorRight.ScrollToHeadingAsync(heading.Text, heading.Level);
                }
                else if (NotionEditor != null)
                {
                    await NotionEditor.ScrollToHeadingAsync(heading.Text, heading.Level);
                }
            }
            else if (TextEditor != null &&
                     heading.LineNumber > 0 &&
                     heading.LineNumber <= TextEditor.Document.LineCount)
            {
                TextEditor.ScrollToLine(heading.LineNumber);
                var docLine = TextEditor.Document.GetLineByNumber(heading.LineNumber);
                TextEditor.CaretOffset = docLine.Offset;
                TextEditor.Focus();
            }
        }

        private void OnScrollToLineRequested(int line)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (TextEditor != null && line > 0 && line <= TextEditor.Document.LineCount)
                {
                    TextEditor.ScrollToLine(line);
                    var docLine = TextEditor.Document.GetLineByNumber(line);
                    TextEditor.CaretOffset = docLine.Offset;
                    TextEditor.Focus();
                }
            });
        }

        private void OutlineTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (OutlineTreeView?.SelectedItem is Heading heading)
            {
                _viewModel?.NavigateToHeading(heading);
                OutlineTreeView.SelectedItem = null;
            }
        }

        private void OnOutlineItemPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control control && control.DataContext is Heading heading)
                _viewModel?.NavigateToHeading(heading);
        }

        // ── Search & tab helpers ────────────────────────────────────
        private void SearchResultsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (SearchResultsListBox?.SelectedItem is SearchResult result)
                _viewModel?.SelectSearchResult(result);
        }

        private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control control && control.DataContext is NoteTabViewModel tab)
            {
                if (_viewModel != null)
                {
                    _viewModel.ActiveEditorPane = EditorPane.Left;
                    _viewModel.SelectTab(tab);
                }
            }
        }

        private void OnRightTabPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control control && control.DataContext is NoteTabViewModel tab)
            {
                if (_viewModel != null)
                {
                    _viewModel.ActiveEditorPane = EditorPane.Right;
                    _viewModel.SelectRightTab(tab);
                }
            }
        }

        // ── Title box ───────────────────────────────────────────────
        private void TitleBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (_viewModel != null) _viewModel.ActiveEditorPane = EditorPane.Left;
            if (_viewModel?.IsNotionMode == true) NotionEditor?.Focus();
            else                                  TextEditor?.Focus();
            e.Handled = true;
        }

        private void RightTitleBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (_viewModel != null) _viewModel.ActiveEditorPane = EditorPane.Right;
            NotionEditorRight?.Focus();
            e.Handled = true;
        }

        // ── Editor shortcut bridge (from Milkdown JS) ────────────────
        private async void OnEditorShortcutTriggered(string action)
        {
            if (_viewModel == null) return;
            switch (action)
            {
                case "save":
                    if (NotionEditor != null) await NotionEditor.FlushPendingChangesAsync();
                    await _viewModel.SaveNoteAsync();
                    if (_viewModel.IsSplitViewActive && _viewModel.HasRightCurrentNote &&
                        string.Equals(_viewModel.CurrentNote?.FullPath, _viewModel.RightCurrentNote?.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        await _viewModel.SaveRightNoteAsync();
                    }
                    break;
                case "newNote":    await _viewModel.NewNoteAsync();  break;
                case "newFolder":  await _viewModel.NewFolderAsync(); break;
                case "closeTab":   _viewModel.CloseActiveTab();       break;
                case "search":     FocusGlobalSearch();               break;
                case "findInNote": ShowInNoteFindBar();               break;
            }
        }

        private async void OnEditorRightShortcutTriggered(string action)
        {
            if (_viewModel == null) return;
            switch (action)
            {
                case "save":
                    if (NotionEditorRight != null) await NotionEditorRight.FlushPendingChangesAsync();
                    await _viewModel.SaveRightNoteAsync();
                    if (_viewModel.IsSplitViewActive && _viewModel.HasCurrentNote &&
                        string.Equals(_viewModel.CurrentNote?.FullPath, _viewModel.RightCurrentNote?.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        await _viewModel.SaveNoteAsync();
                    }
                    break;
                case "newNote":    await _viewModel.NewNoteAsync();       break;
                case "newFolder":  await _viewModel.NewFolderAsync();     break;
                case "closeTab":   _viewModel.CloseActiveRightTab();      break;
                case "search":     FocusGlobalSearch();                   break;
                case "findInNote": ShowInNoteFindBar();                   break;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        public void FocusGlobalSearch()
        {
            if (SearchBox == null) return;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    var handle = topLevel?.TryGetPlatformHandle()?.Handle;
                    if (handle.HasValue && handle.Value != IntPtr.Zero)
                    {
                        SetFocus(handle.Value);
                    }
                }
            }
            catch { }

            Dispatcher.UIThread.Post(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }, DispatcherPriority.Input);
        }

        public async void ShowInNoteFindBar()
        {
            if (_viewModel?.IsNotionMode == true)
            {
                if (_viewModel.IsSplitViewActive && _viewModel.ActiveEditorPane == EditorPane.Right && NotionEditorRight != null)
                {
                    await NotionEditorRight.OpenFindWidgetAsync();
                }
                else if (NotionEditor != null)
                {
                    await NotionEditor.OpenFindWidgetAsync();
                }
            }
        }

        public async void CloseInNoteFind()
        {
            if (_viewModel?.IsNotionMode == true)
            {
                if (_viewModel.IsSplitViewActive && _viewModel.ActiveEditorPane == EditorPane.Right && NotionEditorRight != null)
                {
                    await NotionEditorRight.CloseFindWidgetAsync();
                }
                else if (NotionEditor != null)
                {
                    await NotionEditor.CloseFindWidgetAsync();
                }
            }
        }

        private int FindInTextEditor(string query, bool backwards)
        {
            if (TextEditor == null || string.IsNullOrEmpty(query)) return 0;
            var text = TextEditor.Text ?? string.Empty;
            var matches = Regex.Matches(
                text,
                Regex.Escape(query),
                RegexOptions.IgnoreCase);
            if (matches.Count == 0) return 0;

            int currentCaret = TextEditor.CaretOffset;
            Match? target = null;
            if (backwards)
            {
                target = matches.Cast<Match>().LastOrDefault(m => m.Index < currentCaret) ?? matches[^1];
            }
            else
            {
                target = matches.Cast<Match>().FirstOrDefault(m => m.Index > currentCaret) ?? matches[0];
            }

            if (target != null)
            {
                TextEditor.Select(target.Index, target.Length);
                TextEditor.CaretOffset = target.Index + target.Length;
                var line = TextEditor.Document.GetLineByOffset(target.Index);
                TextEditor.ScrollToLine(line.LineNumber);
            }
            return matches.Count;
        }

        // ── Tag interactions (Double-click to edit, Enter to auto-save) ──
        private void OnTagDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is Control control && control.DataContext is TagItemViewModel item)
            {
                _viewModel?.StartEditTagItem(item);
                e.Handled = true;
            }
        }

        private void TagItemEditBox_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }, DispatcherPriority.Input);
            }
        }

        private async void TagItemEditBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is Control control && control.DataContext is TagItemViewModel item)
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    if (_viewModel != null)
                    {
                        await _viewModel.ConfirmEditTagItem(item);
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    _viewModel?.CancelEditTagItem(item);
                }
            }
        }

        private async void TagItemEditBox_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is Control control && control.DataContext is TagItemViewModel item && item.IsEditing)
            {
                if (_viewModel != null)
                {
                    await _viewModel.ConfirmEditTagItem(item);
                }
            }
        }

        private void AddTagButton_Click(object? sender, RoutedEventArgs e)
        {
            FocusTagInput();
        }

        private async void TagInputBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (_viewModel != null)
                {
                    await _viewModel.ConfirmAddTag();
                }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _viewModel?.CancelAddTag();
            }
        }

        private async void TagInputBox_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.IsAddingTag == true)
            {
                if (!string.IsNullOrWhiteSpace(_viewModel.NewTagText))
                {
                    await _viewModel.ConfirmAddTag();
                }
                else
                {
                    _viewModel.CancelAddTag();
                }
            }
        }

        private void FocusTagInput()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var tagBox = this.FindControl<TextBox>("TagInputBox");
                if (tagBox != null)
                {
                    tagBox.Focus();
                    tagBox.SelectAll();
                }
            }, DispatcherPriority.Input);
        }

        private async void OnNotionEditorFocused()
        {
            if (_viewModel != null)
            {
                _viewModel.ActiveEditorPane = EditorPane.Left;
                await _viewModel.CommitAllEditingTags();
                await CommitAllInlineTreeRenames();
            }
        }

        private async void OnNotionEditorRightFocused()
        {
            if (_viewModel != null)
            {
                _viewModel.ActiveEditorPane = EditorPane.Right;
                await _viewModel.CommitAllEditingTags();
                await CommitAllInlineTreeRenames();
            }
        }

        private async void MainView_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_viewModel == null) return;

            bool isTagEditing = _viewModel.IsAddingTag || _viewModel.CurrentNoteTags.Any(t => t.IsEditing);
            bool isTreeEditing = HasAnyTreeEditingNode();

            if (!isTagEditing && !isTreeEditing) return;

            var source = e.Source as Visual;
            bool clickedTagEditor = false;
            bool clickedTreeEditor = false;

            var current = source;
            while (current != null)
            {
                if (current is TextBox tb)
                {
                    if (tb.Name == "TagInputBox" || tb.DataContext is TagItemViewModel)
                        clickedTagEditor = true;
                    if (tb.Name == "InlineRenameBox" || tb.DataContext is FolderNode)
                        clickedTreeEditor = true;
                }
                if (current is Button btn && (btn.DataContext is TagItemViewModel || btn.Name == "AddTagButton"))
                {
                    clickedTagEditor = true;
                }
                current = current.GetVisualParent();
            }

            if (isTagEditing && !clickedTagEditor)
            {
                await _viewModel.CommitAllEditingTags();
            }

            if (isTreeEditing && !clickedTreeEditor)
            {
                await CommitAllInlineTreeRenames();
            }
        }
    }
}
