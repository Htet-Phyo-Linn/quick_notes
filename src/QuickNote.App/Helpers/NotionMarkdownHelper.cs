using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickNote.App.Helpers
{
    public static partial class NotionMarkdownHelper
    {
        public const string TagH1 = "h1";
        public const string TagH2 = "h2";
        public const string TagH3 = "h3";
        public const string TagBullet = "bullet";
        public const string TagTodo = "todo";
        public const string TagTodoDone = "todo-done";
        public const string TagQuote = "quote";
        public const string TagDivider = "divider";
        public const string TagBody = "p";
        public const string TagOrderedList = "ordered-list";
        public const string TagCodeBlock = "codeblock";
        public const string TagFoldChevron = "fold-chevron";
        public const string TagFoldEllipsis = "fold-ellipsis";

        public static readonly Dictionary<Paragraph, List<Block>> FoldedHeadingSections = new();

        public static readonly string[] BulletMarkers = new[] { "• ", "◦ ", "▪ ", "▫ " };

        public static readonly (string Name, string Id)[] CommonLanguages = new[]
        {
            ("Plain Text", "text"),
            ("Bash / Shell", "bash"),
            ("PowerShell", "powershell"),
            ("Python", "python"),
            ("C#", "csharp"),
            ("JavaScript", "javascript"),
            ("TypeScript", "typescript"),
            ("JSON", "json"),
            ("SQL", "sql"),
            ("HTML", "html"),
            ("CSS", "css"),
            ("YAML", "yaml"),
            ("Batch / CMD", "batch"),
            ("C / C++", "cpp"),
            ("Java", "java"),
            ("Go", "go"),
            ("Rust", "rust"),
            ("Markdown", "markdown"),
            ("XML", "xml"),
            ("Dockerfile", "dockerfile"),
            ("PHP", "php"),
            ("Ruby", "ruby"),
            ("Kotlin", "kotlin"),
            ("Swift", "swift"),
            ("Dart", "dart"),
            ("R", "r"),
            ("Lua", "lua"),
            ("Perl", "perl"),
            ("GraphQL", "graphql"),
            ("TOML / INI", "ini")
        };

        public static string GetBulletMarker(int level)
        {
            if (level < 0) level = 0;
            if (level >= BulletMarkers.Length) level = BulletMarkers.Length - 1;
            return BulletMarkers[level];
        }

        public static bool IsHorizontalRule(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            var trimmed = line.Trim();
            if (trimmed.Length < 3) return false;
            if (trimmed == "---" || trimmed == "***" || trimmed == "___") return true;
            return Regex.IsMatch(trimmed, @"^((\*\s*){3,}|(-\s*){3,}|(_\s*){3,})$");
        }

        public const string BulletPrefix = "• ";
        public const string TodoPrefix = "☐ ";
        public const string TodoDonePrefix = "☑ ";

        public static bool IsDarkMode { get; set; } = true;
        public static Action? OnDocumentContentChanged { get; set; }

        private static readonly Regex InlineMarkdownRegex = new Regex(
            @"(?<code>`[^`\r\n]+`)|" +
            @"(?<bolditalic>\*\*\*[^\*\r\n]+\*\*\*|___[^_\r\n]+___)|" +
            @"(?<bold>\*\*[^\*\r\n]+\*\*|__[^_\r\n]+__)|" +
            @"(?<italic>\*[^\*\r\n]+\*|(?<!\w)_[^_\r\n]+_(?!\w))|" +
            @"(?<strike>~~[^~\r\n]+~~)|" +
            @"(?<link>\[(?<linktext>[^\]\r\n]+)\]\((?<linkurl>[^)\r\n]+)\))",
            RegexOptions.Compiled);

        public static Inline CreateInlineCode(string code)
        {
            var inlineCodeBg = IsDarkMode 
                ? new SolidColorBrush(Color.FromRgb(45, 45, 50)) 
                : new SolidColorBrush(Color.FromRgb(240, 240, 242));
            var inlineCodeFg = IsDarkMode 
                ? new SolidColorBrush(Color.FromRgb(224, 108, 117)) 
                : new SolidColorBrush(Color.FromRgb(180, 40, 60));

            var run = new Run(code)
            {
                FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
                FontSize = 13.5,
                Background = inlineCodeBg,
                Foreground = inlineCodeFg,
                Tag = "InlineCode"
            };

            var span = new Span(run)
            {
                Tag = "InlineCode"
            };
            return span;
        }

        public static List<Inline> ParseInlineMarkdown(string text, string? paragraphTag = null, bool recurse = true)
        {
            var list = new List<Inline>();
            if (string.IsNullOrEmpty(text))
            {
                list.Add(new Run(string.Empty));
                return list;
            }

            int lastIndex = 0;
            var matches = InlineMarkdownRegex.Matches(text);

            foreach (Match m in matches)
            {
                if (m.Index > lastIndex)
                {
                    list.Add(new Run(text.Substring(lastIndex, m.Index - lastIndex)));
                }

                if (m.Groups["code"].Success)
                {
                    var raw = m.Groups["code"].Value;
                    var code = raw.Substring(1, raw.Length - 2);
                    list.Add(CreateInlineCode(code));
                }
                else if (m.Groups["bolditalic"].Success)
                {
                    var raw = m.Groups["bolditalic"].Value;
                    var inner = raw.Substring(3, raw.Length - 6);
                    var bold = new Bold();
                    var italic = new Italic();
                    if (recurse)
                    {
                        foreach (var child in ParseInlineMarkdown(inner, paragraphTag, false))
                            italic.Inlines.Add(child);
                    }
                    else
                    {
                        italic.Inlines.Add(new Run(inner));
                    }
                    bold.Inlines.Add(italic);
                    list.Add(bold);
                }
                else if (m.Groups["bold"].Success)
                {
                    var raw = m.Groups["bold"].Value;
                    var inner = raw.Substring(2, raw.Length - 4);
                    var bold = new Bold();
                    if (recurse)
                    {
                        foreach (var child in ParseInlineMarkdown(inner, paragraphTag, false))
                            bold.Inlines.Add(child);
                    }
                    else
                    {
                        bold.Inlines.Add(new Run(inner));
                    }
                    list.Add(bold);
                }
                else if (m.Groups["italic"].Success)
                {
                    var raw = m.Groups["italic"].Value;
                    var inner = raw.Substring(1, raw.Length - 2);
                    var italic = new Italic();
                    if (recurse)
                    {
                        foreach (var child in ParseInlineMarkdown(inner, paragraphTag, false))
                            italic.Inlines.Add(child);
                    }
                    else
                    {
                        italic.Inlines.Add(new Run(inner));
                    }
                    list.Add(italic);
                }
                else if (m.Groups["strike"].Success)
                {
                    var raw = m.Groups["strike"].Value;
                    var inner = raw.Substring(2, raw.Length - 4);
                    var span = new Span { Tag = "Strikethrough" };
                    span.TextDecorations = TextDecorations.Strikethrough;
                    if (recurse)
                    {
                        foreach (var child in ParseInlineMarkdown(inner, paragraphTag, false))
                            span.Inlines.Add(child);
                    }
                    else
                    {
                        span.Inlines.Add(new Run(inner));
                    }
                    list.Add(span);
                }
                else if (m.Groups["link"].Success)
                {
                    var linkText = m.Groups["linktext"].Value;
                    var linkUrl = m.Groups["linkurl"].Value;
                    var hyperlink = new Hyperlink
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(74, 158, 255)),
                        TextDecorations = TextDecorations.Underline,
                        Tag = linkUrl
                    };
                    if (Uri.TryCreate(linkUrl, UriKind.RelativeOrAbsolute, out var uri))
                    {
                        hyperlink.NavigateUri = uri;
                    }
                    if (recurse)
                    {
                        foreach (var child in ParseInlineMarkdown(linkText, paragraphTag, false))
                            hyperlink.Inlines.Add(child);
                    }
                    else
                    {
                        hyperlink.Inlines.Add(new Run(linkText));
                    }
                    list.Add(hyperlink);
                }

                lastIndex = m.Index + m.Length;
            }

            if (lastIndex < text.Length)
            {
                list.Add(new Run(text.Substring(lastIndex)));
            }

            return list;
        }

        public static string InlinesToMarkdown(InlineCollection inlines, string paragraphTag)
        {
            var sb = new StringBuilder();
            foreach (var inl in inlines)
            {
                AppendInlineMarkdown(sb, inl, paragraphTag);
            }
            return sb.ToString();
        }

        private static void AppendInlineMarkdown(StringBuilder sb, Inline inline, string paragraphTag)
        {
            if ((string?)inline.Tag == TagFoldChevron || (string?)inline.Tag == TagFoldEllipsis)
            {
                return;
            }

            if (inline is Run r)
            {
                if ((string?)r.Tag == "InlineCode")
                {
                    sb.Append($"`{r.Text}`");
                }
                else
                {
                    sb.Append(r.Text);
                }
            }
            else if (inline is Bold b)
            {
                bool hasItalic = b.Inlines.OfType<Italic>().Any();
                if (hasItalic)
                {
                    sb.Append("***");
                    foreach (var child in b.Inlines)
                    {
                        if (child is Italic it)
                        {
                            foreach (var itChild in it.Inlines) AppendInlineMarkdown(sb, itChild, paragraphTag);
                        }
                        else
                        {
                            AppendInlineMarkdown(sb, child, paragraphTag);
                        }
                    }
                    sb.Append("***");
                }
                else
                {
                    sb.Append("**");
                    foreach (var child in b.Inlines) AppendInlineMarkdown(sb, child, paragraphTag);
                    sb.Append("**");
                }
            }
            else if (inline is Italic it)
            {
                sb.Append("*");
                foreach (var child in it.Inlines) AppendInlineMarkdown(sb, child, paragraphTag);
                sb.Append("*");
            }
            else if (inline is Span s)
            {
                if ((string?)s.Tag == "InlineCode")
                {
                    var innerText = new TextRange(s.ContentStart, s.ContentEnd).Text;
                    sb.Append($"`{innerText}`");
                }
                else if ((string?)s.Tag == "Strikethrough" || s.TextDecorations.Contains(TextDecorations.Strikethrough[0]))
                {
                    sb.Append("~~");
                    foreach (var child in s.Inlines) AppendInlineMarkdown(sb, child, paragraphTag);
                    sb.Append("~~");
                }
                else
                {
                    foreach (var child in s.Inlines) AppendInlineMarkdown(sb, child, paragraphTag);
                }
            }
            else if (inline is Hyperlink h)
            {
                var linkTextSb = new StringBuilder();
                foreach (var child in h.Inlines) AppendInlineMarkdown(linkTextSb, child, paragraphTag);
                var url = h.Tag as string ?? h.NavigateUri?.ToString() ?? string.Empty;
                sb.Append($"[{linkTextSb}]({url})");
            }
            else if (inline is LineBreak)
            {
                sb.Append("\n");
            }
        }

        public static FlowDocument MarkdownToFlowDocument(string markdown, string? notesRoot = null)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                LineHeight = double.NaN
            };

            if (string.IsNullOrEmpty(markdown))
            {
                doc.Blocks.Add(CreateParagraph(TagBody, string.Empty));
                return doc;
            }

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            bool previousWasNonEmptyBlock = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                // Blank line handling
                if (trimmed.Length == 0)
                {
                    if (previousWasNonEmptyBlock)
                    {
                        // The 1st blank line following a non-empty block is the Markdown block separator (\n\n).
                        // Skip it so we do not insert an unwanted blank paragraph between blocks.
                        previousWasNonEmptyBlock = false;
                        continue;
                    }

                    if (doc.Blocks.Count == 0)
                    {
                        // Skip leading blank lines before the first block
                        continue;
                    }

                    // 2nd+ consecutive blank lines represent an intentional empty line
                    doc.Blocks.Add(CreateParagraph(TagBody, string.Empty));
                    previousWasNonEmptyBlock = false;
                    continue;
                }

                // Horizontal Rule
                if (IsHorizontalRule(line))
                {
                    doc.Blocks.Add(CreateDividerBlock());
                    previousWasNonEmptyBlock = true;
                    continue;
                }

                // Image: ![alt](src)
                var imgMatch = Regex.Match(trimmed, @"^!\[(.*?)\]\((.*?)\)$");
                if (imgMatch.Success)
                {
                    var alt = imgMatch.Groups[1].Value;
                    var src = imgMatch.Groups[2].Value;
                    var imgBlock = CreateImageBlock(src, alt, notesRoot);
                    if (imgBlock != null)
                    {
                        doc.Blocks.Add(imgBlock);
                        previousWasNonEmptyBlock = true;
                        continue;
                    }
                }

                // Code block: ```language
                if (trimmed.StartsWith("```"))
                {
                    var lang = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : string.Empty;
                    var codeLines = new List<string>();
                    i++;
                    while (i < lines.Length)
                    {
                        var cLine = lines[i];
                        if (cLine.Trim().StartsWith("```"))
                        {
                            break;
                        }
                        codeLines.Add(cLine);
                        i++;
                    }

                    var fullCode = string.Join("\n", codeLines);
                    var codeBlock = CreateCodeBlock(fullCode, lang);
                    if (codeBlock != null)
                    {
                        doc.Blocks.Add(codeBlock);
                        previousWasNonEmptyBlock = true;
                        continue;
                    }
                }

                // Table parsing check
                if (trimmed.StartsWith("|") && trimmed.EndsWith("|") && trimmed.Length > 2)
                {
                    var tableLines = new List<string>();
                    while (i < lines.Length)
                    {
                        var tLine = lines[i].Trim();
                        if (tLine.StartsWith("|") && tLine.EndsWith("|") && tLine.Length > 2)
                        {
                            tableLines.Add(lines[i]);
                            i++;
                        }
                        else
                        {
                            i--;
                            break;
                        }
                    }

                    var tableBlock = CreateTableBlockFromLines(tableLines);
                    if (tableBlock != null)
                    {
                        doc.Blocks.Add(tableBlock);
                        previousWasNonEmptyBlock = true;
                        continue;
                    }
                }

                // Headings
                if (trimmed.StartsWith("# "))
                {
                    doc.Blocks.Add(CreateParagraph(TagH1, trimmed.Substring(2)));
                    previousWasNonEmptyBlock = true;
                }
                else if (trimmed.StartsWith("## "))
                {
                    doc.Blocks.Add(CreateParagraph(TagH2, trimmed.Substring(3)));
                    previousWasNonEmptyBlock = true;
                }
                else if (Regex.IsMatch(trimmed, @"^(#{3,6})\s+"))
                {
                    var m = Regex.Match(trimmed, @"^(#{3,6})\s+(.*)$");
                    doc.Blocks.Add(CreateParagraph(TagH3, m.Groups[2].Value));
                    previousWasNonEmptyBlock = true;
                }
                // List marker followed by heading (e.g. "1. # hello", "- ## section")
                else if (Regex.IsMatch(trimmed, @"^(?:\d+\.|\-|[*]|\[\]|-[ ]\[ \])\s+(#{1,6})\s+"))
                {
                    var listHeadingMatch = Regex.Match(trimmed, @"^(?:\d+\.|\-|[*]|\[\]|-[ ]\[ \])\s+(#{1,6})\s+(.*)$");
                    int level = listHeadingMatch.Groups[1].Value.Length;
                    string headingTag = level switch
                    {
                        1 => TagH1,
                        2 => TagH2,
                        _ => TagH3
                    };
                    string clean = listHeadingMatch.Groups[2].Value;
                    doc.Blocks.Add(CreateParagraph(headingTag, clean));
                    previousWasNonEmptyBlock = true;
                }
                // Bullet List
                else if (Regex.IsMatch(line, @"^\s*[-*]\s+"))
                {
                    int indentSpaces = line.TakeWhile(c => c == ' ').Count();
                    int level = indentSpaces / 2;
                    var bulletText = Regex.Replace(line.TrimStart(), @"^[-*]\s+", "");
                    var marker = GetBulletMarker(level);
                    var p = CreateParagraph(TagBullet, marker + bulletText);
                    p.Margin = new Thickness(12 + (level * 24), 2, 0, 2);
                    doc.Blocks.Add(p);
                    previousWasNonEmptyBlock = true;
                }
                // Ordered List
                else if (Regex.IsMatch(line, @"^\s*\d+\.\s+"))
                {
                    int indentSpaces = line.TakeWhile(c => c == ' ').Count();
                    int level = indentSpaces / 2;
                    var m = Regex.Match(line.TrimStart(), @"^(\d+)\.\s+(.*)$");
                    int num = int.Parse(m.Groups[1].Value);
                    var text = m.Groups[2].Value;
                    var p = CreateOrderedListParagraph(num, text, 12 + (level * 24));
                    doc.Blocks.Add(p);
                    previousWasNonEmptyBlock = true;
                }
                // Todo Item
                else if (trimmed.StartsWith("- [ ] ") || trimmed.StartsWith("- [x] ") || trimmed.StartsWith("- [X] "))
                {
                    bool isDone = trimmed.StartsWith("- [x] ") || trimmed.StartsWith("- [X] ");
                    var todoText = trimmed.Substring(6);
                    doc.Blocks.Add(CreateParagraph(isDone ? TagTodoDone : TagTodo, todoText));
                    previousWasNonEmptyBlock = true;
                }
                // Blockquote
                else if (trimmed.StartsWith(">"))
                {
                    var quoteText = trimmed.Length > 1 && trimmed[1] == ' ' ? trimmed.Substring(2) : trimmed.Substring(1);
                    doc.Blocks.Add(CreateParagraph(TagQuote, quoteText));
                    previousWasNonEmptyBlock = true;
                }
                // Regular Paragraph
                else
                {
                    doc.Blocks.Add(CreateParagraph(TagBody, line));
                    previousWasNonEmptyBlock = true;
                }
            }

            if (doc.Blocks.Count == 0)
            {
                doc.Blocks.Add(CreateParagraph(TagBody, string.Empty));
            }
            else if (doc.Blocks.LastBlock is BlockUIContainer)
            {
                doc.Blocks.Add(CreateParagraph(TagBody, string.Empty));
            }

            return doc;
        }

        public static string BlockToMarkdown(Block block)
        {
            if (block == null) return string.Empty;

            if (block is BlockUIContainer container)
            {
                var containerTag = container.Tag as string;
                if (containerTag != null && containerTag.StartsWith("ImageBlock:"))
                {
                    var src = containerTag.Substring("ImageBlock:".Length);
                    return $"![]({src})";
                }
                else if (containerTag != null && containerTag.StartsWith("CodeBlock:"))
                {
                    var lang = containerTag.Substring("CodeBlock:".Length);
                    string codeText = ExtractCodeFromBlock(container);
                    return $"```{lang}\n{codeText}\n```";
                }
                else if (containerTag == TagDivider)
                {
                    return "---";
                }
                return string.Empty;
            }
            else if (block is Table table)
            {
                var sb = new StringBuilder();
                foreach (var group in table.RowGroups)
                {
                    bool isHeaderRow = true;
                    foreach (var row in group.Rows)
                    {
                        var cellTexts = new List<string>();
                        foreach (var cell in row.Cells)
                        {
                            string text = "";
                            if (cell.Blocks.FirstBlock is Paragraph cp)
                            {
                                text = InlinesToMarkdown(cp.Inlines, TagBody).Trim();
                            }
                            else
                            {
                                var tr = new TextRange(cell.ContentStart, cell.ContentEnd);
                                text = tr.Text.TrimEnd('\r', '\n').Trim();
                            }
                            if (string.IsNullOrEmpty(text)) text = " ";
                            cellTexts.Add(text);
                        }

                        sb.Append("| ").Append(string.Join(" | ", cellTexts)).Append(" |\n");

                        if (isHeaderRow)
                        {
                            var dashes = cellTexts.Select(_ => "---");
                            sb.Append("| ").Append(string.Join(" | ", dashes)).Append(" |\n");
                            isHeaderRow = false;
                        }
                    }
                }
                return sb.ToString().TrimEnd('\n');
            }
            else if (block is Paragraph p)
            {
                var tag = p.Tag as string ?? TagBody;

                // Obsidian code block
                if (tag == TagCodeBlock || tag.StartsWith("CodeBlock:"))
                {
                    var rawLines = new TextRange(p.ContentStart, p.ContentEnd).Text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                    if (rawLines.Length > 0 && rawLines[0].TrimStart().StartsWith("```"))
                    {
                        if (rawLines.Length >= 2 && rawLines[^1].Trim() == "```")
                        {
                            return string.Join("\n", rawLines);
                        }
                        return string.Join("\n", rawLines) + "\n```";
                    }

                    var lang = tag.StartsWith("CodeBlock:") ? tag.Substring("CodeBlock:".Length) : string.Empty;
                    var codeText = ExtractCodeFromParagraph(p);
                    return $"```{lang}\n{codeText}\n```";
                }

                var content = InlinesToMarkdown(p.Inlines, tag).TrimEnd('\r', '\n');

                switch (tag)
                {
                    case TagH1:
                        return $"# {content}";
                    case TagH2:
                        return $"## {content}";
                    case TagH3:
                        return $"### {content}";
                    case TagBullet:
                        int bulLevel = (int)Math.Max(0, (p.Margin.Left - 12) / 24);
                        string bulIndent = new string(' ', bulLevel * 2);
                        string cleanBullet = content;
                        foreach (var m in BulletMarkers)
                        {
                            if (cleanBullet.StartsWith(m))
                            {
                                cleanBullet = cleanBullet.Substring(m.Length);
                                break;
                            }
                        }
                        return $"{bulIndent}- {cleanBullet}";
                    case TagOrderedList:
                        int ordLevel = (int)Math.Max(0, (p.Margin.Left - 12) / 24);
                        string ordIndent = new string(' ', ordLevel * 2);
                        return $"{ordIndent}{content}";
                    case TagTodo:
                        var todoContent = content.StartsWith(TodoPrefix) ? content.Substring(TodoPrefix.Length) : content;
                        return $"- [ ] {todoContent}";
                    case TagTodoDone:
                        var doneContent = content.StartsWith(TodoDonePrefix) ? content.Substring(TodoDonePrefix.Length) : content;
                        return $"- [x] {doneContent}";
                    case TagQuote:
                        var qLines = content.Replace("\r\n", "\n").Split('\n');
                        var qSb = new StringBuilder();
                        for (int q = 0; q < qLines.Length; q++)
                        {
                            if (q > 0) qSb.Append("\n");
                            qSb.Append("> ").Append(qLines[q]);
                        }
                        return qSb.ToString();
                    default:
                        return content;
                }
            }

            return string.Empty;
        }

        public static string GetBlockSeparator(Block? prevBlock, string prevMd, Block? currBlock, string currMd)
        {
            bool prevIsList = (prevBlock is Paragraph p1 && (
                p1.Tag as string == TagBullet ||
                p1.Tag as string == TagOrderedList ||
                p1.Tag as string == TagTodo ||
                p1.Tag as string == TagTodoDone)) ||
                prevMd.StartsWith("- ") || prevMd.StartsWith("* ") || Regex.IsMatch(prevMd, @"^\d+\.\s+");

            bool currIsList = (currBlock is Paragraph p2 && (
                p2.Tag as string == TagBullet ||
                p2.Tag as string == TagOrderedList ||
                p2.Tag as string == TagTodo ||
                p2.Tag as string == TagTodoDone)) ||
                currMd.StartsWith("- ") || currMd.StartsWith("* ") || Regex.IsMatch(currMd, @"^\d+\.\s+");

            if (prevIsList && currIsList)
            {
                return "\n";
            }

            bool prevIsQuote = (prevBlock is Paragraph pQ1 && pQ1.Tag as string == TagQuote) || prevMd.StartsWith(">");
            bool currIsQuote = (currBlock is Paragraph pQ2 && pQ2.Tag as string == TagQuote) || currMd.StartsWith(">");

            if (prevIsQuote && currIsQuote)
            {
                return "\n";
            }

            bool prevIsSpecial = prevIsList || prevIsQuote ||
                (prevBlock != null && IsCodeBlock(prevBlock)) || prevMd.StartsWith("```") ||
                (prevBlock is BlockUIContainer) || prevMd.StartsWith("|") || prevMd.StartsWith("#");

            bool currIsSpecial = currIsList || currIsQuote ||
                (currBlock != null && IsCodeBlock(currBlock)) || currMd.StartsWith("```") ||
                (currBlock is BlockUIContainer) || currMd.StartsWith("|") || currMd.StartsWith("#");

            if (string.IsNullOrEmpty(prevMd) || string.IsNullOrEmpty(currMd))
            {
                // If previous was a special block (like a Heading) and next is an empty paragraph,
                // keep the standard double newline after the special block plus the empty line.
                if (prevIsSpecial && string.IsNullOrEmpty(currMd))
                {
                    return "\n\n";
                }
                return "\n";
            }

            // Between two consecutive regular body lines: single newline (no extra blank line)
            if (!prevIsSpecial && !currIsSpecial)
            {
                return "\n";
            }

            return "\n\n";
        }

        public static string FlowDocumentToMarkdown(FlowDocument doc)
        {
            if (doc == null) return string.Empty;

            var sb = new StringBuilder();
            var allBlocks = new List<Block>();

            void CollectBlocks(IEnumerable<Block> blockList)
            {
                foreach (var b in blockList)
                {
                    allBlocks.Add(b);
                    if (b is Paragraph p && FoldedHeadingSections.TryGetValue(p, out var folded) && folded != null)
                    {
                        CollectBlocks(folded);
                    }
                }
            }

            CollectBlocks(doc.Blocks);

            var rendered = new List<(Block Block, string Markdown)>();

            for (int i = 0; i < allBlocks.Count; i++)
            {
                var block = allBlocks[i];
                var md = BlockToMarkdown(block);
                rendered.Add((block, md));
            }

            for (int i = 0; i < rendered.Count; i++)
            {
                var (curBlock, curMd) = rendered[i];
                if (i > 0)
                {
                    var (prevBlock, prevMd) = rendered[i - 1];
                    sb.Append(GetBlockSeparator(prevBlock, prevMd, curBlock, curMd));
                }
                sb.Append(curMd);
            }

            return sb.ToString();
        }
    }
}
