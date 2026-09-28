using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickNote.App.Helpers
{
    public static partial class NotionMarkdownHelper
    {
        public static Paragraph CreateParagraph(string tag, string text)
        {
            var p = new Paragraph { Tag = tag };

            if (tag == TagBullet)
            {
                string content = text;
                string marker = BulletPrefix;
                foreach (var m in BulletMarkers)
                {
                    if (content.StartsWith(m))
                    {
                        marker = m;
                        content = content.Substring(m.Length);
                        break;
                    }
                }
                if (content.StartsWith("- ")) content = content.Substring(2);
                else if (content.StartsWith("* ")) content = content.Substring(2);

                p.Inlines.Add(new Run(marker));
                foreach (var inl in ParseInlineMarkdown(content, tag))
                {
                    p.Inlines.Add(inl);
                }
            }
            else if (tag == TagTodo)
            {
                string content = text.StartsWith(TodoPrefix) ? text.Substring(TodoPrefix.Length) : text;
                p.Inlines.Add(new Run(TodoPrefix));
                foreach (var inl in ParseInlineMarkdown(content, tag))
                {
                    p.Inlines.Add(inl);
                }
            }
            else if (tag == TagTodoDone)
            {
                string content = text.StartsWith(TodoDonePrefix) ? text.Substring(TodoDonePrefix.Length) : text;
                p.Inlines.Add(new Run(TodoDonePrefix));
                foreach (var inl in ParseInlineMarkdown(content, tag))
                {
                    p.Inlines.Add(inl);
                }
            }
            else if (tag == TagH1 || tag == TagH2 || tag == TagH3)
            {
                p.Inlines.Add(CreateFoldChevron(false));
                foreach (var inl in ParseInlineMarkdown(text, tag))
                {
                    p.Inlines.Add(inl);
                }
            }
            else
            {
                foreach (var inl in ParseInlineMarkdown(text, tag))
                {
                    p.Inlines.Add(inl);
                }
            }

            ApplyStyle(p, tag);
            return p;
        }

        public static Run CreateFoldChevron(bool isFolded = false)
        {
            return new Run(isFolded ? "› " : "˅ ")
            {
                Foreground = IsDarkMode 
                    ? new SolidColorBrush(Color.FromRgb(140, 140, 140)) 
                    : new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                Tag = TagFoldChevron,
                FontWeight = FontWeights.Normal,
                BaselineAlignment = BaselineAlignment.Center
            };
        }

        public static Run CreateFoldEllipsis()
        {
            return new Run(" ...")
            {
                Foreground = IsDarkMode 
                    ? new SolidColorBrush(Color.FromRgb(120, 120, 120)) 
                    : new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                Tag = TagFoldEllipsis,
                FontStyle = FontStyles.Italic,
                FontWeight = FontWeights.Normal
            };
        }

        public static void ToggleHeadingFold(Paragraph p, RichTextBox editor)
        {
            if (p == null || editor == null || editor.Document == null) return;
            var tag = p.Tag as string;
            if (tag != TagH1 && tag != TagH2 && tag != TagH3) return;

            var doc = editor.Document;

            if (FoldedHeadingSections.TryGetValue(p, out var savedBlocks))
            {
                // Expand
                var firstRun = p.Inlines.FirstInline as Run;
                if (firstRun != null && (string?)firstRun.Tag == TagFoldChevron)
                {
                    firstRun.Text = "˅ ";
                }

                var ellipsis = p.Inlines.FirstOrDefault(inl => (string?)inl.Tag == TagFoldEllipsis);
                if (ellipsis != null)
                {
                    p.Inlines.Remove(ellipsis);
                }

                Block insertAfter = p;
                foreach (var b in savedBlocks)
                {
                    doc.Blocks.InsertAfter(insertAfter, b);
                    insertAfter = b;
                }

                FoldedHeadingSections.Remove(p);
            }
            else
            {
                // Collapse
                int currentLevel = tag == TagH1 ? 1 : (tag == TagH2 ? 2 : 3);
                var childBlocks = new List<Block>();
                var next = p.NextBlock;
                while (next != null)
                {
                    int nextLevel = 999;
                    if (next is Paragraph np && np.Tag is string nTag)
                    {
                        if (nTag == TagH1) nextLevel = 1;
                        else if (nTag == TagH2) nextLevel = 2;
                        else if (nTag == TagH3) nextLevel = 3;
                    }

                    if (nextLevel <= currentLevel)
                    {
                        break;
                    }

                    var toRemove = next;
                    next = next.NextBlock;
                    childBlocks.Add(toRemove);
                }

                if (childBlocks.Count > 0)
                {
                    FoldedHeadingSections[p] = childBlocks;
                    foreach (var b in childBlocks)
                    {
                        doc.Blocks.Remove(b);
                    }

                    var firstRun = p.Inlines.FirstInline as Run;
                    if (firstRun != null && (string?)firstRun.Tag == TagFoldChevron)
                    {
                        firstRun.Text = "› ";
                    }
                    else
                    {
                        p.Inlines.InsertBefore(p.Inlines.FirstInline, CreateFoldChevron(true));
                    }

                    p.Inlines.Add(CreateFoldEllipsis());
                }
            }
        }

        public static Paragraph CreateOrderedListParagraph(int number, string text, double leftMargin = 12)
        {
            string prefix = $"{number}. ";
            string content = text.StartsWith(prefix) ? text.Substring(prefix.Length) : text;

            var p = new Paragraph
            {
                Tag = TagOrderedList,
                Margin = new Thickness(leftMargin, 2, 0, 2)
            };
            p.Inlines.Add(new Run(prefix));
            foreach (var inl in ParseInlineMarkdown(content, TagOrderedList))
            {
                p.Inlines.Add(inl);
            }
            ApplyStyle(p, TagOrderedList);
            return p;
        }

        public static BlockUIContainer CreateDividerBlock()
        {
            var border = new Border
            {
                Height = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = IsDarkMode 
                    ? new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)) 
                    : new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD0)),
                SnapsToDevicePixels = true,
                Focusable = false,
                Margin = new Thickness(0, 4, 0, 4)
            };

            var container = new BlockUIContainer(border)
            {
                Tag = TagDivider,
                Margin = new Thickness(0, 6, 0, 6)
            };

            return container;
        }

        public static void ApplyStyle(Paragraph p, string tag)
        {
            p.Tag = tag;
            p.TextDecorations = null;
            p.BorderThickness = new Thickness(0);
            p.Padding = new Thickness(0);
            p.Background = Brushes.Transparent;

            var textColor = IsDarkMode 
                ? new SolidColorBrush(Color.FromRgb(227, 227, 227)) 
                : new SolidColorBrush(Color.FromRgb(55, 53, 47));

            switch (tag)
            {
                case TagH1:
                    p.FontSize = 28;
                    p.FontWeight = FontWeights.Bold;
                    p.FontStyle = FontStyles.Normal;
                    p.Foreground = IsDarkMode 
                        ? new SolidColorBrush(Color.FromRgb(255, 255, 255)) 
                        : new SolidColorBrush(Color.FromRgb(40, 40, 40));
                    p.Margin = new Thickness(0, 20, 0, 6);
                    break;
                case TagH2:
                    p.FontSize = 22;
                    p.FontWeight = FontWeights.Bold;
                    p.FontStyle = FontStyles.Normal;
                    p.Foreground = IsDarkMode 
                        ? new SolidColorBrush(Color.FromRgb(240, 240, 240)) 
                        : new SolidColorBrush(Color.FromRgb(45, 45, 45));
                    p.Margin = new Thickness(0, 16, 0, 4);
                    break;
                case TagH3:
                    p.FontSize = 18;
                    p.FontWeight = FontWeights.SemiBold;
                    p.FontStyle = FontStyles.Normal;
                    p.Foreground = IsDarkMode 
                        ? new SolidColorBrush(Color.FromRgb(220, 220, 220)) 
                        : new SolidColorBrush(Color.FromRgb(55, 53, 47));
                    p.Margin = new Thickness(0, 12, 0, 3);
                    break;
                case TagBullet:
                    p.FontSize = 15;
                    p.FontWeight = FontWeights.Normal;
                    p.FontStyle = FontStyles.Normal;
                    p.Foreground = textColor;
                    if (p.Margin.Left < 12) p.Margin = new Thickness(12, 2, 0, 2);
                    break;
                case TagTodo:
                    p.FontSize = 15;
                    p.FontWeight = FontWeights.Normal;
                    p.FontStyle = FontStyles.Normal;
                    p.Foreground = textColor;
                    if (p.Margin.Left < 4) p.Margin = new Thickness(4, 3, 0, 3);
                    break;
                case TagTodoDone:
                    p.FontSize = 15;
                    p.FontWeight = FontWeights.Normal;
                    p.FontStyle = FontStyles.Normal;
                    p.Foreground = IsDarkMode 
                        ? new SolidColorBrush(Color.FromRgb(115, 115, 115)) 
                        : new SolidColorBrush(Color.FromRgb(156, 163, 175));
                    p.TextDecorations = TextDecorations.Strikethrough;
                    if (p.Margin.Left < 4) p.Margin = new Thickness(4, 3, 0, 3);
                    break;
                case TagOrderedList:
                    p.FontSize = 15;
                    p.FontWeight = FontWeights.Normal;
                    p.FontStyle = FontStyles.Normal;
                    p.Foreground = textColor;
                    if (p.Margin.Left < 12) p.Margin = new Thickness(12, 2, 0, 2);
                    break;
                case TagQuote:
                    p.FontSize = 15;
                    p.FontWeight = FontWeights.Normal;
                    p.FontStyle = FontStyles.Italic;
                    p.Foreground = IsDarkMode 
                        ? new SolidColorBrush(Color.FromRgb(225, 225, 225)) 
                        : new SolidColorBrush(Color.FromRgb(55, 53, 47));
                    p.BorderBrush = IsDarkMode 
                        ? new SolidColorBrush(Color.FromRgb(160, 160, 160)) 
                        : new SolidColorBrush(Color.FromRgb(70, 70, 70));
                    p.BorderThickness = new Thickness(3.5, 0, 0, 0);
                    p.Background = IsDarkMode 
                        ? new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)) 
                        : new SolidColorBrush(Color.FromArgb(12, 0, 0, 0));
                    p.Padding = new Thickness(14, 6, 10, 6);
                    p.Margin = new Thickness(0, 8, 0, 8);
                    break;
                default:
                    p.FontSize = 15;
                    p.FontWeight = FontWeights.Normal;
                    p.FontStyle = FontStyles.Normal;
                    p.Foreground = textColor;
                    p.Margin = new Thickness(0, 3, 0, 3);
                    break;
            }
        }

        public static void ConvertListItemToNormalText(Paragraph p)
        {
            if (p == null) return;
            var tag = p.Tag as string ?? string.Empty;
            p.Tag = TagBody;
            p.Margin = new Thickness(0, 3, 0, 3);
            p.TextDecorations = null;
            ApplyStyle(p, TagBody);

            string marker = string.Empty;
            if (tag == TagBullet)
            {
                var firstRun = p.Inlines.FirstInline as Run;
                var firstText = firstRun?.Text ?? string.Empty;
                foreach (var m in BulletMarkers)
                {
                    if (firstText.StartsWith(m)) { marker = m; break; }
                }
                if (string.IsNullOrEmpty(marker))
                {
                    if (firstText.StartsWith("- ")) marker = "- ";
                    else if (firstText.StartsWith("* ")) marker = "* ";
                }
            }
            else if (tag == TagOrderedList)
            {
                var firstRun = p.Inlines.FirstInline as Run;
                var firstText = firstRun?.Text ?? string.Empty;
                var match = Regex.Match(firstText, @"^(\d+)\.\s*");
                if (match.Success)
                {
                    marker = match.Value;
                }
                else
                {
                    var fullText = InlinesToMarkdown(p.Inlines, tag);
                    var fullMatch = Regex.Match(fullText, @"^(\d+)\.\s*");
                    if (fullMatch.Success) marker = fullMatch.Value;
                }
            }
            else if (tag == TagTodo || tag == TagTodoDone)
            {
                var firstRun = p.Inlines.FirstInline as Run;
                var firstText = firstRun?.Text ?? string.Empty;
                if (firstText.StartsWith(TodoPrefix)) marker = TodoPrefix;
                else if (firstText.StartsWith(TodoDonePrefix)) marker = TodoDonePrefix;
                else if (firstText.StartsWith("- [ ] ")) marker = "- [ ] ";
                else if (firstText.StartsWith("- [x] ")) marker = "- [x] ";
            }
            else if (tag == TagH1 || tag == TagH2 || tag == TagH3)
            {
                var chevron = p.Inlines.FirstOrDefault(inl => (string?)inl.Tag == TagFoldChevron);
                if (chevron != null) p.Inlines.Remove(chevron);
                var ellipsis = p.Inlines.FirstOrDefault(inl => (string?)inl.Tag == TagFoldEllipsis);
                if (ellipsis != null) p.Inlines.Remove(ellipsis);
            }

            if (!string.IsNullOrEmpty(marker))
            {
                int charsToRemove = marker.Length;
                var currentInline = p.Inlines.FirstInline;
                while (currentInline != null && charsToRemove > 0)
                {
                    var nextInline = currentInline.NextInline;
                    if (currentInline is Run r)
                    {
                        if (r.Text.Length <= charsToRemove)
                        {
                            charsToRemove -= r.Text.Length;
                            p.Inlines.Remove(r);
                        }
                        else
                        {
                            r.Text = r.Text.Substring(charsToRemove);
                            charsToRemove = 0;
                        }
                    }
                    else
                    {
                        break;
                    }
                    currentInline = nextInline;
                }
            }

            if (p.Inlines.Count == 0)
            {
                p.Inlines.Add(new Run(string.Empty));
            }
        }

        public static bool TryTransformBlock(Paragraph p)
        {
            if (p == null) return false;

            var currentTag = p.Tag as string ?? TagBody;
            var text = InlinesToMarkdown(p.Inlines, currentTag).TrimEnd('\r', '\n');

            // 1. Heading 1: starts with "# "
            if (text.StartsWith("# "))
            {
                var clean = text.Substring(2);
                p.Inlines.Clear();
                p.Inlines.Add(CreateFoldChevron(false));
                foreach (var inl in ParseInlineMarkdown(clean, TagH1)) p.Inlines.Add(inl);
                ApplyStyle(p, TagH1);
                return true;
            }

            // 2. Heading 2: starts with "## "
            if (text.StartsWith("## "))
            {
                var clean = text.Substring(3);
                p.Inlines.Clear();
                p.Inlines.Add(CreateFoldChevron(false));
                foreach (var inl in ParseInlineMarkdown(clean, TagH2)) p.Inlines.Add(inl);
                ApplyStyle(p, TagH2);
                return true;
            }

            // 3. Heading 3 to 6
            var hMatch = Regex.Match(text, @"^(#{3,6})\s+(.*)$");
            if (hMatch.Success)
            {
                var clean = hMatch.Groups[2].Value;
                p.Inlines.Clear();
                p.Inlines.Add(CreateFoldChevron(false));
                foreach (var inl in ParseInlineMarkdown(clean, TagH3)) p.Inlines.Add(inl);
                ApplyStyle(p, TagH3);
                return true;
            }

            // 3b. List/Bullet/Todo/Quote followed by Heading marker (e.g. "1. # hello", "1. ## title", "- # section")
            var listHeadingMatch = Regex.Match(text, @"^(?:\d+\.|\-|[*]|\[\]|-[ ]\[ \])\s+(#{1,6})\s+(.*)$");
            if (listHeadingMatch.Success)
            {
                int level = listHeadingMatch.Groups[1].Value.Length;
                string headingTag = level switch
                {
                    1 => TagH1,
                    2 => TagH2,
                    _ => TagH3
                };
                string clean = listHeadingMatch.Groups[2].Value;
                p.Inlines.Clear();
                p.Inlines.Add(CreateFoldChevron(false));
                foreach (var inl in ParseInlineMarkdown(clean, headingTag)) p.Inlines.Add(inl);
                ApplyStyle(p, headingTag);
                return true;
            }

            // If block is already a heading, do not demote it to list, bullet, todo, or quote
            if (currentTag == TagH1 || currentTag == TagH2 || currentTag == TagH3)
            {
                return false;
            }

            // 4. Bullet: starts with "- " or "* "
            if (text.StartsWith("- ") || text.StartsWith("* "))
            {
                if (currentTag == TagBullet) return false;
                var clean = text.Substring(2);
                p.Inlines.Clear();
                p.Inlines.Add(new Run(BulletPrefix));
                foreach (var inl in ParseInlineMarkdown(clean, TagBullet)) p.Inlines.Add(inl);
                ApplyStyle(p, TagBullet);
                return true;
            }

            // 5. Todo Checkbox: starts with "[] " or "- [ ] "
            if (text.StartsWith("[] ") || text.StartsWith("- [ ] "))
            {
                if (currentTag == TagTodo) return false;
                string clean = text.StartsWith("[] ") ? text.Substring(3) : text.Substring(6);
                p.Inlines.Clear();
                p.Inlines.Add(new Run(TodoPrefix));
                foreach (var inl in ParseInlineMarkdown(clean, TagTodo)) p.Inlines.Add(inl);
                ApplyStyle(p, TagTodo);
                return true;
            }

            // 6. Numbered list: starts with "1. " or "2. "
            var numMatch = Regex.Match(text, @"^(\d+)\.\s+(.*)");
            if (numMatch.Success)
            {
                if (currentTag == TagOrderedList) return false;
                int num = int.Parse(numMatch.Groups[1].Value);
                var clean = numMatch.Groups[2].Value;
                p.Inlines.Clear();
                p.Inlines.Add(new Run($"{num}. "));
                foreach (var inl in ParseInlineMarkdown(clean, TagOrderedList)) p.Inlines.Add(inl);
                ApplyStyle(p, TagOrderedList);
                return true;
            }

            // 7. Quote: starts with "> " or is ">"
            if (text.StartsWith("> "))
            {
                if (currentTag == TagQuote) return false;
                var clean = text.Substring(2);
                p.Inlines.Clear();
                foreach (var inl in ParseInlineMarkdown(clean, TagQuote)) p.Inlines.Add(inl);
                ApplyStyle(p, TagQuote);
                return true;
            }
            else if (text == ">")
            {
                p.Inlines.Clear();
                p.Inlines.Add(new Run(string.Empty));
                ApplyStyle(p, TagQuote);
                return true;
            }

            return false;
        }
    }
}
