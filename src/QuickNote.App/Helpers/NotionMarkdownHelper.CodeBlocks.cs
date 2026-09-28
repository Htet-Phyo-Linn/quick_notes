using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickNote.App.Helpers
{
    public static partial class NotionMarkdownHelper
    {
        /// <summary>
        /// Applies consistent card styling (background, border, padding, monospace font) to a code block paragraph.
        /// </summary>
        public static void ApplyCodeBlockStyle(Paragraph p)
        {
            if (p == null) return;
            p.FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");
            p.FontSize = 13.5;
            p.LineHeight = 22;
            p.Margin = new Thickness(0, 8, 0, 8);
            p.Padding = new Thickness(14, 8, 14, 10);
            p.BorderThickness = new Thickness(1);
            p.Background = IsDarkMode 
                ? new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)) 
                : new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5));
            p.BorderBrush = IsDarkMode 
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x33)) 
                : new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
        }

        /// <summary>
        /// Creates an Obsidian-style code block paragraph in Normal (rendered) mode.
        /// When unfocused: shows highlighted code with a top-right language copy badge (no fences).
        /// When focused (active): shows raw fences (```lang and ```) for direct editing.
        /// </summary>
        public static Paragraph CreateCodeBlock(string code, string language)
        {
            var norm = SyntaxHighlighter.NormalizeLanguage(language);
            var p = new Paragraph
            {
                Tag = "CodeBlock:" + norm
            };
            ApplyCodeBlockStyle(p);
            PopulateNormalCodeBlockParagraph(p, code, norm);
            return p;
        }

        public static void PopulateCodeBlockParagraph(Paragraph p, string code, string language)
        {
            bool isActive = p.Resources["IsActiveCodeBlock"] as bool? ?? false;
            if (isActive)
            {
                PopulateActiveCodeBlockParagraph(p, code, language);
            }
            else
            {
                PopulateNormalCodeBlockParagraph(p, code, language);
            }
        }

        /// <summary>
        /// Normal Mode: Displays highlighted code with the language name at top-right (no ``` fences).
        /// Clicking the language name copies the code to the clipboard.
        /// </summary>
        public static void PopulateNormalCodeBlockParagraph(Paragraph p, string code, string language, double targetWidth = 0)
        {
            ApplyCodeBlockStyle(p);
            p.Inlines.Clear();
            p.DataContext = code;
            p.Resources["IsActiveCodeBlock"] = false;

            var norm = SyntaxHighlighter.NormalizeLanguage(language);
            p.Tag = "CodeBlock:" + norm;

            var displayName = GetLanguageDisplayName(norm);

            // Header line: top-right language name / copy badge
            var headerDock = new DockPanel
            {
                LastChildFill = false,
                Margin = new Thickness(0, -2, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Focusable = false,
                Background = null
            };

            headerDock.Width = targetWidth > 50 ? targetWidth : 600;

            var badgeText = new TextBlock
            {
                Text = displayName,
                FontSize = 11.5,
                FontFamily = new FontFamily("Segoe UI, -apple-system, BlinkMacSystemFont, Roboto, sans-serif"),
                Foreground = IsDarkMode
                    ? new SolidColorBrush(Color.FromRgb(140, 140, 150))
                    : new SolidColorBrush(Color.FromRgb(100, 100, 110)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var badgeBorder = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Click to copy code",
                Child = badgeText
            };

            badgeBorder.MouseEnter += (s, e) =>
            {
                badgeBorder.Background = IsDarkMode
                    ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0));
            };
            badgeBorder.MouseLeave += (s, e) =>
            {
                badgeBorder.Background = Brushes.Transparent;
            };

            // Copy on click
            badgeBorder.PreviewMouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true; // Prevent editor from moving caret into code block
                try
                {
                    var codeToCopy = p.DataContext as string ?? ExtractCodeFromParagraph(p);
                    Clipboard.SetText(codeToCopy);
                    badgeText.Text = "Copied!";
                    badgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)); // emerald green
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    timer.Tick += (ts, te) =>
                    {
                        timer.Stop();
                        badgeText.Text = displayName;
                        badgeText.Foreground = IsDarkMode
                            ? new SolidColorBrush(Color.FromRgb(140, 140, 150))
                            : new SolidColorBrush(Color.FromRgb(100, 100, 110));
                    };
                    timer.Start();
                }
                catch { }
            };

            DockPanel.SetDock(badgeBorder, Dock.Right);
            headerDock.Children.Add(badgeBorder);

            var container = new InlineUIContainer(headerDock)
            {
                BaselineAlignment = BaselineAlignment.Center,
                Focusable = false
            };
            p.Inlines.Add(container);
            p.Inlines.Add(new LineBreak());

            // Code lines with syntax highlighting
            if (!string.IsNullOrEmpty(code))
            {
                var highlightedInlines = SyntaxHighlighter.Highlight(code, norm);
                foreach (var inl in highlightedInlines)
                {
                    p.Inlines.Add(inl);
                }
            }
            else
            {
                p.Inlines.Add(new Run(string.Empty));
            }
        }

        /// <summary>
        /// Active (SCP) Mode: Displays ```<language> on line 1, code in middle, and ``` on the last line.
        /// Triggered when the user places the cursor inside the code block.
        /// </summary>
        public static void PopulateActiveCodeBlockParagraph(Paragraph p, string code, string language)
        {
            ApplyCodeBlockStyle(p);
            p.Inlines.Clear();
            p.DataContext = code;
            p.Resources["IsActiveCodeBlock"] = true;

            var norm = SyntaxHighlighter.NormalizeLanguage(language);
            p.Tag = "CodeBlock:" + norm;

            var fenceBrush = IsDarkMode 
                ? new SolidColorBrush(Color.FromRgb(113, 113, 122)) 
                : new SolidColorBrush(Color.FromRgb(161, 161, 170));
            var langBrush = IsDarkMode
                ? new SolidColorBrush(Color.FromRgb(96, 165, 250))
                : new SolidColorBrush(Color.FromRgb(37, 99, 235));

            fenceBrush.Freeze();
            langBrush.Freeze();

            // Line 1: ```<language>
            p.Inlines.Add(new Run("```") { Foreground = fenceBrush });
            if (!string.IsNullOrEmpty(norm) && norm != "text")
            {
                p.Inlines.Add(new Run(norm) { Foreground = langBrush, FontWeight = FontWeights.SemiBold });
            }
            p.Inlines.Add(new LineBreak());

            // Middle lines: syntax highlighted code
            if (!string.IsNullOrEmpty(code))
            {
                var highlightedInlines = SyntaxHighlighter.Highlight(code, norm);
                foreach (var inl in highlightedInlines)
                {
                    p.Inlines.Add(inl);
                }
                p.Inlines.Add(new LineBreak());
            }
            else
            {
                p.Inlines.Add(new Run(string.Empty));
                p.Inlines.Add(new LineBreak());
            }

            // Last line: ```
            p.Inlines.Add(new Run("```") { Foreground = fenceBrush });
        }

        /// <summary>
        /// Extracts code text from either a Paragraph code block or a legacy BlockUIContainer code block.
        /// </summary>
        public static string ExtractCodeFromBlock(Block block)
        {
            if (block is BlockUIContainer container)
            {
                if (container.Child is Border card && card.Tag is RichTextBox rtb)
                {
                    return new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text.TrimEnd('\r', '\n');
                }
                return string.Empty;
            }

            if (block is Paragraph p)
            {
                return ExtractCodeFromParagraph(p);
            }

            return string.Empty;
        }

        /// <summary>
        /// Extracts code text from an Obsidian-style code block paragraph, handling both Normal and Active modes.
        /// </summary>
        public static string ExtractCodeFromParagraph(Paragraph p)
        {
            if (p == null) return string.Empty;

            bool isActive = p.Resources["IsActiveCodeBlock"] as bool? ?? false;
            if (!isActive)
            {
                if (p.DataContext is string savedCode)
                {
                    return savedCode;
                }

                // Fallback: extract from inlines skipping header
                var runs = new List<string>();
                bool skippedHeader = false;
                foreach (var inl in p.Inlines)
                {
                    if (!skippedHeader)
                    {
                        if (inl is LineBreak) skippedHeader = true;
                        continue;
                    }
                    if (inl is Run r) runs.Add(r.Text);
                    else if (inl is LineBreak) runs.Add("\n");
                }
                return string.Concat(runs);
            }

            var fullText = new TextRange(p.ContentStart, p.ContentEnd).Text.Replace("\r\n", "\n").TrimEnd('\n');
            var lines = fullText.Split('\n').ToList();
            if (lines.Count == 0) return string.Empty;

            // Remove opening fence line if present
            if (lines[0].TrimStart().StartsWith("```"))
            {
                lines.RemoveAt(0);
            }

            // Remove closing fence line if present
            if (lines.Count > 0 && lines[^1].Trim() == "```")
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Extracts language from an active paragraph's opening fence line, or from Tag.
        /// </summary>
        public static string ExtractLanguageFromParagraph(Paragraph p)
        {
            if (p == null) return "text";

            bool isActive = p.Resources["IsActiveCodeBlock"] as bool? ?? false;
            if (isActive)
            {
                var firstLine = new TextRange(p.ContentStart, p.ContentEnd).Text.Replace("\r\n", "\n").Split('\n').FirstOrDefault() ?? string.Empty;
                if (firstLine.TrimStart().StartsWith("```"))
                {
                    var fenceLang = firstLine.TrimStart().Substring(3).Trim();
                    if (!string.IsNullOrEmpty(fenceLang))
                    {
                        return SyntaxHighlighter.NormalizeLanguage(fenceLang);
                    }
                }
            }

            return GetCodeBlockLanguage(p);
        }

        /// <summary>
        /// Updates the code block language and tag, refreshing syntax highlighting.
        /// </summary>
        public static void SetCodeBlockLanguage(Block block, string language)
        {
            if (block == null) return;
            var norm = SyntaxHighlighter.NormalizeLanguage(language);

            if (block is BlockUIContainer container)
            {
                if (container.Child is Border card && card.Child is Grid mainGrid)
                {
                    foreach (var child in mainGrid.Children)
                    {
                        if (child is Border headerBorder && headerBorder.Child is DockPanel headerDock)
                        {
                            foreach (var dockChild in headerDock.Children)
                            {
                                if (dockChild is Border langBadge && langBadge.Tag is Action<string> apply)
                                {
                                    apply(norm);
                                    return;
                                }
                            }
                        }
                    }
                }
                container.Tag = "CodeBlock:" + norm;
                return;
            }

            if (block is Paragraph p)
            {
                p.Tag = "CodeBlock:" + norm;
                var code = ExtractCodeFromParagraph(p);
                PopulateCodeBlockParagraph(p, code, norm);
            }
        }

        /// <summary>
        /// Determines if a block is an Obsidian or legacy code block.
        /// </summary>
        public static bool IsCodeBlock(Block? block)
        {
            if (block == null) return false;
            if (block.Tag is string tag && (tag == TagCodeBlock || tag.StartsWith("CodeBlock:"))) return true;
            return false;
        }

        /// <summary>
        /// Extracts language from a code block's tag or its first fence line.
        /// </summary>
        public static string GetCodeBlockLanguage(Paragraph p)
        {
            if (p.Tag is string tag && tag.StartsWith("CodeBlock:"))
            {
                return tag.Substring("CodeBlock:".Length);
            }

            var firstLine = new TextRange(p.ContentStart, p.ContentEnd).Text.Replace("\r\n", "\n").Split('\n').FirstOrDefault() ?? string.Empty;
            if (firstLine.TrimStart().StartsWith("```"))
            {
                var fenceLang = firstLine.TrimStart().Substring(3).Trim();
                return SyntaxHighlighter.NormalizeLanguage(fenceLang);
            }

            return "text";
        }

        /// <summary>
        /// Formats raw language codes into clean display titles (e.g. "powershell" -> "PowerShell").
        /// </summary>
        public static string GetLanguageDisplayName(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "Code";
            var norm = SyntaxHighlighter.NormalizeLanguage(lang);
            return norm switch
            {
                "powershell" => "PowerShell",
                "csharp" => "C#",
                "javascript" => "JavaScript",
                "typescript" => "TypeScript",
                "python" => "Python",
                "bash" => "Bash",
                "batch" => "CMD",
                "sql" => "SQL",
                "json" => "JSON",
                "html" => "HTML",
                "css" => "CSS",
                "xml" => "XML",
                "yaml" => "YAML",
                "markdown" => "Markdown",
                "cpp" => "C++",
                "c" => "C",
                "rust" => "Rust",
                "go" => "Go",
                "java" => "Java",
                "php" => "PHP",
                "dockerfile" => "Dockerfile",
                "text" => "Text",
                _ => char.ToUpperInvariant(norm[0]) + norm.Substring(1)
            };
        }

        /// <summary>
        /// Updates the width of a Normal Mode code block header DockPanel to match the editor viewport.
        /// </summary>
        public static void UpdateCodeBlockHeaderWidth(Paragraph p, double width)
        {
            if (p == null || width <= 50) return;
            foreach (var inl in p.Inlines)
            {
                if (inl is InlineUIContainer container && container.Child is DockPanel dock)
                {
                    dock.Width = width;
                    break;
                }
            }
        }

        /// <summary>
        /// Reliably gets the containing Paragraph for a TextPointer, traversing inline and block parent hierarchies.
        /// </summary>
        public static Paragraph? GetContainingParagraph(TextPointer? pointer)
        {
            if (pointer == null) return null;
            if (pointer.Paragraph != null) return pointer.Paragraph;

            DependencyObject? current = pointer.Parent;
            while (current != null)
            {
                if (current is Paragraph p) return p;
                if (current is Block b) return b as Paragraph;
                if (current is FrameworkContentElement fce) current = fce.Parent;
                else if (current is FrameworkElement fe) current = fe.Parent;
                else break;
            }
            return null;
        }
    }
}
