using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        #region Notion Live Markdown Typing Engine

        private bool IsEventFromEmbeddedControl(RoutedEventArgs e)
        {
            if (e.OriginalSource == null || e.OriginalSource == NotionEditorBox)
                return false;

            if (e.OriginalSource is DependencyObject dep)
            {
                DependencyObject? current = dep;
                while (current != null && current != NotionEditorBox)
                {
                    if (current is BlockUIContainer)
                        return true;
                    if (current is RichTextBox innerRtb && innerRtb != NotionEditorBox)
                        return true;

                    current = LogicalTreeHelper.GetParent(current);
                }
            }
            return false;
        }

        private void NotionEditorBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindVisualChild<ScrollViewer>(NotionEditorBox);
            if (sv == null) return;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Shift + Wheel = horizontal scroll
                double shiftOffsetChange = (e.Delta / 120.0) * 48.0;
                double targetHorizontal = Math.Clamp(sv.HorizontalOffset - shiftOffsetChange, 0, sv.ScrollableWidth);
                sv.ScrollToHorizontalOffset(targetHorizontal);
                e.Handled = true;
                return;
            }

            // Dampen vertical scroll speed: smooth ~48px per standard wheel notch (e.Delta / 120 = 1 notch)
            // instead of jumping multiple large paragraph blocks.
            double offsetChange = (e.Delta / 120.0) * 48.0;
            double targetVertical = Math.Clamp(sv.VerticalOffset - offsetChange, 0, sv.ScrollableHeight);
            sv.ScrollToVerticalOffset(targetVertical);
            e.Handled = true;
        }

        private void NotionEditorBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (IsEventFromEmbeddedControl(e)) return;

            if (NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty)
            {
                var focusedRtb = Keyboard.FocusedElement as RichTextBox;
                if (focusedRtb == null || focusedRtb == NotionEditorBox)
                {
                    var sel = NotionEditorBox.Selection;
                    if (sel.Start.Paragraph == null || sel.Start.Paragraph != sel.End.Paragraph)
                    {
                        DeleteSelection(sel);
                    }
                }
            }
        }

        private void TransformBlock(RichTextBox rtb, Paragraph p, string tag, string prefixToStrip, TextPointer caret)
        {
            var fullRange = new TextRange(p.ContentStart, p.ContentEnd);
            var fullText = fullRange.Text.TrimEnd('\r', '\n');

            string remainingText = string.Empty;
            if (fullText.StartsWith(prefixToStrip + " "))
            {
                remainingText = fullText.Substring(prefixToStrip.Length + 1);
            }
            else if (fullText.StartsWith(prefixToStrip))
            {
                remainingText = fullText.Substring(prefixToStrip.Length).TrimStart(' ');
            }
            else
            {
                var textAfterCaret = new TextRange(caret, p.ContentEnd).Text.TrimEnd('\r', '\n');
                remainingText = textAfterCaret;
            }

            if (tag == NotionMarkdownHelper.TagBullet)
            {
                remainingText = NotionMarkdownHelper.BulletPrefix + remainingText;
            }
            else if (tag == NotionMarkdownHelper.TagTodo)
            {
                remainingText = NotionMarkdownHelper.TodoPrefix + remainingText;
            }

            p.Inlines.Clear();
            var run = new Run(remainingText);
            p.Inlines.Add(run);
            NotionMarkdownHelper.ApplyStyle(p, tag);

            if (string.IsNullOrEmpty(remainingText) || 
                (tag == NotionMarkdownHelper.TagBullet && remainingText == NotionMarkdownHelper.BulletPrefix) ||
                (tag == NotionMarkdownHelper.TagTodo && remainingText == NotionMarkdownHelper.TodoPrefix))
            {
                rtb.CaretPosition = p.ContentEnd;
            }
            else
            {
                if (tag == NotionMarkdownHelper.TagBullet)
                {
                    rtb.CaretPosition = run.ContentStart.GetPositionAtOffset(NotionMarkdownHelper.BulletPrefix.Length) ?? run.ContentEnd;
                }
                else if (tag == NotionMarkdownHelper.TagTodo)
                {
                    rtb.CaretPosition = run.ContentStart.GetPositionAtOffset(NotionMarkdownHelper.TodoPrefix.Length) ?? run.ContentEnd;
                }
                else
                {
                    rtb.CaretPosition = run.ContentStart;
                }
            }

            rtb.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, p.FontSize);
            rtb.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, p.FontWeight);
            rtb.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, p.Foreground);
        }

        private bool ConvertParagraphToDivider(Paragraph p)
        {
            if (p?.Parent is FlowDocument doc)
            {
                var divider = NotionMarkdownHelper.CreateDividerBlock();
                doc.Blocks.InsertBefore(p, divider);
                var newP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                doc.Blocks.InsertAfter(divider, newP);
                doc.Blocks.Remove(p);
                NotionEditorBox.CaretPosition = newP.ContentStart;
                ResetCaretFormatting();
                return true;
            }
            return false;
        }

        private void NotionEditorBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsEventFromEmbeddedControl(e)) return;
            if (HandleTableKeyNavigation(e)) return;

            // Handle Ctrl+A explicitly to guarantee full note selection across all blocks
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                NotionEditorBox.SelectAll();
                return;
            }

            if ((e.Key == Key.Delete || e.Key == Key.Back) && NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty)
            {
                var focusedRtb = Keyboard.FocusedElement as RichTextBox;
                if (focusedRtb == null || focusedRtb == NotionEditorBox)
                {
                    e.Handled = true;
                    DeleteSelection(NotionEditorBox.Selection);
                    return;
                }
            }

            var caret = NotionEditorBox.CaretPosition;
            var p = caret != null ? (NotionMarkdownHelper.GetContainingParagraph(caret) ?? caret.Paragraph) : null;

            if (p == null) return;

            if (e.Key == Key.Tab)
            {
                var caretTab = NotionEditorBox.CaretPosition;
                var pTab = caretTab != null ? (NotionMarkdownHelper.GetContainingParagraph(caretTab) ?? caretTab.Paragraph) : null;
                if (pTab != null)
                {
                    if (NotionMarkdownHelper.IsCodeBlock(pTab))
                    {
                        e.Handled = true;
                        caretTab?.InsertTextInRun("    ");
                        SyncMarkdownFromEditor();
                        return;
                    }

                    var tagTab = pTab.Tag as string ?? string.Empty;
                    if (tagTab == NotionMarkdownHelper.TagBullet || tagTab == NotionMarkdownHelper.TagOrderedList || 
                        tagTab == NotionMarkdownHelper.TagTodo || tagTab == NotionMarkdownHelper.TagTodoDone)
                    {
                        e.Handled = true;
                        bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                        IndentListItem(pTab, !isShift);
                        SyncMarkdownFromEditor();
                        return;
                    }
                }
            }

            if (e.Key == Key.Space)
            {
                // If inside an existing code block, Space is just a normal character: do not re-create block or jump cursor!
                if (NotionMarkdownHelper.IsCodeBlock(p))
                {
                    return;
                }

                var textBeforeCaret = new TextRange(p.ContentStart, caret).Text.Trim();
                if (NotionMarkdownHelper.IsHorizontalRule(textBeforeCaret))
                {
                    e.Handled = true;
                    _isUpdatingEditor = true;
                    try
                    {
                        ConvertParagraphToDivider(p);
                    }
                    finally
                    {
                        _isUpdatingEditor = false;
                    }
                    SyncMarkdownFromEditor();
                    return;
                }

                if (textBeforeCaret.StartsWith("```"))
                {
                    e.Handled = true;
                    _isUpdatingEditor = true;
                    try
                    {
                        string lang = textBeforeCaret.Length > 3 ? textBeforeCaret.Substring(3).Trim() : string.Empty;
                        if (p.Parent is FlowDocument doc)
                        {
                            var codeBlock = NotionMarkdownHelper.CreateCodeBlock(string.Empty, lang);
                            doc.Blocks.InsertBefore(p, codeBlock);
                            var nextP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                            doc.Blocks.InsertAfter(codeBlock, nextP);
                            doc.Blocks.Remove(p);

                            _activeEditingCodeBlock = codeBlock;
                            NotionMarkdownHelper.PopulateActiveCodeBlockParagraph(codeBlock, string.Empty, lang);
                            var codeLine = codeBlock.ContentStart.GetLineStartPosition(1) ?? codeBlock.ContentStart;
                            NotionEditorBox.CaretPosition = codeLine;
                        }
                    }
                    finally
                    {
                        _isUpdatingEditor = false;
                    }
                    SyncMarkdownFromEditor();
                    return;
                }

                var tag = p.Tag as string ?? NotionMarkdownHelper.TagBody;

                _isUpdatingEditor = true;
                try
                {
                    if (tag != NotionMarkdownHelper.TagH1 && tag != NotionMarkdownHelper.TagH2 && tag != NotionMarkdownHelper.TagH3)
                    {
                        if (textBeforeCaret == "#")
                        {
                            var textAfter = new TextRange(caret, p.ContentEnd).Text;
                            e.Handled = true;
                            p.Inlines.Clear();
                            foreach (var inl in NotionMarkdownHelper.ParseInlineMarkdown(textAfter, NotionMarkdownHelper.TagH1)) p.Inlines.Add(inl);
                            NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagH1);
                            NotionEditorBox.CaretPosition = p.ContentStart;
                            return;
                        }
                        else if (textBeforeCaret == "##")
                        {
                            var textAfter = new TextRange(caret, p.ContentEnd).Text;
                            e.Handled = true;
                            p.Inlines.Clear();
                            foreach (var inl in NotionMarkdownHelper.ParseInlineMarkdown(textAfter, NotionMarkdownHelper.TagH2)) p.Inlines.Add(inl);
                            NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagH2);
                            NotionEditorBox.CaretPosition = p.ContentStart;
                            return;
                        }
                        else if (textBeforeCaret == "###" || textBeforeCaret == "####" || textBeforeCaret == "#####" || textBeforeCaret == "######")
                        {
                            var textAfter = new TextRange(caret, p.ContentEnd).Text;
                            e.Handled = true;
                            p.Inlines.Clear();
                            foreach (var inl in NotionMarkdownHelper.ParseInlineMarkdown(textAfter, NotionMarkdownHelper.TagH3)) p.Inlines.Add(inl);
                            NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagH3);
                            NotionEditorBox.CaretPosition = p.ContentStart;
                            return;
                        }
                        else if (textBeforeCaret == "-" || textBeforeCaret == "*")
                        {
                            var textAfter = new TextRange(caret, p.ContentEnd).Text;
                            e.Handled = true;
                            p.Inlines.Clear();
                            p.Inlines.Add(new Run(NotionMarkdownHelper.BulletPrefix));
                            foreach (var inl in NotionMarkdownHelper.ParseInlineMarkdown(textAfter, NotionMarkdownHelper.TagBullet)) p.Inlines.Add(inl);
                            NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagBullet);
                            NotionEditorBox.CaretPosition = p.ContentEnd;
                            return;
                        }
                        else if (textBeforeCaret == "[]" || textBeforeCaret == "[ ]" || textBeforeCaret == "- [ ]")
                        {
                            var textAfter = new TextRange(caret, p.ContentEnd).Text;
                            e.Handled = true;
                            p.Inlines.Clear();
                            p.Inlines.Add(new Run(NotionMarkdownHelper.TodoPrefix));
                            foreach (var inl in NotionMarkdownHelper.ParseInlineMarkdown(textAfter, NotionMarkdownHelper.TagTodo)) p.Inlines.Add(inl);
                            NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagTodo);
                            NotionEditorBox.CaretPosition = p.ContentEnd;
                            return;
                        }
                        else if (Regex.IsMatch(textBeforeCaret, @"^(\d+)\.$"))
                        {
                            var m = Regex.Match(textBeforeCaret, @"^(\d+)\.$");
                            int num = int.Parse(m.Groups[1].Value);
                            var textAfter = new TextRange(caret, p.ContentEnd).Text.TrimEnd('\r', '\n');
                            e.Handled = true;
                            p.Inlines.Clear();
                            var run = new Run($"{num}. ");
                            p.Inlines.Add(run);
                            if (!string.IsNullOrEmpty(textAfter))
                            {
                                foreach (var inl in NotionMarkdownHelper.ParseInlineMarkdown(textAfter, NotionMarkdownHelper.TagOrderedList)) p.Inlines.Add(inl);
                            }
                            p.Tag = NotionMarkdownHelper.TagOrderedList;
                            NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagOrderedList);
                            NotionEditorBox.CaretPosition = p.ContentStart.GetPositionAtOffset(run.Text.Length + 1) ?? p.ContentEnd;
                            return;
                        }
                        else if (textBeforeCaret == ">")
                        {
                            var textAfter = new TextRange(caret, p.ContentEnd).Text;
                            e.Handled = true;
                            p.Inlines.Clear();
                            foreach (var inl in NotionMarkdownHelper.ParseInlineMarkdown(textAfter, NotionMarkdownHelper.TagQuote)) p.Inlines.Add(inl);
                            NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagQuote);
                            NotionEditorBox.CaretPosition = p.ContentEnd;
                            return;
                        }
                    }
                }
                finally
                {
                    _isUpdatingEditor = false;
                }

                // Live inline formatting on Space: **word** -> Bold, `word` -> InlineCode
                var boldMatch = Regex.Match(textBeforeCaret, @"\*\*([^\*\s]+)\*\*$");
                if (boldMatch.Success)
                {
                    string boldWord = boldMatch.Groups[1].Value;
                    int tokenLen = boldMatch.Length;
                    var startPtr = caret.GetPositionAtOffset(-tokenLen);
                    if (startPtr != null)
                    {
                        e.Handled = true;
                        var r = new TextRange(startPtr, caret);
                        r.Text = boldWord;
                        r.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                        r.End.InsertTextInRun(" ");
                        NotionEditorBox.CaretPosition = r.End;
                        ResetCaretFormatting();
                        SyncMarkdownFromEditor();
                        return;
                    }
                }

                var codeMatch = Regex.Match(textBeforeCaret, @"`([^`\s]+)`$");
                if (codeMatch.Success)
                {
                    string codeWord = codeMatch.Groups[1].Value;
                    int tokenLen = codeMatch.Length;
                    var startPtr = caret.GetPositionAtOffset(-tokenLen);
                    if (startPtr != null)
                    {
                        e.Handled = true;
                        var r = new TextRange(startPtr, caret);
                        r.Text = codeWord;
                        r.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily("Cascadia Code, Consolas, Courier New, monospace"));
                        r.ApplyPropertyValue(TextElement.FontSizeProperty, 13.5);
                        var inlineCodeBg = _viewModel.IsDarkMode ? new SolidColorBrush(Color.FromRgb(45, 45, 50)) : new SolidColorBrush(Color.FromRgb(240, 240, 242));
                        var inlineCodeFg = _viewModel.IsDarkMode ? new SolidColorBrush(Color.FromRgb(224, 108, 117)) : new SolidColorBrush(Color.FromRgb(180, 40, 60));
                        r.ApplyPropertyValue(TextElement.BackgroundProperty, inlineCodeBg);
                        r.ApplyPropertyValue(TextElement.ForegroundProperty, inlineCodeFg);
                        r.End.InsertTextInRun(" ");
                        NotionEditorBox.CaretPosition = r.End;
                        ResetCaretFormatting();
                        SyncMarkdownFromEditor();
                        return;
                    }
                }
            }

            if (e.Key == Key.Enter)
            {
                // If caret is inside an existing Obsidian code block:
                if (NotionMarkdownHelper.IsCodeBlock(p))
                {
                    var fullCodeRange = new TextRange(p.ContentStart, p.ContentEnd);
                    var fullCodeText = fullCodeRange.Text.TrimEnd('\r', '\n');
                    var textAfterCaret = new TextRange(caret, p.ContentEnd).Text.TrimEnd('\r', '\n');

                    // If caret is on the closing fence line at the end, exit code block!
                    if (string.IsNullOrWhiteSpace(textAfterCaret) && fullCodeText.EndsWith("```"))
                    {
                        e.Handled = true;
                        _isUpdatingEditor = true;
                        try
                        {
                            var nextP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                            if (p.Parent is FlowDocument fDoc)
                            {
                                fDoc.Blocks.InsertAfter(p, nextP);
                                NotionEditorBox.CaretPosition = nextP.ContentStart;
                            }
                            if (_activeEditingCodeBlock == p)
                            {
                                CollapseCodeBlockToNormal(p);
                                _activeEditingCodeBlock = null;
                            }
                        }
                        finally
                        {
                            _isUpdatingEditor = false;
                        }
                        SyncMarkdownFromEditor();
                        return;
                    }

                    // Inside the code block: insert a newline / LineBreak inside the paragraph!
                    e.Handled = true;
                    _isUpdatingEditor = true;
                    try
                    {
                        EditingCommands.EnterLineBreak.Execute(null, NotionEditorBox);
                    }
                    finally
                    {
                        _isUpdatingEditor = false;
                    }
                    SyncMarkdownFromEditor();
                    return;
                }

                var fullText = new TextRange(p.ContentStart, p.ContentEnd).Text.TrimEnd('\r', '\n');

                if (fullText.Trim().StartsWith("```"))
                {
                    e.Handled = true;
                    _isUpdatingEditor = true;
                    try
                    {
                        var trimmed = fullText.Trim();
                        string lang = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : string.Empty;
                        if (p.Parent is FlowDocument doc)
                        {
                            var codeBlock = NotionMarkdownHelper.CreateCodeBlock(string.Empty, lang);
                            doc.Blocks.InsertBefore(p, codeBlock);
                            var nextP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                            doc.Blocks.InsertAfter(codeBlock, nextP);
                            doc.Blocks.Remove(p);

                            _activeEditingCodeBlock = codeBlock;
                            NotionMarkdownHelper.PopulateActiveCodeBlockParagraph(codeBlock, string.Empty, lang);
                            var codeLine = codeBlock.ContentStart.GetLineStartPosition(1) ?? codeBlock.ContentStart;
                            NotionEditorBox.CaretPosition = codeLine;
                        }
                    }
                    finally
                    {
                        _isUpdatingEditor = false;
                    }
                    SyncMarkdownFromEditor();
                    return;
                }

                if (NotionMarkdownHelper.IsHorizontalRule(fullText))
                {
                    e.Handled = true;
                    _isUpdatingEditor = true;
                    try
                    {
                        ConvertParagraphToDivider(p);
                    }
                    finally
                    {
                        _isUpdatingEditor = false;
                    }
                    SyncMarkdownFromEditor();
                    return;
                }

                bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                if (isShift)
                {
                    return;
                }

                NotionMarkdownHelper.TryTransformBlock(p);

                var tag = p.Tag as string ?? NotionMarkdownHelper.TagBody;

                // Case 1: Headings and Quotes
                if (tag == NotionMarkdownHelper.TagH1 || tag == NotionMarkdownHelper.TagH2 || 
                    tag == NotionMarkdownHelper.TagH3 || tag == NotionMarkdownHelper.TagQuote)
                {
                    e.Handled = true;
                    caret = NotionEditorBox.CaretPosition;
                    var textBefore = caret != null ? new TextRange(p.ContentStart, caret).Text : string.Empty;
                    var textAfter = caret != null ? new TextRange(caret, p.ContentEnd).Text.TrimEnd('\r', '\n') : string.Empty;

                    if (string.IsNullOrEmpty(textBefore))
                    {
                        var newP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                        InsertParagraphBefore(p, newP);
                        NotionEditorBox.CaretPosition = p.ContentStart;
                        ResetCaretFormatting();
                        SyncMarkdownFromEditor();
                        return;
                    }
                    else if (!string.IsNullOrEmpty(textAfter))
                    {
                        new TextRange(caret, p.ContentEnd).Text = string.Empty;
                        var newP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, textAfter);
                        InsertParagraphAfter(p, newP);
                        NotionEditorBox.CaretPosition = newP.ContentStart;
                        ResetCaretFormatting();
                        SyncMarkdownFromEditor();
                        return;
                    }
                    else
                    {
                        var newP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                        InsertParagraphAfter(p, newP);
                        NotionEditorBox.CaretPosition = newP.ContentStart;
                        ResetCaretFormatting();
                        SyncMarkdownFromEditor();
                        return;
                    }
                }

                // Case 2: Numbered List Continuation / Exit
                if (tag == NotionMarkdownHelper.TagOrderedList)
                {
                    var text = new TextRange(p.ContentStart, p.ContentEnd).Text.TrimEnd('\r', '\n');
                    var match = Regex.Match(text, @"^(\d+)\.\s*(.*)");
                    if (match.Success)
                    {
                        int currentNum = int.Parse(match.Groups[1].Value);
                        string content = match.Groups[2].Value.Trim();
                        if (string.IsNullOrEmpty(content))
                        {
                            e.Handled = true;
                            NotionMarkdownHelper.ConvertListItemToNormalText(p);
                            NotionEditorBox.CaretPosition = p.ContentStart;
                            ResetCaretFormatting();
                            return;
                        }
                        else
                        {
                            e.Handled = true;
                            var newP = NotionMarkdownHelper.CreateOrderedListParagraph(currentNum + 1, string.Empty, p.Margin.Left);
                            InsertParagraphAfter(p, newP);
                            NotionEditorBox.CaretPosition = newP.ContentEnd;
                            return;
                        }
                    }
                }

                // Case 3: Bullet List Continuation / Exit
                if (tag == NotionMarkdownHelper.TagBullet)
                {
                    var text = new TextRange(p.ContentStart, p.ContentEnd).Text.TrimEnd('\r', '\n');
                    int currentLevel = (int)Math.Max(0, (p.Margin.Left - 12) / 24);
                    var currentMarker = NotionMarkdownHelper.GetBulletMarker(currentLevel);

                    if (text.TrimEnd() == currentMarker.TrimEnd())
                    {
                        e.Handled = true;
                        if (currentLevel > 0)
                        {
                            IndentListItem(p, false);
                        }
                        else
                        {
                            NotionMarkdownHelper.ConvertListItemToNormalText(p);
                            NotionEditorBox.CaretPosition = p.ContentStart;
                            ResetCaretFormatting();
                        }
                        return;
                    }

                    e.Handled = true;
                    var newP = new Paragraph(new Run(currentMarker))
                    {
                        Tag = NotionMarkdownHelper.TagBullet,
                        Margin = p.Margin
                    };
                    NotionMarkdownHelper.ApplyStyle(newP, NotionMarkdownHelper.TagBullet);
                    InsertParagraphAfter(p, newP);
                    NotionEditorBox.CaretPosition = newP.ContentEnd;
                    return;
                }

                // Case 4: Todo List Continuation / Exit
                if (tag == NotionMarkdownHelper.TagTodo || tag == NotionMarkdownHelper.TagTodoDone)
                {
                    var text = new TextRange(p.ContentStart, p.ContentEnd).Text.TrimEnd('\r', '\n');
                    if (text == NotionMarkdownHelper.TodoPrefix.TrimEnd() || text == NotionMarkdownHelper.TodoDonePrefix.TrimEnd())
                    {
                        e.Handled = true;
                        NotionMarkdownHelper.ConvertListItemToNormalText(p);
                        NotionEditorBox.CaretPosition = p.ContentStart;
                        ResetCaretFormatting();
                        return;
                    }

                    e.Handled = true;
                    var newP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagTodo, string.Empty);
                    newP.Margin = p.Margin;
                    InsertParagraphAfter(p, newP);
                    NotionEditorBox.CaretPosition = newP.ContentEnd;
                    return;
                }

                // Case 5: Normal Body Text
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var newCaret = NotionEditorBox.CaretPosition;
                    var newP = newCaret?.Paragraph;
                    if (newP != null)
                    {
                        var pt = newP.Tag as string;
                        if (pt == NotionMarkdownHelper.TagH1 || pt == NotionMarkdownHelper.TagH2 || 
                            pt == NotionMarkdownHelper.TagH3 || pt == NotionMarkdownHelper.TagQuote)
                        {
                            NotionMarkdownHelper.ApplyStyle(newP, NotionMarkdownHelper.TagBody);
                        }

                        ResetCaretFormatting();

                        foreach (var inline in newP.Inlines)
                        {
                            if (inline is Run r && string.IsNullOrEmpty(r.Text))
                            {
                                r.Background = Brushes.Transparent;
                                r.FontFamily = new FontFamily("Segoe UI, -apple-system, sans-serif");
                                r.FontWeight = FontWeights.Normal;
                                r.FontStyle = FontStyles.Normal;
                                r.TextDecorations = null;
                            }
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            else if (e.Key == Key.Back)
            {
                if (caret == null || p == null) return;
                var tag = p.Tag as string;
                var fullText = new TextRange(p.ContentStart, p.ContentEnd).Text.TrimEnd('\r', '\n');

                if (p.PreviousBlock is BlockUIContainer prevContainer)
                {
                    if (caret.CompareTo(p.ContentStart) == 0)
                    {
                        if ((prevContainer.Tag as string) == NotionMarkdownHelper.TagDivider)
                        {
                            e.Handled = true;
                            NotionEditorBox.Document.Blocks.Remove(prevContainer);
                            SyncMarkdownFromEditor();
                            return;
                        }
                        else if (string.IsNullOrEmpty(fullText))
                        {
                            e.Handled = true;
                            NotionEditorBox.Document.Blocks.Remove(p);
                            if (prevContainer.Child is Border card && card.Tag is RichTextBox innerRtb)
                            {
                                innerRtb.Focus();
                                innerRtb.CaretPosition = innerRtb.Document.ContentEnd;
                            }
                            else
                            {
                                NotionEditorBox.CaretPosition = prevContainer.ElementEnd;
                            }
                            SyncMarkdownFromEditor();
                            return;
                        }
                    }
                }

                if (tag == NotionMarkdownHelper.TagH1 || tag == NotionMarkdownHelper.TagH2 || 
                    tag == NotionMarkdownHelper.TagH3 || tag == NotionMarkdownHelper.TagQuote)
                {
                    if (tag == NotionMarkdownHelper.TagQuote)
                    {
                        if (string.IsNullOrEmpty(fullText))
                        {
                            e.Handled = true;
                            NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagBody);
                            return;
                        }
                    }
                    else
                    {
                        var textUpToCaret = new TextRange(p.ContentStart, caret).Text;
                        int chevronLength = 0;
                        if (p.Inlines.FirstInline is Run chevronRun && (string?)chevronRun.Tag == NotionMarkdownHelper.TagFoldChevron)
                        {
                            chevronLength = chevronRun.Text.Length;
                        }

                        if (textUpToCaret.Length <= chevronLength)
                        {
                            e.Handled = true;
                            NotionMarkdownHelper.ConvertListItemToNormalText(p);
                            NotionEditorBox.CaretPosition = p.ContentStart;
                            ResetCaretFormatting();
                            SyncMarkdownFromEditor();
                            return;
                        }
                    }
                }

                if (NotionMarkdownHelper.IsCodeBlock(p))
                {
                    var cleanText = fullText.Trim();
                    if (string.IsNullOrEmpty(cleanText))
                    {
                        e.Handled = true;
                        p.Inlines.Clear();
                        p.Tag = NotionMarkdownHelper.TagBody;
                        NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagBody);
                        _activeEditingCodeBlock = null;
                        ResetCaretFormatting();
                        SyncMarkdownFromEditor();
                        return;
                    }
                }

                if (tag == NotionMarkdownHelper.TagBullet || tag == NotionMarkdownHelper.TagOrderedList ||
                    tag == NotionMarkdownHelper.TagTodo || tag == NotionMarkdownHelper.TagTodoDone)
                {
                    string marker = string.Empty;
                    bool isTodo = tag == NotionMarkdownHelper.TagTodo || tag == NotionMarkdownHelper.TagTodoDone;
                    double baseLeft = isTodo ? 4 : 12;
                    int currentLevel = (int)Math.Max(0, (p.Margin.Left - baseLeft) / 24);

                    if (tag == NotionMarkdownHelper.TagBullet)
                    {
                        var firstRun = p.Inlines.FirstInline as Run;
                        var firstText = firstRun?.Text ?? string.Empty;
                        foreach (var m in NotionMarkdownHelper.BulletMarkers)
                        {
                            if (firstText.StartsWith(m)) { marker = m; break; }
                        }
                        if (string.IsNullOrEmpty(marker))
                        {
                            if (firstText.StartsWith("- ")) marker = "- ";
                            else if (firstText.StartsWith("* ")) marker = "* ";
                            else marker = NotionMarkdownHelper.GetBulletMarker(currentLevel);
                        }
                    }
                    else if (tag == NotionMarkdownHelper.TagOrderedList)
                    {
                        var match = Regex.Match(fullText, @"^(\d+)\.\s*");
                        if (match.Success) marker = match.Value;
                        else marker = "1. ";
                    }
                    else
                    {
                        if (fullText.StartsWith(NotionMarkdownHelper.TodoPrefix)) marker = NotionMarkdownHelper.TodoPrefix;
                        else if (fullText.StartsWith(NotionMarkdownHelper.TodoDonePrefix)) marker = NotionMarkdownHelper.TodoDonePrefix;
                        else if (fullText.StartsWith("- [ ] ")) marker = "- [ ] ";
                        else if (fullText.StartsWith("- [x] ")) marker = "- [x] ";
                        else marker = NotionMarkdownHelper.TodoPrefix;
                    }

                    var textUpToCaret = new TextRange(p.ContentStart, caret).Text;
                    if (textUpToCaret.Length <= marker.Length)
                    {
                        e.Handled = true;
                        if (currentLevel > 0)
                        {
                            IndentListItem(p, false);
                        }
                        else
                        {
                            NotionMarkdownHelper.ConvertListItemToNormalText(p);
                            NotionEditorBox.CaretPosition = p.ContentStart;
                            ResetCaretFormatting();
                        }
                        SyncMarkdownFromEditor();
                        return;
                    }
                }
            }
            else if (e.Key == Key.Delete)
            {
                if (caret != null && p != null && caret.CompareTo(p.ContentEnd) == 0 && p.NextBlock is BlockUIContainer nextDivider && (nextDivider.Tag as string) == NotionMarkdownHelper.TagDivider)
                {
                    e.Handled = true;
                    NotionEditorBox.Document.Blocks.Remove(nextDivider);
                    SyncMarkdownFromEditor();
                    return;
                }

                if (NotionMarkdownHelper.IsCodeBlock(p))
                {
                    var cleanText = new TextRange(p.ContentStart, p.ContentEnd).Text.Trim();
                    if (string.IsNullOrEmpty(cleanText))
                    {
                        e.Handled = true;
                        p.Inlines.Clear();
                        p.Tag = NotionMarkdownHelper.TagBody;
                        NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagBody);
                        _activeEditingCodeBlock = null;
                        ResetCaretFormatting();
                        SyncMarkdownFromEditor();
                        return;
                    }
                }
            }
        }

        private void IndentListItem(Paragraph p, bool indent)
        {
            var tag = p.Tag as string ?? string.Empty;
            bool isTodo = tag == NotionMarkdownHelper.TagTodo || tag == NotionMarkdownHelper.TagTodoDone;
            double baseLeft = isTodo ? 4 : 12;
            int currentLevel = (int)Math.Max(0, (p.Margin.Left - baseLeft) / 24);
            int newLevel = indent ? Math.Min(3, currentLevel + 1) : Math.Max(0, currentLevel - 1);

            if (currentLevel == newLevel && !indent)
            {
                NotionMarkdownHelper.ConvertListItemToNormalText(p);
                NotionEditorBox.CaretPosition = p.ContentStart;
                ResetCaretFormatting();
                return;
            }

            p.Margin = isTodo 
                ? new Thickness(4 + newLevel * 24, 3, 0, 3) 
                : new Thickness(12 + newLevel * 24, 2, 0, 2);

            if (tag == NotionMarkdownHelper.TagBullet)
            {
                var newMarker = NotionMarkdownHelper.GetBulletMarker(newLevel);
                if (p.Inlines.FirstInline is Run firstRun)
                {
                    string matchedMarker = "";
                    foreach (var m in NotionMarkdownHelper.BulletMarkers)
                    {
                        if (firstRun.Text.StartsWith(m)) { matchedMarker = m; break; }
                    }
                    if (!string.IsNullOrEmpty(matchedMarker))
                    {
                        firstRun.Text = newMarker + firstRun.Text.Substring(matchedMarker.Length);
                    }
                    else if (firstRun.Text.StartsWith("- ") || firstRun.Text.StartsWith("* "))
                    {
                        firstRun.Text = newMarker + firstRun.Text.Substring(2);
                    }
                    else
                    {
                        firstRun.Text = newMarker + firstRun.Text;
                    }
                }
                else
                {
                    p.Inlines.InsertBefore(p.Inlines.FirstInline, new Run(newMarker));
                }

                if (p.Inlines.FirstInline is Run r)
                {
                    NotionEditorBox.CaretPosition = r.ContentStart.GetPositionAtOffset(newMarker.Length) ?? r.ContentEnd;
                }
            }
        }

        private void InsertParagraphAfter(Paragraph current, Paragraph newParagraph)
        {
            if (current.NextBlock != null)
            {
                NotionEditorBox.Document.Blocks.InsertBefore(current.NextBlock, newParagraph);
            }
            else
            {
                NotionEditorBox.Document.Blocks.Add(newParagraph);
            }
        }

        private void InsertParagraphBefore(Paragraph current, Paragraph newParagraph)
        {
            NotionEditorBox.Document.Blocks.InsertBefore(current, newParagraph);
        }

        private void NotionEditorBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsEventFromEmbeddedControl(e)) return;
            var point = e.GetPosition(NotionEditorBox);

            var (borderTable, colIndex, _) = FindTableColumnBorderNear(point);
            if (borderTable != null && colIndex >= 0)
            {
                _isDraggingColumnBorder = true;
                _resizingTable = borderTable;
                _resizingColIndex = colIndex;
                _dragStartX = point.X;

                while (_resizingTable.Columns.Count <= colIndex)
                {
                    _resizingTable.Columns.Add(new TableColumn { Width = new GridLength(160, GridUnitType.Pixel) });
                }

                foreach (var c in _resizingTable.Columns)
                {
                    if (!c.Width.IsAbsolute)
                    {
                        c.Width = new GridLength(160, GridUnitType.Pixel);
                    }
                }

                var col = _resizingTable.Columns[colIndex];
                _initialColWidth = col.Width.Value;

                NotionEditorBox.CaptureMouse();
                Mouse.OverrideCursor = Cursors.SizeWE;
                e.Handled = true;
                return;
            }

            if (NotionEditorBox.Document != null && NotionEditorBox.Document.Blocks.FirstBlock is Table firstTable)
            {
                var firstStartRect = firstTable.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                if (point.Y < firstStartRect.Top && point.Y >= 0)
                {
                    e.Handled = true;
                    ExitTableToAbove(firstTable);
                    return;
                }
            }

            if (NotionEditorBox.Document != null)
            {
                foreach (var block in NotionEditorBox.Document.Blocks)
                {
                    if (block is Table table && table.RowGroups.Count > 0 && table.RowGroups[0].Rows.Count > 0)
                    {
                        var lastRow = table.RowGroups[0].Rows[table.RowGroups[0].Rows.Count - 1];
                        if (lastRow.Cells.Count > 0)
                        {
                            var lastCell = lastRow.Cells[lastRow.Cells.Count - 1];
                            var cellEndRect = lastCell.ContentEnd.GetCharacterRect(LogicalDirection.Backward);

                            if (point.Y > cellEndRect.Bottom)
                            {
                                bool isDirectlyBelow = false;
                                if (table.NextBlock == null)
                                {
                                    isDirectlyBelow = true;
                                }
                                else if (table.NextBlock is Paragraph)
                                {
                                    var nextStart = table.NextBlock.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                                    if (point.Y <= nextStart.Bottom + 15)
                                    {
                                        isDirectlyBelow = true;
                                    }
                                }

                                if (isDirectlyBelow)
                                {
                                    e.Handled = true;
                                    ExitTableToBelow(table);
                                    return;
                                }
                            }
                        }
                    }
                }

                var lastBlock = NotionEditorBox.Document.Blocks.LastBlock;
                if (lastBlock != null)
                {
                    var lastEndRect = lastBlock.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                    if (point.Y > lastEndRect.Bottom)
                    {
                        e.Handled = true;
                        Paragraph? targetP = lastBlock as Paragraph;
                        if (targetP == null)
                        {
                            targetP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                            NotionEditorBox.Document.Blocks.Add(targetP);
                        }
                        NotionEditorBox.CaretPosition = targetP.ContentEnd;
                        NotionEditorBox.Focus();
                        ResetCaretFormatting();
                        TableToolbar.Visibility = Visibility.Collapsed;
                        return;
                    }
                }
            }

            var textPos = NotionEditorBox.GetPositionFromPoint(point, true);
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
                            NotionMarkdownHelper.ToggleHeadingFold(p, NotionEditorBox);
                            return;
                        }
                    }
                    else if (tag == NotionMarkdownHelper.TagTodo || tag == NotionMarkdownHelper.TagTodoDone)
                    {
                        var textRange = new TextRange(p.ContentStart, p.ContentEnd);
                        var raw = textRange.Text.TrimEnd('\r', '\n');

                        var rangeToClick = new TextRange(p.ContentStart, textPos);
                        if (rangeToClick.Text.Length <= 3)
                        {
                            e.Handled = true;

                            if (tag == NotionMarkdownHelper.TagTodo)
                            {
                                var content = raw.StartsWith(NotionMarkdownHelper.TodoPrefix) ? raw.Substring(NotionMarkdownHelper.TodoPrefix.Length) : raw;
                                p.Inlines.Clear();
                                p.Inlines.Add(new Run(NotionMarkdownHelper.TodoDonePrefix + content));
                                NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagTodoDone);
                            }
                            else
                            {
                                var content = raw.StartsWith(NotionMarkdownHelper.TodoDonePrefix) ? raw.Substring(NotionMarkdownHelper.TodoDonePrefix.Length) : raw;
                                p.Inlines.Clear();
                                p.Inlines.Add(new Run(NotionMarkdownHelper.TodoPrefix + content));
                                NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagTodo);
                            }
                        }
                    }
                }
            }
        }

        private void NotionEditorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isUpdatingEditor)
            {
                var caret = NotionEditorBox.CaretPosition;
                var p = caret != null ? NotionMarkdownHelper.GetContainingParagraph(caret) : null;
                if (p != null)
                {
                    if (NotionMarkdownHelper.IsCodeBlock(p))
                    {
                        // Inside a code block: do NOT transform comments into headings or lists!
                        _isUpdatingFromNotion = true;
                        try
                        {
                            var markdown = NotionMarkdownHelper.FlowDocumentToMarkdown(NotionEditorBox.Document);
                            _viewModel.EditorContent = markdown;
                            Dispatcher.InvokeAsync(UpdateResponsiveImages, System.Windows.Threading.DispatcherPriority.Background);
                        }
                        finally
                        {
                            _isUpdatingFromNotion = false;
                        }
                        return;
                    }

                    var tag = p.Tag as string ?? NotionMarkdownHelper.TagBody;
                    var text = new TextRange(p.ContentStart, p.ContentEnd).Text.TrimEnd('\r', '\n');
                    bool isBody = tag == NotionMarkdownHelper.TagBody;
                    bool isHeadingConversion = text.Contains("# ") || text.StartsWith("#");

                    if (isBody || isHeadingConversion)
                    {
                        if (NotionMarkdownHelper.IsHorizontalRule(text))
                        {
                            _isUpdatingEditor = true;
                            try
                            {
                                ConvertParagraphToDivider(p);
                            }
                            finally
                            {
                                _isUpdatingEditor = false;
                            }
                        }
                        else if (text.StartsWith("# ") || text.StartsWith("## ") || text.StartsWith("### ") ||
                            text.StartsWith("- ") || text.StartsWith("* ") || text.StartsWith("[] ") ||
                            text.StartsWith("- [ ] ") || text.StartsWith("> ") ||
                            Regex.IsMatch(text, @"^\d+\.\s+"))
                        {
                            _isUpdatingEditor = true;
                            try
                            {
                                if (NotionMarkdownHelper.TryTransformBlock(p))
                                {
                                    NotionEditorBox.CaretPosition = p.ContentEnd;
                                }
                            }
                            finally
                            {
                                _isUpdatingEditor = false;
                            }
                        }
                    }
                }

                _isUpdatingFromNotion = true;
                try
                {
                    var markdown = NotionMarkdownHelper.FlowDocumentToMarkdown(NotionEditorBox.Document);
                    _viewModel.EditorContent = markdown;
                    Dispatcher.InvokeAsync(UpdateResponsiveImages, System.Windows.Threading.DispatcherPriority.Background);
                }
                finally
                {
                    _isUpdatingFromNotion = false;
                }
            }
        }

        private void NoteTitleBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Down)
            {
                e.Handled = true;
                NotionEditorBox.Focus();
                if (NotionEditorBox.Document.Blocks.FirstBlock != null)
                {
                    NotionEditorBox.CaretPosition = NotionEditorBox.Document.Blocks.FirstBlock.ContentStart;
                }
            }
        }

        private void NewTagInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (_viewModel.ConfirmAddTagCommand.CanExecute(null))
                {
                    _viewModel.ConfirmAddTagCommand.Execute(null);
                }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (_viewModel.CancelAddTagCommand.CanExecute(null))
                {
                    _viewModel.CancelAddTagCommand.Execute(null);
                }
            }
        }

        private void NewTagInputBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox tb && tb.IsVisible)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    tb.Focus();
                    tb.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void NotionEditorBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var itemStyle = TryFindResource("NotionMenuItemStyle") as Style;
            var menu = NotionEditorBox.ContextMenu;
            if (menu == null) return;

            menu.Items.Clear();
            menu.PlacementTarget = NotionEditorBox;

            // Cut / Copy / Paste
            menu.Items.Add(new MenuItem { Header = "Cut",   Command = ApplicationCommands.Cut,   Style = itemStyle, Icon = CreateMenuIcon("IconEditGeometry") });
            menu.Items.Add(new MenuItem { Header = "Copy",  Command = ApplicationCommands.Copy,  Style = itemStyle, Icon = CreateMenuIcon("IconCopyGeometry") });
            menu.Items.Add(new MenuItem { Header = "Paste", Command = ApplicationCommands.Paste, Style = itemStyle });

            menu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

            // Inline Formatting
            menu.Items.Add(new MenuItem { Header = "Bold (Ctrl+B)",  Command = EditingCommands.ToggleBold,   Style = itemStyle });
            menu.Items.Add(new MenuItem { Header = "Italic (Ctrl+I)", Command = EditingCommands.ToggleItalic, Style = itemStyle });

            var strikeItem = new MenuItem { Header = "Strikethrough", Style = itemStyle };
            strikeItem.Click += (s, args) => ToggleSelectionStrikethrough();
            menu.Items.Add(strikeItem);

            var codeItem = new MenuItem { Header = "Inline Code", Style = itemStyle };
            codeItem.Click += (s, args) => ToggleSelectionInlineCode();
            menu.Items.Add(codeItem);

            menu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

            // Turn Into Block (submenu)
            var turnIntoItem = new MenuItem { Header = "Turn Into Block ›", Style = itemStyle };

            var h1Item = new MenuItem { Header = "Heading 1 (#)", Style = itemStyle };
            h1Item.Click += (s, args) => TransformCaretBlock(NotionMarkdownHelper.TagH1);
            var h2Item = new MenuItem { Header = "Heading 2 (##)", Style = itemStyle };
            h2Item.Click += (s, args) => TransformCaretBlock(NotionMarkdownHelper.TagH2);
            var h3Item = new MenuItem { Header = "Heading 3 (###)", Style = itemStyle };
            h3Item.Click += (s, args) => TransformCaretBlock(NotionMarkdownHelper.TagH3);
            var bulletItem = new MenuItem { Header = "Bullet List (•)", Style = itemStyle };
            bulletItem.Click += (s, args) => TransformCaretBlock(NotionMarkdownHelper.TagBullet);
            var todoItem = new MenuItem { Header = "To-do List (☐)", Style = itemStyle };
            todoItem.Click += (s, args) => TransformCaretBlock(NotionMarkdownHelper.TagTodo);
            var quoteItem = new MenuItem { Header = "Quote (>)", Style = itemStyle };
            quoteItem.Click += (s, args) => TransformCaretBlock(NotionMarkdownHelper.TagQuote);
            var blockCodeItem = new MenuItem { Header = "Code Block (```)", Style = itemStyle };
            blockCodeItem.Click += (s, args) => TransformCaretBlock(NotionMarkdownHelper.TagCodeBlock);

            turnIntoItem.Items.Add(h1Item);
            turnIntoItem.Items.Add(h2Item);
            turnIntoItem.Items.Add(h3Item);
            turnIntoItem.Items.Add(bulletItem);
            turnIntoItem.Items.Add(todoItem);
            turnIntoItem.Items.Add(quoteItem);
            turnIntoItem.Items.Add(blockCodeItem);

            menu.Items.Add(turnIntoItem);

            menu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

            // Insert Divider Line
            var dividerItem = new MenuItem { Header = "Insert Divider Line (---)", Style = itemStyle, Icon = CreateMenuIcon("IconDividerGeometry") };
            dividerItem.Click += (s, args) => InsertDividerInEditor();
            menu.Items.Add(dividerItem);

            // Insert Table
            var tableItem = new MenuItem { Header = "Insert Table (3×3)", Style = itemStyle, Icon = CreateMenuIcon("IconTableGeometry") };
            tableItem.Click += (s, args) => InsertTableInEditor();
            menu.Items.Add(tableItem);

            // Table Actions (if inside a table cell)
            var tableCell = GetParentTableCell(NotionEditorBox.CaretPosition);
            if (tableCell != null)
            {
                menu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

                var tableSubmenu = new MenuItem { Header = "Table Actions ›", Style = itemStyle };

                var rowAbove = new MenuItem { Header = "Insert Row Above", Icon = CreateMenuIcon("IconTableRowAboveGeometry"), Style = itemStyle };
                rowAbove.Click += (s, a) => { NotionMarkdownHelper.InsertRow(tableCell, true); SyncMarkdownFromEditor(); };
                tableSubmenu.Items.Add(rowAbove);

                var rowBelow = new MenuItem { Header = "Insert Row Below", Icon = CreateMenuIcon("IconTableRowBelowGeometry"), Style = itemStyle };
                rowBelow.Click += (s, a) => { NotionMarkdownHelper.InsertRow(tableCell, false); SyncMarkdownFromEditor(); };
                tableSubmenu.Items.Add(rowBelow);

                var delRow = new MenuItem { Header = "Delete Row", Icon = CreateMenuIcon("IconTableRowDeleteGeometry"), Style = itemStyle };
                delRow.Click += (s, a) => { NotionMarkdownHelper.DeleteRow(tableCell); SyncMarkdownFromEditor(); };
                tableSubmenu.Items.Add(delRow);

                tableSubmenu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

                var colLeft = new MenuItem { Header = "Insert Column Left", Icon = CreateMenuIcon("IconTableColLeftGeometry"), Style = itemStyle };
                colLeft.Click += (s, a) => { NotionMarkdownHelper.InsertColumn(tableCell, true); SyncMarkdownFromEditor(); UpdateDocumentLayoutWidth(); };
                tableSubmenu.Items.Add(colLeft);

                var colRight = new MenuItem { Header = "Insert Column Right", Icon = CreateMenuIcon("IconTableColRightGeometry"), Style = itemStyle };
                colRight.Click += (s, a) => { NotionMarkdownHelper.InsertColumn(tableCell, false); SyncMarkdownFromEditor(); UpdateDocumentLayoutWidth(); };
                tableSubmenu.Items.Add(colRight);

                var delCol = new MenuItem { Header = "Delete Column", Icon = CreateMenuIcon("IconTableColDeleteGeometry"), Style = itemStyle };
                delCol.Click += (s, a) => { NotionMarkdownHelper.DeleteColumn(tableCell); SyncMarkdownFromEditor(); UpdateDocumentLayoutWidth(); };
                tableSubmenu.Items.Add(delCol);

                tableSubmenu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

                var alignLeft = new MenuItem { Header = "Align Left", Icon = CreateMenuIcon("IconAlignLeftGeometry"), Style = itemStyle };
                alignLeft.Click += (s, a) => { NotionMarkdownHelper.AlignCellText(tableCell, TextAlignment.Left); SyncMarkdownFromEditor(); };
                tableSubmenu.Items.Add(alignLeft);

                var alignCenter = new MenuItem { Header = "Align Center", Icon = CreateMenuIcon("IconAlignCenterGeometry"), Style = itemStyle };
                alignCenter.Click += (s, a) => { NotionMarkdownHelper.AlignCellText(tableCell, TextAlignment.Center); SyncMarkdownFromEditor(); };
                tableSubmenu.Items.Add(alignCenter);

                var alignRight = new MenuItem { Header = "Align Right", Icon = CreateMenuIcon("IconAlignRightGeometry"), Style = itemStyle };
                alignRight.Click += (s, a) => { NotionMarkdownHelper.AlignCellText(tableCell, TextAlignment.Right); SyncMarkdownFromEditor(); };
                tableSubmenu.Items.Add(alignRight);

                tableSubmenu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

                var insertLineBelow = new MenuItem { Header = "Insert Line Below Table", Icon = CreateMenuIcon("IconPlusGeometry"), Style = itemStyle };
                insertLineBelow.Click += (s, a) => {
                    var curRow = tableCell.Parent as TableRow;
                    var curGroup = curRow?.Parent as TableRowGroup;
                    var tbl = curGroup?.Parent as Table;
                    if (tbl != null) ExitTableToBelow(tbl);
                };
                tableSubmenu.Items.Add(insertLineBelow);

                var insertLineAbove = new MenuItem { Header = "Insert Line Above Table", Icon = CreateMenuIcon("IconPlusGeometry"), Style = itemStyle };
                insertLineAbove.Click += (s, a) => {
                    var curRow = tableCell.Parent as TableRow;
                    var curGroup = curRow?.Parent as TableRowGroup;
                    var tbl = curGroup?.Parent as Table;
                    if (tbl != null) ExitTableToAbove(tbl);
                };
                tableSubmenu.Items.Add(insertLineAbove);

                tableSubmenu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

                var delTable = new MenuItem { Header = "Delete Table", Icon = CreateMenuIcon("IconTableDeleteGeometry"), Style = itemStyle };
                delTable.Click += (s, a) => { NotionMarkdownHelper.DeleteTable(tableCell, NotionEditorBox); SyncMarkdownFromEditor(); TableToolbar.Visibility = Visibility.Collapsed; UpdateDocumentLayoutWidth(); };
                tableSubmenu.Items.Add(delTable);

                menu.Items.Add(tableSubmenu);
            }
        }

        private void ToggleSelectionStrikethrough()
        {
            var selection = NotionEditorBox.Selection;
            if (selection == null || selection.IsEmpty) return;

            var currentDecoration = selection.GetPropertyValue(Inline.TextDecorationsProperty);
            if (currentDecoration == TextDecorations.Strikethrough)
            {
                selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
            }
            else
            {
                selection.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
            }
        }

        private void ToggleSelectionInlineCode()
        {
            var selection = NotionEditorBox.Selection;
            if (selection == null || selection.IsEmpty) return;

            var currentFont = selection.GetPropertyValue(Inline.FontFamilyProperty) as FontFamily;
            if (currentFont != null && currentFont.Source.Contains("Consolas"))
            {
                selection.ApplyPropertyValue(Inline.FontFamilyProperty, new FontFamily("Segoe UI, -apple-system, sans-serif"));
                selection.ApplyPropertyValue(Inline.BackgroundProperty, Brushes.Transparent);
            }
            else
            {
                selection.ApplyPropertyValue(Inline.FontFamilyProperty, new FontFamily("Consolas, Courier New"));
                var bg = _viewModel.IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)) : new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xEE));
                selection.ApplyPropertyValue(Inline.BackgroundProperty, bg);
            }
        }

        private void TransformCaretBlock(string tag)
        {
            var caret = NotionEditorBox.CaretPosition;
            var p = caret?.Paragraph;
            if (p != null)
            {
                if (tag == NotionMarkdownHelper.TagCodeBlock)
                {
                    var rawText = new TextRange(p.ContentStart, p.ContentEnd).Text.TrimEnd('\r', '\n');
                    NotionMarkdownHelper.PopulateNormalCodeBlockParagraph(p, rawText, "text");
                    UpdateDocumentLayoutWidth();
                    SyncMarkdownFromEditor();
                    return;
                }

                NotionMarkdownHelper.ApplyStyle(p, tag);
                if (tag == NotionMarkdownHelper.TagQuote)
                {
                    foreach (var inline in p.Inlines)
                    {
                        if (inline is Run r)
                        {
                            r.FontStyle = FontStyles.Italic;
                            r.Background = Brushes.Transparent;
                        }
                    }
                }
                SyncMarkdownFromEditor();
            }
        }

        private void InsertDividerInEditor()
        {
            var caretP = NotionEditorBox.CaretPosition?.Paragraph;
            if (caretP != null && caretP.Parent is FlowDocument)
            {
                var tr = new TextRange(caretP.ContentStart, caretP.ContentEnd);
                if (string.IsNullOrWhiteSpace(tr.Text) || NotionMarkdownHelper.IsHorizontalRule(tr.Text))
                {
                    ConvertParagraphToDivider(caretP);
                    SyncMarkdownFromEditor();
                    return;
                }
            }

            var divider = NotionMarkdownHelper.CreateDividerBlock();
            if (caretP != null && caretP.Parent is FlowDocument doc)
            {
                doc.Blocks.InsertAfter(caretP, divider);
                var newP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                doc.Blocks.InsertAfter(divider, newP);
                NotionEditorBox.CaretPosition = newP.ContentStart;
            }
            else
            {
                NotionEditorBox.Document.Blocks.Add(divider);
                var newP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                NotionEditorBox.Document.Blocks.Add(newP);
                NotionEditorBox.CaretPosition = newP.ContentStart;
            }

            ResetCaretFormatting();
            SyncMarkdownFromEditor();
        }

        private void InsertTableInEditor()
        {
            var table = NotionMarkdownHelper.CreateTableBlock(3, 3);
            var caretP = NotionEditorBox.CaretPosition?.Paragraph;
            if (caretP != null)
            {
                var tr = new TextRange(caretP.ContentStart, caretP.ContentEnd);
                if (string.IsNullOrWhiteSpace(tr.Text))
                {
                    NotionEditorBox.Document.Blocks.InsertBefore(caretP, table);
                    NotionEditorBox.Document.Blocks.Remove(caretP);
                }
                else
                {
                    NotionEditorBox.Document.Blocks.InsertAfter(caretP, table);
                }
            }
            else
            {
                NotionEditorBox.Document.Blocks.Add(table);
            }

            if (table.NextBlock == null)
            {
                var trailingP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                NotionEditorBox.Document.Blocks.Add(trailingP);
            }

            if (table.RowGroups.Count > 0 && table.RowGroups[0].Rows.Count > 0 && table.RowGroups[0].Rows[0].Cells.Count > 0)
            {
                var firstCell = table.RowGroups[0].Rows[0].Cells[0];
                NotionEditorBox.CaretPosition = firstCell.ContentStart;
            }

            SyncMarkdownFromEditor();
            UpdateDocumentLayoutWidth();
        }

        private void SyncMarkdownFromEditor()
        {
            _isUpdatingFromNotion = true;
            try
            {
                _viewModel.EditorContent = NotionMarkdownHelper.FlowDocumentToMarkdown(NotionEditorBox.Document);
            }
            finally
            {
                _isUpdatingFromNotion = false;
            }
        }

        private void HandleCodeBlockSelectionChanged()
        {
            if (_isUpdatingEditor) return;

            // CRITICAL: If the user has an active text selection (e.g. Ctrl+A, dragging mouse, Shift+Arrows),
            // NEVER mutate code blocks or change CaretPosition! Mutating inlines or setting CaretPosition
            // will immediately collapse and destroy the user's selection.
            if (NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty)
            {
                return;
            }

            var caret = NotionEditorBox.CaretPosition;
            var caretP = NotionMarkdownHelper.GetContainingParagraph(caret);
            bool caretInCodeBlock = NotionMarkdownHelper.IsCodeBlock(caretP);

            if (caretInCodeBlock && caretP != null)
            {
                if (caretP != _activeEditingCodeBlock)
                {
                    _isUpdatingEditor = true;
                    try
                    {
                        if (_activeEditingCodeBlock != null && _activeEditingCodeBlock.Parent != null)
                        {
                            CollapseCodeBlockToNormal(_activeEditingCodeBlock);
                        }

                        _activeEditingCodeBlock = caretP;
                        bool alreadyActive = caretP.Resources["IsActiveCodeBlock"] as bool? ?? false;
                        if (!alreadyActive)
                        {
                            var code = NotionMarkdownHelper.ExtractCodeFromParagraph(caretP);
                            var lang = NotionMarkdownHelper.GetCodeBlockLanguage(caretP);
                            NotionMarkdownHelper.PopulateActiveCodeBlockParagraph(caretP, code, lang);

                            var codeLine = caretP.ContentStart.GetLineStartPosition(1) ?? caretP.ContentStart;
                            NotionEditorBox.CaretPosition = codeLine;
                        }
                    }
                    finally
                    {
                        _isUpdatingEditor = false;
                    }
                }
            }
            else
            {
                if (_activeEditingCodeBlock != null)
                {
                    bool isActuallyOutside = true;
                    if (caret != null && _activeEditingCodeBlock.Parent != null)
                    {
                        if (caret.CompareTo(_activeEditingCodeBlock.ContentStart) >= 0 &&
                            caret.CompareTo(_activeEditingCodeBlock.ContentEnd) <= 0)
                        {
                            isActuallyOutside = false;
                        }
                    }

                    if (isActuallyOutside)
                    {
                        _isUpdatingEditor = true;
                        try
                        {
                            if (_activeEditingCodeBlock.Parent != null)
                            {
                                CollapseCodeBlockToNormal(_activeEditingCodeBlock);
                            }
                            _activeEditingCodeBlock = null;
                        }
                        finally
                        {
                            _isUpdatingEditor = false;
                        }
                    }
                }
            }
        }

        private void CollapseCodeBlockToNormal(Paragraph p)
        {
            var rawText = new TextRange(p.ContentStart, p.ContentEnd).Text.Replace("\r\n", "\n").Trim();
            var code = NotionMarkdownHelper.ExtractCodeFromParagraph(p);
            var lang = NotionMarkdownHelper.ExtractLanguageFromParagraph(p);

            // If empty or only ``` without language remains, revert to normal body paragraph
            if (string.IsNullOrWhiteSpace(rawText) || rawText == "```")
            {
                p.Inlines.Clear();
                p.Tag = NotionMarkdownHelper.TagBody;
                p.DataContext = null;
                p.Resources.Remove("IsActiveCodeBlock");
                NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagBody);
                return;
            }

            double targetWidth = NotionEditorBox.Document.PageWidth > 50 
                ? NotionEditorBox.Document.PageWidth - 52 
                : (NotionEditorBox.ActualWidth > 70 ? NotionEditorBox.ActualWidth - 92 : 600);

            NotionMarkdownHelper.PopulateNormalCodeBlockParagraph(p, code, lang, targetWidth);
        }

        private Paragraph EnsureParagraphAfterCodeBlock(Paragraph codeBlock)
        {
            if (codeBlock.NextBlock is Paragraph p && !NotionMarkdownHelper.IsCodeBlock(p))
            {
                return p;
            }

            var newP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
            if (codeBlock.NextBlock != null)
            {
                NotionEditorBox.Document.Blocks.InsertBefore(codeBlock.NextBlock, newP);
            }
            else
            {
                NotionEditorBox.Document.Blocks.Add(newP);
            }
            return newP;
        }

        #endregion
    }
}
