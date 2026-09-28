using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using QuickNote.App.Helpers;

namespace QuickNote.App
{
    public partial class MainWindow
    {
        private bool _isUpdatingSecondaryEditor;
        private bool _isUpdatingSecondaryFromNotion;

        private void UpdateReadingModeUI(bool isReadingMode)
        {
            NotionEditorBox.IsReadOnly = isReadingMode;
            NoteTitleBox.IsReadOnly = isReadingMode;
            NotionEditorBox.CaretBrush = isReadingMode ? Brushes.Transparent : (_viewModel.IsDarkMode ? Brushes.White : Brushes.Black);

            if (isReadingMode)
            {
                TableToolbar.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateSplitViewLayout(bool isSplit)
        {
            if (isSplit)
            {
                SplitterCol.Width = GridLength.Auto;
                SecondaryEditorCol.Width = new GridLength(1, GridUnitType.Star);
                SecondaryEditorCol.MinWidth = 200;
                SplitterBar.Visibility = Visibility.Visible;
                SecondaryEditorPane.Visibility = Visibility.Visible;
                LoadSecondaryDocument();
            }
            else
            {
                SplitterCol.Width = new GridLength(0);
                SecondaryEditorCol.Width = new GridLength(0);
                SecondaryEditorCol.MinWidth = 0;
                SplitterBar.Visibility = Visibility.Collapsed;
                SecondaryEditorPane.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadSecondaryDocument()
        {
            if (SecondaryEditorBox == null) return;
            _isUpdatingSecondaryEditor = true;
            try
            {
                var content = _viewModel.SecondaryNote != null
                    ? (_viewModel.SecondaryNote.Content ?? string.Empty)
                    : _viewModel.EditorContent;

                SecondaryEditorBox.Document = NotionMarkdownHelper.MarkdownToFlowDocument(content, _settingsService.Settings.NotesRoot);
            }
            finally
            {
                _isUpdatingSecondaryEditor = false;
            }
        }

        private void SecondaryEditorBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindVisualChild<ScrollViewer>(SecondaryEditorBox);
            if (sv == null) return;

            // Dampen vertical scroll speed to ~48px per wheel notch
            double offsetChange = (e.Delta / 120.0) * 48.0;
            double targetVertical = Math.Clamp(sv.VerticalOffset - offsetChange, 0, sv.ScrollableHeight);
            sv.ScrollToVerticalOffset(targetVertical);
            e.Handled = true;
        }

        private void SecondaryEditorBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var textPos = SecondaryEditorBox.GetPositionFromPoint(e.GetPosition(SecondaryEditorBox), snapToText: false);
            if (textPos != null)
            {
                var p = textPos.Paragraph;
                if (p != null)
                {
                    var tag = p.Tag as string;
                    if (tag == NotionMarkdownHelper.TagH1 || tag == NotionMarkdownHelper.TagH2 || tag == NotionMarkdownHelper.TagH3)
                    {
                        var rangeToClick = new TextRange(p.ContentStart, textPos);
                        bool isFolded = NotionMarkdownHelper.FoldedHeadingSections.ContainsKey(p);
                        var trToEnd = new TextRange(textPos, p.ContentEnd);
                        if (rangeToClick.Text.Length <= 3 || (isFolded && trToEnd.Text.Trim().Length <= 4))
                        {
                            e.Handled = true;
                            NotionMarkdownHelper.ToggleHeadingFold(p, SecondaryEditorBox);
                            return;
                        }
                    }
                }
            }
        }

        private void SecondaryEditorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isUpdatingSecondaryEditor)
            {
                _isUpdatingSecondaryFromNotion = true;
                try
                {
                    var markdown = NotionMarkdownHelper.FlowDocumentToMarkdown(SecondaryEditorBox.Document);
                    _viewModel.SecondaryEditorContent = markdown;

                    if (_viewModel.SecondaryNote != null)
                    {
                        _viewModel.SecondaryNote.Content = markdown;
                    }

                    // If viewing the same note in both panes, sync primary content
                    if (_viewModel.SecondaryNote == _viewModel.CurrentNote && !_isUpdatingEditor)
                    {
                        _viewModel.EditorContent = markdown;
                    }
                }
                finally
                {
                    _isUpdatingSecondaryFromNotion = false;
                }
            }
        }
    }
}
