using System;
using System.Linq;
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
        private void NotionEditor_MarkdownChanged(string markdown)
        {
            if (_viewModel == null || _isUpdatingEditorText) return;
            _isUpdatingEditorText = true;
            try
            {
                _viewModel.EditorContent = markdown;
                if (TextEditor != null && TextEditor.Text != markdown)
                    TextEditor.Text = markdown;
            }
            finally { _isUpdatingEditorText = false; }
        }

        private void TextEditor_TextChanged(object? sender, EventArgs e)
        {
            if (_viewModel == null || _isUpdatingEditorText || TextEditor == null) return;
            _isUpdatingEditorText = true;
            try
            {
                _viewModel.EditorContent = TextEditor.Text;
                if (NotionEditor != null) NotionEditor.LoadMarkdown(TextEditor.Text);
            }
            finally { _isUpdatingEditorText = false; }
        }

        private void OnMarkdownRenderRequested(string markdown)
        {
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
                        NotionEditor.LoadMarkdown(markdown, force: true);
                    }
                    if (TextEditor != null && TextEditor.Text != markdown)
                        TextEditor.Text = markdown;
                }
                finally { _isUpdatingEditorText = false; }
                UpdatePreview(markdown);
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

        // ── Heading / outline navigation ────────────────────────────
        private async void OnHeadingNavigationRequested(Heading heading)
        {
            if (heading == null) return;

            if (_viewModel?.IsNotionMode == true && NotionEditor != null)
            {
                await NotionEditor.ScrollToHeadingAsync(heading.Text, heading.Level);
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
                _viewModel?.SelectTab(tab);
        }

        // ── Title box ───────────────────────────────────────────────
        private void TitleBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (_viewModel?.IsNotionMode == true) NotionEditor?.Focus();
            else                                  TextEditor?.Focus();
            e.Handled = true;
        }

        // ── Editor shortcut bridge (from Milkdown JS) ────────────────
        private async void OnEditorShortcutTriggered(string action)
        {
            if (_viewModel == null) return;
            switch (action)
            {
                case "save":      await _viewModel.SaveNoteAsync(); break;
                case "newNote":   await _viewModel.NewNoteAsync();  break;
                case "newFolder": await _viewModel.NewFolderAsync(); break;
                case "closeTab":  _viewModel.CloseActiveTab();       break;
                case "search":
                    SearchBox?.Focus();
                    SearchBox?.SelectAll();
                    break;
            }
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
