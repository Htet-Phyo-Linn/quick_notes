using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using QuickNote.App.Helpers;

namespace QuickNote.App
{
    public partial class MainWindow
    {
        private void CanExecuteCopy(object sender, CanExecuteRoutedEventArgs e)
        {
            var focusedRtb = Keyboard.FocusedElement as RichTextBox;
            if (focusedRtb != null && focusedRtb != NotionEditorBox)
            {
                e.CanExecute = focusedRtb.Selection != null && !focusedRtb.Selection.IsEmpty;
                return;
            }
            e.CanExecute = NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty;
            e.Handled = true;
        }

        private void CanExecuteCut(object sender, CanExecuteRoutedEventArgs e)
        {
            var focusedRtb = Keyboard.FocusedElement as RichTextBox;
            if (focusedRtb != null && focusedRtb != NotionEditorBox)
            {
                e.CanExecute = focusedRtb.Selection != null && !focusedRtb.Selection.IsEmpty && !focusedRtb.IsReadOnly;
                return;
            }
            e.CanExecute = NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty && !NotionEditorBox.IsReadOnly;
            e.Handled = true;
        }

        private void OnNotionEditorBoxCopy(object sender, ExecutedRoutedEventArgs e)
        {
            var focusedRtb = Keyboard.FocusedElement as RichTextBox;
            if (focusedRtb != null && focusedRtb != NotionEditorBox)
            {
                return;
            }

            var sel = NotionEditorBox.Selection;
            if (sel == null || sel.IsEmpty) return;

            var md = GetSelectedMarkdown(sel);
            if (!string.IsNullOrEmpty(md))
            {
                try
                {
                    Clipboard.SetText(md);
                }
                catch { }
            }
            e.Handled = true;
        }

        private void OnNotionEditorBoxCut(object sender, ExecutedRoutedEventArgs e)
        {
            var focusedRtb = Keyboard.FocusedElement as RichTextBox;
            if (focusedRtb != null && focusedRtb != NotionEditorBox)
            {
                return;
            }

            var sel = NotionEditorBox.Selection;
            if (sel == null || sel.IsEmpty) return;

            var md = GetSelectedMarkdown(sel);
            if (!string.IsNullOrEmpty(md))
            {
                try
                {
                    Clipboard.SetText(md);
                }
                catch { }
            }

            DeleteSelection(sel);
            e.Handled = true;
        }

        private void CanExecuteDelete(object sender, CanExecuteRoutedEventArgs e)
        {
            var focusedRtb = Keyboard.FocusedElement as RichTextBox;
            if (focusedRtb != null && focusedRtb != NotionEditorBox)
            {
                return;
            }
            if (NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty && !NotionEditorBox.IsReadOnly)
            {
                e.CanExecute = true;
                e.Handled = true;
            }
        }

        private void OnNotionEditorBoxDelete(object sender, ExecutedRoutedEventArgs e)
        {
            var focusedRtb = Keyboard.FocusedElement as RichTextBox;
            if (focusedRtb != null && focusedRtb != NotionEditorBox)
            {
                return;
            }
            if (NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty)
            {
                DeleteSelection(NotionEditorBox.Selection);
                e.Handled = true;
            }
        }

        private void OnNotionEditorBoxBackspace(object sender, ExecutedRoutedEventArgs e)
        {
            var focusedRtb = Keyboard.FocusedElement as RichTextBox;
            if (focusedRtb != null && focusedRtb != NotionEditorBox)
            {
                return;
            }
            if (NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty)
            {
                DeleteSelection(NotionEditorBox.Selection);
                e.Handled = true;
            }
        }

        private static string CleanCodeBlockPartialText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace("\r\n", "\n").TrimEnd('\n');
            if (text.StartsWith(" \n"))
            {
                text = text.Substring(2);
            }
            else if (text.StartsWith(" "))
            {
                text = text.TrimStart(' ');
            }
            var lines = text.Split('\n').ToList();
            if (lines.Count > 0 && lines[0].TrimStart().StartsWith("```"))
            {
                lines.RemoveAt(0);
            }
            if (lines.Count > 0 && lines[^1].Trim() == "```")
            {
                lines.RemoveAt(lines.Count - 1);
            }
            return string.Join("\n", lines);
        }

