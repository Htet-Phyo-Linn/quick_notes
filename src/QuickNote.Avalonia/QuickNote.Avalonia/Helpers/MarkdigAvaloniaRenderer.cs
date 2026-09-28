using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace QuickNote.Avalonia.Helpers
{
    public static class MarkdigAvaloniaRenderer
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseTaskLists()
            .UseAutoLinks()
            .Build();

        private static readonly FontFamily CodeFont = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");

        // Notion Dark Palette
        private static readonly IBrush TextPrimary = new SolidColorBrush(Color.FromRgb(230, 230, 230));
        private static readonly IBrush TextSecondary = new SolidColorBrush(Color.FromRgb(140, 140, 140));
        private static readonly IBrush CodeBg = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        private static readonly IBrush CodeBorder = new SolidColorBrush(Color.FromRgb(45, 45, 45));
        private static readonly IBrush InlineCodeFg = new SolidColorBrush(Color.FromRgb(224, 108, 117));
        private static readonly IBrush InlineCodeBg = new SolidColorBrush(Color.FromRgb(42, 42, 46));
        private static readonly IBrush QuoteBorder = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        private static readonly IBrush QuoteBg = new SolidColorBrush(Color.FromArgb(20, 37, 99, 235));
        private static readonly IBrush RuleBrush = new SolidColorBrush(Color.FromRgb(48, 48, 48));

        public static Control Render(string markdown)
        {
            var panel = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 24)
            };

            if (string.IsNullOrWhiteSpace(markdown))
            {
                return panel;
            }

            try
            {
                var doc = Markdown.Parse(markdown, Pipeline);
                foreach (var block in doc)
                {
                    var control = RenderBlock(block);
                    if (control != null)
                    {
                        panel.Children.Add(control);
                    }
                }
            }
            catch (Exception)
            {
                panel.Children.Add(new SelectableTextBlock
                {
                    Text = markdown,
                    Foreground = TextPrimary,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            return panel;
        }

        private static Control? RenderBlock(Block block)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    return RenderHeading(heading);

                case ParagraphBlock paragraph:
                    return RenderParagraph(paragraph);

                case ListBlock list:
                    return RenderList(list);

                case FencedCodeBlock codeBlock:
                    return RenderCodeBlock(codeBlock);

                case CodeBlock codeBlock:
                    return RenderCodeBlock(codeBlock);

                case QuoteBlock quote:
                    return RenderQuote(quote);

                case ThematicBreakBlock:
                    return new Border
                    {
                        Height = 1,
                        Background = RuleBrush,
                        Margin = new Thickness(0, 10, 0, 10),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                case Table table:
                    return RenderTable(table);

                default:
                    return null;
            }
        }

        private static Control RenderHeading(HeadingBlock heading)
        {
            double fontSize = heading.Level switch
            {
                1 => 26,
                2 => 21,
                3 => 17,
                4 => 15,
                _ => 14
            };

            Thickness margin = heading.Level switch
            {
                1 => new Thickness(0, 16, 0, 4),
                2 => new Thickness(0, 12, 0, 3),
                3 => new Thickness(0, 8, 0, 2),
                _ => new Thickness(0, 6, 0, 2)
            };

            var tb = new SelectableTextBlock
            {
                FontSize = fontSize,
                FontWeight = heading.Level <= 2 ? FontWeight.Bold : FontWeight.SemiBold,
                Foreground = TextPrimary,
                Margin = margin,
                TextWrapping = TextWrapping.Wrap
            };

            AppendInlines(tb, heading.Inline);
            return tb;
        }

        private static Control RenderParagraph(ParagraphBlock paragraph)
        {
            var tb = new SelectableTextBlock
            {
                FontSize = 14.5,
                Foreground = TextPrimary,
                LineHeight = 22,
                Margin = new Thickness(0, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap
            };

            AppendInlines(tb, paragraph.Inline);
            return tb;
        }

        private static Control RenderList(ListBlock list)
        {
            var stack = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(list.Parent is ListItemBlock ? 16 : 4, 2, 0, 2)
            };

            int index = 1;
            foreach (var item in list)
            {
                if (item is ListItemBlock listItem)
                {
                    bool isTask = false;
                    bool isChecked = false;

                    // Check for task item
                    if (listItem.Count > 0 && listItem[0] is ParagraphBlock pb && pb.Inline?.FirstChild is TaskList taskList)
                    {
                        isTask = true;
                        isChecked = taskList.Checked;
                    }

                    var row = new Grid();
                    if (isTask)
                    {
                        row.ColumnDefinitions.Add(new ColumnDefinition(24, GridUnitType.Pixel));
                        row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

                        var check = new CheckBox
                        {
                            IsChecked = isChecked,
                            IsEnabled = false,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0, -2, 0, 0)
                        };
                        Grid.SetColumn(check, 0);
                        row.Children.Add(check);

                        var contentStack = new StackPanel { Spacing = 2 };
                        foreach (var subBlock in listItem)
                        {
                            var rendered = RenderBlock(subBlock);
                            if (rendered != null) contentStack.Children.Add(rendered);
                        }
                        Grid.SetColumn(contentStack, 1);
                        row.Children.Add(contentStack);
                    }
                    else if (list.IsOrdered)
                    {
                        row.ColumnDefinitions.Add(new ColumnDefinition(28, GridUnitType.Pixel));
                        row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

                        var numLabel = new TextBlock
                        {
                            Text = $"{index++}.",
                            Foreground = TextSecondary,
                            FontSize = 14,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0, 2, 0, 0)
                        };
                        Grid.SetColumn(numLabel, 0);
                        row.Children.Add(numLabel);

                        var contentStack = new StackPanel { Spacing = 2 };
                        foreach (var subBlock in listItem)
                        {
                            var rendered = RenderBlock(subBlock);
                            if (rendered != null) contentStack.Children.Add(rendered);
                        }
                        Grid.SetColumn(contentStack, 1);
                        row.Children.Add(contentStack);
                    }
                    else
                    {
                        row.ColumnDefinitions.Add(new ColumnDefinition(18, GridUnitType.Pixel));
                        row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

                        var bulletLabel = new TextBlock
                        {
                            Text = "•",
                            Foreground = TextSecondary,
                            FontSize = 14,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0, 2, 0, 0)
                        };
                        Grid.SetColumn(bulletLabel, 0);
                        row.Children.Add(bulletLabel);

                        var contentStack = new StackPanel { Spacing = 2 };
                        foreach (var subBlock in listItem)
                        {
                            var rendered = RenderBlock(subBlock);
                            if (rendered != null) contentStack.Children.Add(rendered);
                        }
                        Grid.SetColumn(contentStack, 1);
                        row.Children.Add(contentStack);
                    }

                    stack.Children.Add(row);
                }
            }

            return stack;
        }

        private static Control RenderCodeBlock(Block block)
        {
            string code = string.Empty;
            string language = string.Empty;

            if (block is FencedCodeBlock fenced)
            {
                language = fenced.Info ?? string.Empty;
                var sb = new System.Text.StringBuilder();
                foreach (var line in fenced.Lines.Lines)
                {
                    if (line.Slice.Text != null)
                    {
                        sb.AppendLine(line.Slice.ToString());
                    }
                }
                code = sb.ToString().TrimEnd('\r', '\n');
            }
            else if (block is CodeBlock cb)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var line in cb.Lines.Lines)
                {
                    if (line.Slice.Text != null)
                    {
                        sb.AppendLine(line.Slice.ToString());
                    }
                }
                code = sb.ToString().TrimEnd('\r', '\n');
            }

            var border = new Border
            {
                Background = CodeBg,
                BorderBrush = CodeBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var stack = new StackPanel { Spacing = 4 };

            if (!string.IsNullOrWhiteSpace(language))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = language.ToUpperInvariant(),
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = TextSecondary,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            stack.Children.Add(new SelectableTextBlock
            {
                Text = code,
                FontFamily = CodeFont,
                FontSize = 13,
                Foreground = TextPrimary,
                LineHeight = 19,
                TextWrapping = TextWrapping.NoWrap
            });

            border.Child = stack;
            return border;
        }

        private static Control RenderQuote(QuoteBlock quote)
        {
            var border = new Border
            {
                BorderBrush = QuoteBorder,
                BorderThickness = new Thickness(3.5, 0, 0, 0),
                Background = QuoteBg,
                CornerRadius = new CornerRadius(0, 4, 4, 0),
                Padding = new Thickness(12, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var stack = new StackPanel { Spacing = 4 };
            foreach (var child in quote)
            {
                var rendered = RenderBlock(child);
                if (rendered != null) stack.Children.Add(rendered);
            }

            border.Child = stack;
            return border;
        }

        private static Control RenderTable(Table table)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 6, 0, 6)
            };

            int rowIdx = 0;
            foreach (var block in table)
            {
                if (block is TableRow row)
                {
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                    int colIdx = 0;
                    foreach (var cellBlock in row)
                    {
                        if (cellBlock is TableCell cell)
                        {
                            while (grid.ColumnDefinitions.Count <= colIdx)
                            {
                                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                            }

                            var cellBorder = new Border
                            {
                                BorderBrush = CodeBorder,
                                BorderThickness = new Thickness(1),
                                Background = row.IsHeader ? new SolidColorBrush(Color.FromRgb(38, 38, 42)) : Brushes.Transparent,
                                Padding = new Thickness(10, 6, 10, 6)
                            };

                            var cellContent = new StackPanel();
                            foreach (var b in cell)
                            {
                                var rendered = RenderBlock(b);
                                if (rendered != null) cellContent.Children.Add(rendered);
                            }

                            cellBorder.Child = cellContent;
                            Grid.SetRow(cellBorder, rowIdx);
                            Grid.SetColumn(cellBorder, colIdx);
                            grid.Children.Add(cellBorder);

                            colIdx++;
                        }
                    }

                    rowIdx++;
                }
            }

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = grid
            };

            return scroll;
        }

        private static void AppendInlines(SelectableTextBlock tb, ContainerInline? container)
        {
            if (container == null) return;

            foreach (var inline in container)
            {
                AppendInline(tb, inline);
            }
        }

        private static void AppendInline(SelectableTextBlock tb, MdInline inline)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    tb.Inlines?.Add(new Run(lit.Content.ToString()));
                    break;

                case LineBreakInline:
                    tb.Inlines?.Add(new LineBreak());
                    break;

                case EmphasisInline emp:
                    var isBold = emp.DelimiterCount >= 2;
                    var isItalic = emp.DelimiterCount == 1 || emp.DelimiterCount == 3;
                    var span = new Span();
                    if (isBold) span.FontWeight = FontWeight.Bold;
                    if (isItalic) span.FontStyle = FontStyle.Italic;

                    foreach (var child in emp)
                    {
                        AppendInlineToSpan(span, child);
                    }
                    tb.Inlines?.Add(span);
                    break;

                case CodeInline code:
                    var codeRun = new Run($"`{code.Content}`")
                    {
                        FontFamily = CodeFont,
                        Foreground = InlineCodeFg
                    };
                    tb.Inlines?.Add(codeRun);
                    break;

                case LinkInline link:
                    if (link.IsImage)
                    {
                        var imageCtrl = CreateImageControl(link.Url, link.FirstChild is LiteralInline altLit ? altLit.Content.ToString() : string.Empty);
                        if (imageCtrl != null)
                        {
                            tb.Inlines?.Add(new InlineUIContainer(imageCtrl));
                            break;
                        }
                    }
                    var linkText = link.FirstChild is LiteralInline l ? l.Content.ToString() : (link.Url ?? string.Empty);
                    var url = link.Url;
                    var linkBtn = new Button
                    {
                        Content = linkText,
                        Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(0),
                        Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand)
                    };
                    linkBtn.Click += (s, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            QuickNote.Avalonia.Notion.NotionEditorControl.OpenUrlInDefaultBrowser(url);
                        }
                    };
                    tb.Inlines?.Add(new InlineUIContainer(linkBtn));
                    break;

                case TaskList:
                    // Handled at list item level
                    break;

                case ContainerInline container:
                    foreach (var child in container)
                    {
                        AppendInline(tb, child);
                    }
                    break;

                default:
                    tb.Inlines?.Add(new Run(inline.ToString()));
                    break;
            }
        }

        private static void AppendInlineToSpan(Span span, MdInline inline)
        {
            if (inline is LiteralInline lit)
            {
                span.Inlines.Add(new Run(lit.Content.ToString()));
            }
            else if (inline is CodeInline code)
            {
                span.Inlines.Add(new Run($"`{code.Content}`") { FontFamily = CodeFont, Foreground = InlineCodeFg });
            }
            else if (inline is ContainerInline container)
            {
                foreach (var child in container)
                {
                    AppendInlineToSpan(span, child);
                }
            }
        }

        private static Control? CreateImageControl(string? src, string alt)
        {
            if (string.IsNullOrWhiteSpace(src)) return null;

            try
            {
                global::Avalonia.Media.Imaging.Bitmap? bitmap = null;

                if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var headerEnd = src.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
                    if (headerEnd > 0)
                    {
                        var cleanB64 = src.Substring(headerEnd + 8);
                        var bytes = Convert.FromBase64String(cleanB64);
                        using var ms = new MemoryStream(bytes);
                        bitmap = new global::Avalonia.Media.Imaging.Bitmap(ms);
                    }
                }
                else if (File.Exists(src))
                {
                    bitmap = new global::Avalonia.Media.Imaging.Bitmap(src);
                }

                if (bitmap != null)
                {
                    var img = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        MaxWidth = 700,
                        MaxHeight = 500,
                        Margin = new Thickness(0, 8, 0, 8)
                    };
                    if (!string.IsNullOrWhiteSpace(alt))
                    {
                        ToolTip.SetTip(img, alt);
                    }
                    return img;
                }
            }
            catch { }

            return null;
        }
    }
}
