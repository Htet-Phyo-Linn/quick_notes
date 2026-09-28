using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickNote.App.Helpers
{
    public static partial class NotionMarkdownHelper
    {
        public static Table CreateTableBlockFromLines(List<string> tableLines)
        {
            var table = new Table
            {
                Tag = "MarkdownTable",
                CellSpacing = 0,
                BorderThickness = new Thickness(1),
                BorderBrush = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)) : new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD0)),
                Margin = new Thickness(0, 10, 0, 10),
                Background = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)) : new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xF9))
            };

            var rowGroup = new TableRowGroup();
            table.RowGroups.Add(rowGroup);

            bool isHeader = true;
            int maxCols = 0;
            foreach (var line in tableLines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("|") && trimmed.EndsWith("|"))
                {
                    var content = trimmed.Substring(1, trimmed.Length - 2);
                    var cells = content.Split('|').Select(c => c.Trim()).ToArray();

                    bool isSeparator = cells.Length > 0 && cells.All(c => c.Length > 0 && c.Contains('-') && c.All(ch => ch == '-' || ch == ':' || ch == ' '));
                    if (isSeparator)
                    {
                        isHeader = false;
                        continue;
                    }

                    if (cells.Length > maxCols) maxCols = cells.Length;

                    var row = new TableRow();
                    if (isHeader)
                    {
                        row.Background = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)) : new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xE7));
                        foreach (var cellText in cells)
                        {
                            var p = new Paragraph
                            {
                                FontWeight = FontWeights.Bold,
                                FontSize = 14,
                                Foreground = IsDarkMode ? Brushes.White : Brushes.Black,
                                Margin = new Thickness(0)
                            };
                            foreach (var inl in ParseInlineMarkdown(cellText)) p.Inlines.Add(inl);
                            var cell = new TableCell(p)
                            {
                                BorderThickness = new Thickness(0, 0, 1, 1),
                                BorderBrush = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x3E)) : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xDC)),
                                Padding = new Thickness(8, 6, 8, 6)
                            };
                            row.Cells.Add(cell);
                        }
                    }
                    else
                    {
                        foreach (var cellText in cells)
                        {
                            var p = new Paragraph
                            {
                                FontSize = 14,
                                Foreground = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)) : new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)),
                                Margin = new Thickness(0)
                            };
                            foreach (var inl in ParseInlineMarkdown(cellText)) p.Inlines.Add(inl);
                            var cell = new TableCell(p)
                            {
                                BorderThickness = new Thickness(0, 0, 1, 1),
                                BorderBrush = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)) : new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEB)),
                                Padding = new Thickness(8, 6, 8, 6)
                            };
                            row.Cells.Add(cell);
                        }
                    }
                    rowGroup.Rows.Add(row);
                }
            }

            for (int c = 0; c < Math.Max(1, maxCols); c++)
            {
                table.Columns.Add(new TableColumn { Width = new GridLength(120, GridUnitType.Pixel) });
            }

            return table;
        }

        public static bool IsTsvTable(string text, out List<string[]> rows)
        {
            rows = new List<string[]>();
            if (string.IsNullOrWhiteSpace(text)) return false;

            var lines = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return false;

            int tabCountInFirstLine = lines[0].Count(c => c == '\t');
            if (tabCountInFirstLine < 1) return false;

            foreach (var line in lines)
            {
                var cells = line.Split('\t');
                rows.Add(cells);
            }

            return rows.Count > 0;
        }

        public static Table CreateTableBlockFromTsv(List<string[]> rows)
        {
            var table = new Table
            {
                Tag = "MarkdownTable",
                CellSpacing = 0,
                BorderThickness = new Thickness(1),
                BorderBrush = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)) : new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD0)),
                Margin = new Thickness(0, 10, 0, 10),
                Background = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)) : new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xF9))
            };

            var rowGroup = new TableRowGroup();
            table.RowGroups.Add(rowGroup);

            int maxCols = rows.Max(r => r.Length);
            for (int c = 0; c < Math.Max(1, maxCols); c++)
            {
                table.Columns.Add(new TableColumn { Width = new GridLength(120, GridUnitType.Pixel) });
            }

            bool isHeader = true;
            foreach (var rowCells in rows)
            {
                var row = new TableRow();
                if (isHeader)
                {
                    row.Background = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)) : new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xE7));
                }

                for (int i = 0; i < maxCols; i++)
                {
                    string cellText = i < rowCells.Length ? rowCells[i].Trim() : string.Empty;
                    var p = new Paragraph
                    {
                        FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                        FontSize = 14,
                        Foreground = isHeader ? (IsDarkMode ? Brushes.White : Brushes.Black) : (IsDarkMode ? new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)) : new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30))),
                        Margin = new Thickness(0)
                    };
                    p.Inlines.Add(new Run(cellText));

                    var cell = new TableCell(p)
                    {
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        BorderBrush = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x3E)) : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xDC)),
                        Padding = new Thickness(8, 6, 8, 6)
                    };
                    row.Cells.Add(cell);
                }

                rowGroup.Rows.Add(row);
                isHeader = false;
            }

            return table;
        }

        public static Table CreateTableBlock(int rows = 3, int cols = 3)
        {
            var table = new Table
            {
                Tag = "MarkdownTable",
                CellSpacing = 0,
                BorderThickness = new Thickness(1),
                BorderBrush = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)) : new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD0)),
                Margin = new Thickness(0, 10, 0, 10),
                Background = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)) : new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xF9))
            };

            for (int c = 0; c < cols; c++)
            {
                table.Columns.Add(new TableColumn { Width = new GridLength(120, GridUnitType.Pixel) });
            }

            var rowGroup = new TableRowGroup();
            table.RowGroups.Add(rowGroup);

            for (int r = 0; r < rows; r++)
            {
                var row = new TableRow();
                bool isHeader = (r == 0);
                if (isHeader)
                {
                    row.Background = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)) : new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xE7));
                }

                for (int c = 0; c < cols; c++)
                {
                    var cell = CreateEmptyTableCell(isHeader);
                    row.Cells.Add(cell);
                }
                rowGroup.Rows.Add(row);
            }

            return table;
        }

        public static TableCell CreateEmptyTableCell(bool isHeader = false)
        {
            var p = new Paragraph
            {
                FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 14,
                Foreground = isHeader ? (IsDarkMode ? Brushes.White : Brushes.Black) : (IsDarkMode ? new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)) : new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30))),
                Margin = new Thickness(0)
            };
            p.Inlines.Add(new Run(string.Empty));

            return new TableCell(p)
            {
                BorderThickness = new Thickness(0, 0, 1, 1),
                BorderBrush = IsDarkMode ? new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x3E)) : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xDC)),
                Padding = new Thickness(8, 6, 8, 6)
            };
        }

        public static void InsertRow(TableCell activeCell, bool above)
        {
            if (activeCell == null) return;
            var activeRow = activeCell.Parent as TableRow;
            var group = activeRow?.Parent as TableRowGroup;
            if (activeRow == null || group == null) return;

            int colCount = activeRow.Cells.Count;
            var newRow = new TableRow();
            for (int c = 0; c < colCount; c++)
            {
                newRow.Cells.Add(CreateEmptyTableCell(false));
            }

            int index = group.Rows.IndexOf(activeRow);
            if (above)
            {
                if (index == 0) index = 1;
                group.Rows.Insert(index, newRow);
            }
            else
            {
                group.Rows.Insert(index + 1, newRow);
            }
        }

        public static void DeleteRow(TableCell activeCell)
        {
            if (activeCell == null) return;
            var activeRow = activeCell.Parent as TableRow;
            var group = activeRow?.Parent as TableRowGroup;
            if (activeRow == null || group == null) return;

            if (group.Rows.Count > 1)
            {
                group.Rows.Remove(activeRow);
            }
        }

        public static void InsertColumn(TableCell activeCell, bool left)
        {
            if (activeCell == null) return;
            var activeRow = activeCell.Parent as TableRow;
            var group = activeRow?.Parent as TableRowGroup;
            var table = group?.Parent as Table;
            if (activeRow == null || group == null || table == null) return;

            int colIndex = activeRow.Cells.IndexOf(activeCell);
            if (colIndex < 0) return;

            int insertIndex = left ? colIndex : colIndex + 1;

            foreach (var row in group.Rows)
            {
                bool isHeader = (group.Rows.IndexOf(row) == 0);
                var newCell = CreateEmptyTableCell(isHeader);
                if (insertIndex >= row.Cells.Count)
                {
                    row.Cells.Add(newCell);
                }
                else
                {
                    row.Cells.Insert(insertIndex, newCell);
                }
            }

            table.Columns.Add(new TableColumn { Width = new GridLength(120, GridUnitType.Pixel) });
        }

        public static void DeleteColumn(TableCell activeCell)
        {
            if (activeCell == null) return;
            var activeRow = activeCell.Parent as TableRow;
            var group = activeRow?.Parent as TableRowGroup;
            var table = group?.Parent as Table;
            if (activeRow == null || group == null || table == null) return;

            int colIndex = activeRow.Cells.IndexOf(activeCell);
            if (colIndex < 0) return;

            if (activeRow.Cells.Count > 1)
            {
                foreach (var row in group.Rows)
                {
                    if (colIndex < row.Cells.Count)
                    {
                        row.Cells.RemoveAt(colIndex);
                    }
                }

                if (table.Columns.Count > 0)
                {
                    int rmIndex = Math.Min(colIndex, table.Columns.Count - 1);
                    table.Columns.RemoveAt(rmIndex);
                }
            }
        }

        public static void DeleteTable(TableCell activeCell, RichTextBox rtb)
        {
            if (activeCell == null || rtb == null) return;
            var activeRow = activeCell.Parent as TableRow;
            var group = activeRow?.Parent as TableRowGroup;
            var table = group?.Parent as Table;
            if (table != null)
            {
                rtb.Document.Blocks.Remove(table);
            }
        }

        public static void AdjustColumnWidth(TableCell activeCell, double delta)
        {
            if (activeCell == null) return;
            var activeRow = activeCell.Parent as TableRow;
            var group = activeRow?.Parent as TableRowGroup;
            var table = group?.Parent as Table;
            if (activeRow == null || table == null) return;

            int colIndex = activeRow.Cells.IndexOf(activeCell);
            if (colIndex < 0) return;

            while (table.Columns.Count <= colIndex)
            {
                table.Columns.Add(new TableColumn { Width = new GridLength(120, GridUnitType.Pixel) });
            }

            var col = table.Columns[colIndex];
            double currentWidth = col.Width.IsAbsolute ? col.Width.Value : 120.0;
            double newWidth = Math.Clamp(currentWidth + delta, 60.0, 600.0);
            col.Width = new GridLength(newWidth, GridUnitType.Pixel);
        }

        public static void AlignCellText(TableCell activeCell, TextAlignment alignment)
        {
            if (activeCell == null) return;
            activeCell.TextAlignment = alignment;
            foreach (var block in activeCell.Blocks)
            {
                block.TextAlignment = alignment;
            }
        }
    }
}