        private static string StripFencesIfPresent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var norm = text.Replace("\r\n", "\n").Trim();
            if (norm.StartsWith("```") && norm.EndsWith("```") && norm.Length >= 6)
            {
                var lines = norm.Split('\n').ToList();
                if (lines[0].TrimStart().StartsWith("```"))
                {
                    lines.RemoveAt(0);
                }
                if (lines.Count > 0 && lines[^1].Trim() == "```")
                {
                    lines.RemoveAt(lines.Count - 1);
                }
                return string.Join("\n", lines);
            }
            return text.Replace("\r\n", "\n");
        }

        private string ParagraphToMarkdownForSelection(Paragraph p, TextPointer rangeStart, TextPointer rangeEnd)
        {
            if (NotionMarkdownHelper.IsCodeBlock(p))
            {
                var lang = NotionMarkdownHelper.GetCodeBlockLanguage(p);
                bool isFullBlock = rangeStart.CompareTo(p.ContentStart) <= 0 && rangeEnd.CompareTo(p.ContentEnd) >= 0;
                if (isFullBlock)
                {
                    var fullCode = NotionMarkdownHelper.ExtractCodeFromParagraph(p);
                    return $"```{lang}\n{fullCode}\n```";
                }
                else
                {
                    var rangeText = new TextRange(rangeStart, rangeEnd).Text;
                    return CleanCodeBlockPartialText(rangeText);
                }
            }

            var text = new TextRange(rangeStart, rangeEnd).Text.TrimEnd('\r', '\n');
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var tag = p.Tag as string ?? NotionMarkdownHelper.TagBody;
            return tag switch
            {
                NotionMarkdownHelper.TagH1 => text.StartsWith("# ") ? text : $"# {text}",
                NotionMarkdownHelper.TagH2 => text.StartsWith("## ") ? text : $"## {text}",
                NotionMarkdownHelper.TagH3 => text.StartsWith("### ") ? text : $"### {text}",
                NotionMarkdownHelper.TagQuote => text.StartsWith("> ") ? text : $"> {text}",
                NotionMarkdownHelper.TagBullet => text.StartsWith("- ") ? text : $"- {text}",
                NotionMarkdownHelper.TagTodo => text.StartsWith("- [ ] ") ? text : $"- [ ] {text}",
                NotionMarkdownHelper.TagTodoDone => text.StartsWith("- [x] ") ? text : $"- [x] {text}",
                _ => text
            };
        }

        private string GetSelectedMarkdown(TextSelection sel)
        {
            if (sel == null || sel.IsEmpty) return string.Empty;

            var doc = NotionEditorBox.Document;
            var start = sel.Start;
            var end = sel.End;

            // Single paragraph selection
            if (start.Paragraph != null && start.Paragraph == end.Paragraph)
            {
                var p = start.Paragraph;
                if (NotionMarkdownHelper.IsCodeBlock(p))
                {
                    if (start.CompareTo(p.ContentStart) <= 0 && end.CompareTo(p.ContentEnd) >= 0)
                    {
                        return NotionMarkdownHelper.BlockToMarkdown(p);
                    }
                    var selText = new TextRange(start, end).Text;
                    return CleanCodeBlockPartialText(selText);
                }
                var tag = p.Tag as string ?? NotionMarkdownHelper.TagBody;
                if (tag != NotionMarkdownHelper.TagBody)
                {
                    return ParagraphToMarkdownForSelection(p, start, end);
                }
                return sel.Text;
            }

            // Find all blocks intersecting the selection
            var intersectingBlocks = new List<Block>();
            foreach (var block in doc.Blocks)
            {
                if (block.ElementEnd.CompareTo(start) > 0 && block.ElementStart.CompareTo(end) < 0)
                {
                    // If selection ends right at the start of this block without selecting any content in it, skip it
                    if (block is Paragraph p && end.CompareTo(p.ContentStart) <= 0)
                    {
                        continue;
                    }
                    intersectingBlocks.Add(block);
                }
            }

            if (intersectingBlocks.Count == 0)
            {
                return sel.Text;
            }

            var parts = new List<(Block Block, string Markdown)>();
            for (int i = 0; i < intersectingBlocks.Count; i++)
            {
                var block = intersectingBlocks[i];
                bool isFirst = (i == 0);
                bool isLast = (i == intersectingBlocks.Count - 1);

                if (block is Paragraph p)
                {
                    TextPointer blockStart = isFirst ? start : p.ContentStart;
                    TextPointer blockEnd = isLast ? end : p.ContentEnd;

                    if (isLast && blockStart.CompareTo(blockEnd) >= 0)
                    {
                        continue;
                    }

                    if (NotionMarkdownHelper.IsCodeBlock(p))
                    {
                        var lang = NotionMarkdownHelper.GetCodeBlockLanguage(p);
                        string codeText;
                        if (!isFirst && !isLast)
                        {
                            codeText = NotionMarkdownHelper.ExtractCodeFromParagraph(p);
                        }
                        else
                        {
                            var rangeText = new TextRange(blockStart, blockEnd).Text;
                            rangeText = CleanCodeBlockPartialText(rangeText);
                            codeText = string.IsNullOrEmpty(rangeText) ? NotionMarkdownHelper.ExtractCodeFromParagraph(p) : rangeText;
                        }
                        if (!string.IsNullOrEmpty(codeText))
                        {
                            parts.Add((block, $"```{lang}\n{codeText}\n```"));
                            continue;
                        }
                    }

                    var md = ParagraphToMarkdownForSelection(p, blockStart, blockEnd);
                    if (isLast && string.IsNullOrEmpty(md))
                    {
                        continue;
                    }
                    parts.Add((block, md));
                }
                else
                {
                    parts.Add((block, NotionMarkdownHelper.BlockToMarkdown(block)));
                }
            }

            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                var (curBlock, curMd) = parts[i];
                if (i > 0)
                {
                    var (prevBlock, prevMd) = parts[i - 1];
                    sb.Append(NotionMarkdownHelper.GetBlockSeparator(prevBlock, prevMd, curBlock, curMd));
                }
                sb.Append(curMd);
            }

            return sb.ToString();
        }

        private void DeleteSelection(TextSelection sel)
        {
            if (sel == null || sel.IsEmpty) return;

            var doc = NotionEditorBox.Document;
            var start = sel.Start;
            var end = sel.End;

            var startP = NotionMarkdownHelper.GetContainingParagraph(start) ?? start.Paragraph;
            var endP = NotionMarkdownHelper.GetContainingParagraph(end) ?? end.Paragraph;

            // Single paragraph selection
            if (startP != null && startP == endP)
            {
                var p = startP;
                sel.Text = string.Empty;
                if (NotionMarkdownHelper.IsCodeBlock(p))
                {
                    var cleanText = new TextRange(p.ContentStart, p.ContentEnd).Text.Trim();
                    if (string.IsNullOrEmpty(cleanText) || cleanText == "```")
                    {
                        p.Inlines.Clear();
                        p.Tag = NotionMarkdownHelper.TagBody;
                        NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagBody);
                        _activeEditingCodeBlock = null;
                    }
                }
                SyncMarkdownFromEditor();
                return;
            }

            var intersectingBlocks = new List<Block>();
            foreach (var block in doc.Blocks)
            {
                if (block.ElementEnd.CompareTo(start) > 0 && block.ElementStart.CompareTo(end) < 0)
                {
                    intersectingBlocks.Add(block);
                }
            }

            if (intersectingBlocks.Count == 0)
            {
                sel.Text = string.Empty;
                SyncMarkdownFromEditor();
                return;
            }

            _isUpdatingEditor = true;
            try
            {
                Block firstBlock = intersectingBlocks.First();
                Block lastBlock = intersectingBlocks.Last();

                Block? prevBlock = firstBlock.PreviousBlock;
                Block? nextBlock = lastBlock.NextBlock;

                Paragraph? firstP = firstBlock as Paragraph;
                Paragraph? lastP = lastBlock as Paragraph;

                bool firstIsPartial = firstP != null && start.CompareTo(firstP.ContentStart) > 0;
                bool lastIsPartial = lastP != null && end.CompareTo(lastP.ContentEnd) < 0;

                if (firstIsPartial && firstP != null)
                {
                    try
                    {
                        new TextRange(start, firstP.ContentEnd).Text = string.Empty;
                    }
                    catch { }
                }

                if (lastIsPartial && lastP != null)
                {
                    try
                    {
                        new TextRange(lastP.ContentStart, end).Text = string.Empty;
                    }
                    catch { }
                }

                for (int i = 0; i < intersectingBlocks.Count; i++)
                {
                    var b = intersectingBlocks[i];
                    if (b == firstP && firstIsPartial) continue;
                    if (b == lastP && lastIsPartial) continue;

                    try
                    {
                        doc.Blocks.Remove(b);
                    }
                    catch { }
                }

                TextPointer targetCaret;

                if (firstIsPartial && lastIsPartial && firstP != null && lastP != null && firstP != lastP)
                {
                    try
                    {
                        var remainingInlines = lastP.Inlines.ToList();
                        lastP.Inlines.Clear();
                        foreach (var inl in remainingInlines)
                        {
                            firstP.Inlines.Add(inl);
                        }
                        doc.Blocks.Remove(lastP);
                    }
                    catch { }
                    targetCaret = firstP.ContentEnd;
                }
                else if (firstIsPartial && firstP != null && firstP.Parent != null)
                {
                    targetCaret = firstP.ContentEnd;
                }
                else if (lastIsPartial && lastP != null && lastP.Parent != null)
                {
                    targetCaret = lastP.ContentStart;
                }
                else if (prevBlock != null && prevBlock.Parent != null)
                {
                    targetCaret = prevBlock.ElementEnd;
                }
                else if (nextBlock != null && nextBlock.Parent != null)
                {
                    targetCaret = nextBlock.ElementStart;
                }
                else if (doc.Blocks.Count > 0)
                {
                    targetCaret = doc.Blocks.FirstBlock.ContentStart;
                }
                else
                {
                    var emptyP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                    doc.Blocks.Add(emptyP);
                    targetCaret = emptyP.ContentStart;
                }

                if (firstP != null && firstP.Parent != null && NotionMarkdownHelper.IsCodeBlock(firstP))
                {
                    var cleanText = new TextRange(firstP.ContentStart, firstP.ContentEnd).Text.Trim();
                    if (string.IsNullOrEmpty(cleanText) || cleanText == "```")
                    {
                        firstP.Inlines.Clear();
                        firstP.Tag = NotionMarkdownHelper.TagBody;
                        NotionMarkdownHelper.ApplyStyle(firstP, NotionMarkdownHelper.TagBody);
                        if (_activeEditingCodeBlock == firstP) _activeEditingCodeBlock = null;
                    }
                }

                if (lastP != null && lastP.Parent != null && NotionMarkdownHelper.IsCodeBlock(lastP))
                {
                    var cleanText = new TextRange(lastP.ContentStart, lastP.ContentEnd).Text.Trim();
                    if (string.IsNullOrEmpty(cleanText) || cleanText == "```")
                    {
                        lastP.Inlines.Clear();
                        lastP.Tag = NotionMarkdownHelper.TagBody;
                        NotionMarkdownHelper.ApplyStyle(lastP, NotionMarkdownHelper.TagBody);
                        if (_activeEditingCodeBlock == lastP) _activeEditingCodeBlock = null;
                    }
                }

                if (_activeEditingCodeBlock != null && _activeEditingCodeBlock.Parent == null)
                {
                    _activeEditingCodeBlock = null;
                }

                try
                {
                    NotionEditorBox.CaretPosition = targetCaret;
                    NotionEditorBox.Selection.Select(targetCaret, targetCaret);
                }
                catch { }
            }
            finally
            {
                _isUpdatingEditor = false;
            }

            SyncMarkdownFromEditor();
        }

        private void OnNotionEditorBoxPaste(object sender, DataObjectPastingEventArgs e)
        {
            var focusedRtb = Keyboard.FocusedElement as RichTextBox;
            if (focusedRtb != null && focusedRtb != NotionEditorBox)
                return;

            // ── Excel / TSV table: check BEFORE bitmap so Excel cell copies are treated as
            //    tables rather than the screenshot bitmap Excel also puts on the clipboard ──
            var rawText = (e.DataObject.GetData(DataFormats.UnicodeText) as string)
                        ?? (e.DataObject.GetData(DataFormats.Text) as string)
                        ?? (Clipboard.ContainsText() ? Clipboard.GetText() : null);

            if (!string.IsNullOrEmpty(rawText) && NotionMarkdownHelper.IsTsvTable(rawText, out var tsvRows))
            {
                e.CancelCommand();
                e.Handled = true;

                if (NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty)
                {
                    DeleteSelection(NotionEditorBox.Selection);
                }

                var caret = NotionEditorBox.CaretPosition;
                if (caret != null)
                {
                    var tableBlock = NotionMarkdownHelper.CreateTableBlockFromTsv(tsvRows);
                    var currentP = caret.Paragraph;
                    if (currentP != null)
                    {
                        var curText = new TextRange(currentP.ContentStart, currentP.ContentEnd).Text.Trim();
                        bool isCurrentEmpty = string.IsNullOrWhiteSpace(curText);

                        NotionEditorBox.Document.Blocks.InsertAfter(currentP, tableBlock);
                        if (isCurrentEmpty)
                        {
                            NotionEditorBox.Document.Blocks.Remove(currentP);
                        }

                        var trailingP = EnsureParagraphAfterTable(tableBlock);
                        NotionEditorBox.CaretPosition = trailingP.ContentStart;
                    }
                    else
                    {
                        NotionEditorBox.Document.Blocks.Add(tableBlock);
                        var trailingP = EnsureParagraphAfterTable(tableBlock);
                        NotionEditorBox.CaretPosition = trailingP.ContentStart;
                    }

                    UpdateDocumentLayoutWidth();
                    SyncMarkdownFromEditor();
                }
                return;
            }

            if (e.DataObject.GetDataPresent(DataFormats.Bitmap) || Clipboard.ContainsImage())
            {
                try
                {
                    var bitmap = Clipboard.GetImage() ?? e.DataObject.GetData(DataFormats.Bitmap) as BitmapSource;
                    if (bitmap != null)
                    {
                        e.CancelCommand();
                        e.Handled = true;

                        var notesRoot = _settingsService.Settings.NotesRoot;
                        if (string.IsNullOrWhiteSpace(notesRoot))
                        {
                            notesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QuickNote");
                        }

                        var assetsDir = Path.Combine(notesRoot, ".assets");
                        if (!Directory.Exists(assetsDir))
                        {
                            Directory.CreateDirectory(assetsDir);
                        }

                        var fileName = $"pasted_image_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 6)}.png";
                        var fullPath = Path.Combine(assetsDir, fileName);

                        using (var fileStream = new FileStream(fullPath, FileMode.Create))
                        {
                            BitmapEncoder encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            encoder.Save(fileStream);
                        }

                        var relPath = ".assets/" + fileName;
                        var imgBlock = NotionMarkdownHelper.CreateImageBlock(relPath, "Pasted Image", _settingsService.Settings.NotesRoot);

                        if (imgBlock != null)
                        {
                            if (NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty)
                            {
                                DeleteSelection(NotionEditorBox.Selection);
                            }

                            var caret = NotionEditorBox.CaretPosition;
                            if (caret != null)
                            {
                                var currentP = caret.Paragraph;
                                if (currentP != null)
                                {
                                    var curText = new TextRange(currentP.ContentStart, currentP.ContentEnd).Text.Trim();
                                    bool isCurrentEmpty = string.IsNullOrWhiteSpace(curText);

                                    NotionEditorBox.Document.Blocks.InsertAfter(currentP, imgBlock);
                                    if (isCurrentEmpty)
                                    {
                                        NotionEditorBox.Document.Blocks.Remove(currentP);
                                    }

                                    var nextP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                                    NotionEditorBox.Document.Blocks.InsertAfter(imgBlock, nextP);
                                    NotionEditorBox.CaretPosition = nextP.ContentStart;
                                }
                                else
                                {
                                    NotionEditorBox.Document.Blocks.Add(imgBlock);
                                    var nextP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                                    NotionEditorBox.Document.Blocks.Add(nextP);
                                    NotionEditorBox.CaretPosition = nextP.ContentStart;
                                }

                                UpdateResponsiveImages();
                                UpdateDocumentLayoutWidth();
                                SyncMarkdownFromEditor();
                            }
                        }
                    }
                }
                catch { }
            }
            else if (e.DataObject.GetDataPresent(DataFormats.FileDrop))
            {
                try
                {
                    var files = (string[])e.DataObject.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        var firstFile = files[0];
                        var ext = Path.GetExtension(firstFile).ToLowerInvariant();
                        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp" || ext == ".webp")
                        {
                            e.CancelCommand();
                            e.Handled = true;

                            var notesRoot = _settingsService.Settings.NotesRoot;
                            if (string.IsNullOrWhiteSpace(notesRoot))
                            {
                                notesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QuickNote");
                            }

                            var assetsDir = Path.Combine(notesRoot, ".assets");
                            if (!Directory.Exists(assetsDir))
                            {
                                Directory.CreateDirectory(assetsDir);
                            }

                            var fileName = $"image_{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileName(firstFile)}";
                            var fullPath = Path.Combine(assetsDir, fileName);

                            File.Copy(firstFile, fullPath, overwrite: true);

                            var relPath = ".assets/" + fileName;
                            var imgBlock = NotionMarkdownHelper.CreateImageBlock(relPath, Path.GetFileNameWithoutExtension(firstFile), _settingsService.Settings.NotesRoot);

                            if (imgBlock != null)
                            {
                                if (NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty)
                                {
                                    DeleteSelection(NotionEditorBox.Selection);
                                }

                                var caret = NotionEditorBox.CaretPosition;
                                if (caret != null)
                                {
                                    var currentP = caret.Paragraph;
                                    if (currentP != null)
                                    {
                                        var curText = new TextRange(currentP.ContentStart, currentP.ContentEnd).Text.Trim();
                                        bool isCurrentEmpty = string.IsNullOrWhiteSpace(curText);

                                        NotionEditorBox.Document.Blocks.InsertAfter(currentP, imgBlock);
                                        if (isCurrentEmpty)
                                        {
                                            NotionEditorBox.Document.Blocks.Remove(currentP);
                                        }

                                        var nextP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                                        NotionEditorBox.Document.Blocks.InsertAfter(imgBlock, nextP);
                                        NotionEditorBox.CaretPosition = nextP.ContentStart;
                                    }
                                    else
                                    {
                                        NotionEditorBox.Document.Blocks.Add(imgBlock);
                                        var nextP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                                        NotionEditorBox.Document.Blocks.Add(nextP);
                                        NotionEditorBox.CaretPosition = nextP.ContentStart;
                                    }

                                    UpdateResponsiveImages();
                                    UpdateDocumentLayoutWidth();
                                    SyncMarkdownFromEditor();
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            else if (e.DataObject.GetDataPresent(DataFormats.UnicodeText) || e.DataObject.GetDataPresent(DataFormats.Text))
            {
                try
                {
                    var text = (e.DataObject.GetData(DataFormats.UnicodeText) as string)
                            ?? (e.DataObject.GetData(DataFormats.Text) as string)
                            ?? Clipboard.GetText();

                    if (!string.IsNullOrEmpty(text))
                    {
                        e.CancelCommand();
                        e.Handled = true;

                        _isUpdatingEditor = true;
                        try
                        {
                            if (NotionEditorBox.Selection != null && !NotionEditorBox.Selection.IsEmpty)
                            {
                                DeleteSelection(NotionEditorBox.Selection);
                            }

                            var caret = NotionEditorBox.CaretPosition ?? NotionEditorBox.Document.ContentStart;
                            if (caret != null)
                            {
                                var curCodeP = NotionMarkdownHelper.GetContainingParagraph(caret);
                                if (curCodeP != null && NotionMarkdownHelper.IsCodeBlock(curCodeP))
                                {
                                    var codeToInsert = StripFencesIfPresent(text);
                                    bool isAct = curCodeP.Resources["IsActiveCodeBlock"] as bool? ?? false;
                                    if (!isAct)
                                    {
                                        var existingCode = NotionMarkdownHelper.ExtractCodeFromParagraph(curCodeP);
                                        var existingLang = NotionMarkdownHelper.GetCodeBlockLanguage(curCodeP);
                                        NotionMarkdownHelper.PopulateActiveCodeBlockParagraph(curCodeP, existingCode, existingLang);
                                        _activeEditingCodeBlock = curCodeP;
                                        var codeLine = curCodeP.ContentStart.GetLineStartPosition(1) ?? curCodeP.ContentStart;
                                        NotionEditorBox.CaretPosition = codeLine;
                                    }

                                    var sel = NotionEditorBox.Selection;
                                    if (sel != null && !sel.IsEmpty)
                                    {
                                        sel.Text = string.Empty;
                                    }

                                    var lines = codeToInsert.Replace("\r\n", "\n").Split('\n');
                                    for (int i = 0; i < lines.Length; i++)
                                    {
                                        if (i > 0)
                                        {
                                            EditingCommands.EnterLineBreak.Execute(null, NotionEditorBox);
                                        }
                                        if (!string.IsNullOrEmpty(lines[i]))
                                        {
                                            var curCaret = NotionEditorBox.CaretPosition;
                                            var r = new TextRange(curCaret, curCaret);
                                            r.Text = lines[i];
                                            NotionEditorBox.CaretPosition = r.End;
                                        }
                                    }
                                    return;
                                }

                                bool isMultiLine = text.Contains('\n');
                                bool hasBlockMarkdown = text.StartsWith("#") || text.StartsWith("```") ||
                                                        text.StartsWith("|") || text.StartsWith("- ") ||
                                                        text.StartsWith("* ") || text.StartsWith("> ") ||
                                                        Regex.IsMatch(text, @"^\d+\.\s+");

                                if (isMultiLine || hasBlockMarkdown)
                                {
                                    var tempDoc = NotionMarkdownHelper.MarkdownToFlowDocument(text, _settingsService.Settings.NotesRoot);
                                    if (tempDoc.Blocks.Count > 0)
                                    {
                                        var blocksToInsert = tempDoc.Blocks.ToList();
                                        tempDoc.Blocks.Clear();

                                        // If multiple blocks were parsed and the last block is an empty body paragraph,
                                        // remove it so pasting does not leave an unwanted trailing blank line.
                                        if (blocksToInsert.Count > 1 &&
                                            blocksToInsert.Last() is Paragraph lastPara &&
                                            (lastPara.Tag as string == NotionMarkdownHelper.TagBody) &&
                                            string.IsNullOrWhiteSpace(new TextRange(lastPara.ContentStart, lastPara.ContentEnd).Text))
                                        {
                                            blocksToInsert.RemoveAt(blocksToInsert.Count - 1);
                                        }

                                        var currentP = NotionMarkdownHelper.GetContainingParagraph(caret) ?? caret.Paragraph;
                                        if (currentP != null)
                                        {
                                            var curText = new TextRange(currentP.ContentStart, currentP.ContentEnd).Text.Trim();
                                            bool isCurrentEmpty = string.IsNullOrWhiteSpace(curText);

                                            Block insertPoint = currentP;
                                            Paragraph? tailP = null;

                                            if (!isCurrentEmpty)
                                            {
                                                var textAfterCaret = new TextRange(caret, currentP.ContentEnd).Text.TrimEnd('\r', '\n');
                                                if (!string.IsNullOrEmpty(textAfterCaret))
                                                {
                                                    new TextRange(caret, currentP.ContentEnd).Text = string.Empty;
                                                    tailP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, textAfterCaret);
                                                }
                                            }

                                            Block lastBlock = insertPoint;
                                            foreach (var block in blocksToInsert)
                                            {
                                                NotionEditorBox.Document.Blocks.InsertAfter(lastBlock, block);
                                                lastBlock = block;
                                            }

                                            if (isCurrentEmpty)
                                            {
                                                NotionEditorBox.Document.Blocks.Remove(currentP);
                                            }

                                            if (tailP != null)
                                            {
                                                NotionEditorBox.Document.Blocks.InsertAfter(lastBlock, tailP);
                                                NotionEditorBox.CaretPosition = tailP.ContentStart;
                                            }
                                            else
                                            {
                                                if (lastBlock is Table t)
                                                {
                                                    var trailingP = EnsureParagraphAfterTable(t);
                                                    NotionEditorBox.CaretPosition = trailingP.ContentStart;
                                                }
                                                else if (lastBlock is BlockUIContainer)
                                                {
                                                    var trailingP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
                                                    NotionEditorBox.Document.Blocks.InsertAfter(lastBlock, trailingP);
                                                    NotionEditorBox.CaretPosition = trailingP.ContentStart;
                                                }
                                                else if (lastBlock is Paragraph codeP && NotionMarkdownHelper.IsCodeBlock(codeP))
                                                {
                                                    var trailingP = EnsureParagraphAfterCodeBlock(codeP);
                                                    NotionEditorBox.CaretPosition = trailingP.ContentStart;
                                                }
                                                else
                                                {
                                                    NotionEditorBox.CaretPosition = lastBlock.ContentEnd;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            foreach (var block in blocksToInsert)
                                            {
                                                NotionEditorBox.Document.Blocks.Add(block);
                                            }
                                        }

                                        UpdateResponsiveImages();
                                        UpdateDocumentLayoutWidth();
                                    }
                                }
                                else
                                {
                                    // Single line: check if contains inline markdown
                                    var inlines = NotionMarkdownHelper.ParseInlineMarkdown(text);
                                    if (inlines.Count == 1 && inlines[0] is Run singleRun && singleRun.Tag == null && singleRun.FontWeight == FontWeights.Normal && singleRun.FontStyle == FontStyles.Normal)
                                    {
                                        caret.InsertTextInRun(text);
                                    }
                                    else
                                    {
                                        var currentP = NotionMarkdownHelper.GetContainingParagraph(caret) ?? caret.Paragraph;
                                        if (currentP != null)
                                        {
                                            var curText = new TextRange(currentP.ContentStart, currentP.ContentEnd).Text.Trim();
                                            if (string.IsNullOrWhiteSpace(curText))
                                            {
                                                var newP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, text);
                                                NotionEditorBox.Document.Blocks.InsertAfter(currentP, newP);
                                                NotionEditorBox.Document.Blocks.Remove(currentP);
                                                NotionEditorBox.CaretPosition = newP.ContentEnd;
                                            }
                                            else
                                            {
                                                var textBefore = new TextRange(currentP.ContentStart, caret).Text;
                                                var textAfter = new TextRange(caret, currentP.ContentEnd).Text.TrimEnd('\r', '\n');
                                                var combined = textBefore + text + textAfter;
                                                var tag = currentP.Tag as string ?? NotionMarkdownHelper.TagBody;
                                                currentP.Inlines.Clear();
                                                foreach (var inl in NotionMarkdownHelper.ParseInlineMarkdown(combined, tag))
                                                {
                                                    currentP.Inlines.Add(inl);
                                                }
                                                NotionMarkdownHelper.ApplyStyle(currentP, tag);
                                                NotionEditorBox.CaretPosition = currentP.ContentEnd;
                                            }
                                        }
                                        else
                                        {
                                            caret.InsertTextInRun(text);
                                        }
                                    }
                                }
                            }
                        }
                        finally
                        {
                            _isUpdatingEditor = false;
                        }

                        SyncMarkdownFromEditor();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuickNote] Paste error: {ex}");
                }
            }
        }
    }
}
