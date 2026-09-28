using System;
using System.Collections.Generic;
using System.Linq;
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
        private Paragraph EnsureParagraphAfterTable(Table table)
        {
            if (table.NextBlock is Paragraph p)
            {
                return p;
            }

            var newP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
            if (table.NextBlock != null)
            {
                NotionEditorBox.Document.Blocks.InsertBefore(table.NextBlock, newP);
            }
            else
            {
                NotionEditorBox.Document.Blocks.Add(newP);
            }
            return newP;
        }

        private Paragraph EnsureParagraphBeforeTable(Table table)
        {
            if (table.PreviousBlock is Paragraph p)
            {
                return p;
            }

            var newP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, string.Empty);
            NotionEditorBox.Document.Blocks.InsertBefore(table, newP);
            return newP;
        }

        private void ExitTableToBelow(Table table)
        {
            var p = EnsureParagraphAfterTable(table);
            NotionEditorBox.CaretPosition = p.ContentStart;
            NotionEditorBox.Focus();
            ResetCaretFormatting();
            TableToolbar.Visibility = Visibility.Collapsed;
        }

        private void ExitTableToAbove(Table table)
        {
            var p = EnsureParagraphBeforeTable(table);
            NotionEditorBox.CaretPosition = p.ContentEnd;
            NotionEditorBox.Focus();
            ResetCaretFormatting();
            TableToolbar.Visibility = Visibility.Collapsed;
        }

        private bool HandleTableKeyNavigation(KeyEventArgs e)
        {
            var cell = GetParentTableCell(NotionEditorBox.CaretPosition);
            if (cell == null) return false;

            var row = cell.Parent as TableRow;
            var group = row?.Parent as TableRowGroup;
            var table = group?.Parent as Table;
            if (row == null || group == null || table == null) return false;

            int cellIndex = row.Cells.IndexOf(cell);
            int rowIndex = group.Rows.IndexOf(row);

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ExitTableToBelow(table);
                return true;
            }

            if (e.Key == Key.Down)
            {
                e.Handled = true;
                if (rowIndex < group.Rows.Count - 1)
                {
                    var nextRow = group.Rows[rowIndex + 1];
                    int targetCol = Math.Min(cellIndex, nextRow.Cells.Count - 1);
                    NotionEditorBox.CaretPosition = nextRow.Cells[targetCol].ContentStart;
                }
                else
                {
                    ExitTableToBelow(table);
                }
                return true;
            }

            if (e.Key == Key.Up)
            {
                e.Handled = true;
                if (rowIndex > 0)
                {
                    var prevRow = group.Rows[rowIndex - 1];
                    int targetCol = Math.Min(cellIndex, prevRow.Cells.Count - 1);
                    NotionEditorBox.CaretPosition = prevRow.Cells[targetCol].ContentStart;
                }
                else
                {
                    ExitTableToAbove(table);
                }
                return true;
            }

            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    // Move to previous cell
                    if (cellIndex > 0)
                    {
                        NotionEditorBox.CaretPosition = row.Cells[cellIndex - 1].ContentStart;
                    }
                    else if (rowIndex > 0)
                    {
                        var prevRow = group.Rows[rowIndex - 1];
                        NotionEditorBox.CaretPosition = prevRow.Cells[prevRow.Cells.Count - 1].ContentStart;
                    }
                }
                else
                {
                    // Move to next cell
                    if (cellIndex < row.Cells.Count - 1)
                    {
                        NotionEditorBox.CaretPosition = row.Cells[cellIndex + 1].ContentStart;
                    }
                    else if (rowIndex < group.Rows.Count - 1)
                    {
                        var nextRow = group.Rows[rowIndex + 1];
                        NotionEditorBox.CaretPosition = nextRow.Cells[0].ContentStart;
                    }
                    else
                    {
                        // Add new row at end
                        NotionMarkdownHelper.InsertRow(cell, false);
                        SyncMarkdownFromEditor();
                        UpdateDocumentLayoutWidth();
                        if (group.Rows.Count > rowIndex + 1)
                        {
                            NotionEditorBox.CaretPosition = group.Rows[rowIndex + 1].Cells[0].ContentStart;
                        }
                    }
                }
                return true;
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;

                // Ctrl+Enter or Shift+Enter inside table jumps out to below
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control || 
                    (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    ExitTableToBelow(table);
                    return true;
                }

                if (rowIndex < group.Rows.Count - 1)
                {
                    var nextRow = group.Rows[rowIndex + 1];
                    int targetCol = Math.Min(cellIndex, nextRow.Cells.Count - 1);
                    NotionEditorBox.CaretPosition = nextRow.Cells[targetCol].ContentStart;
                }
                else
                {
                    // On the bottom row
                    bool isCurrentRowEmpty = row.Cells.All(c => string.IsNullOrWhiteSpace(new TextRange(c.ContentStart, c.ContentEnd).Text));

                    if (isCurrentRowEmpty)
                    {
                        // Empty bottom row: clean up row and exit table!
                        if (group.Rows.Count > 2)
                        {
                            group.Rows.Remove(row);
                            SyncMarkdownFromEditor();
                            UpdateDocumentLayoutWidth();
                        }
                        ExitTableToBelow(table);
                    }
                    else
                    {
                        // Current row has text: add a new row below
                        NotionMarkdownHelper.InsertRow(cell, false);
                        SyncMarkdownFromEditor();
                        UpdateDocumentLayoutWidth();
                        if (group.Rows.Count > rowIndex + 1)
                        {
                            var nextRow = group.Rows[rowIndex + 1];
                            int targetCol = Math.Min(cellIndex, nextRow.Cells.Count - 1);
                            NotionEditorBox.CaretPosition = nextRow.Cells[targetCol].ContentStart;
                        }
                    }
                }
                return true;
            }

            return false;
        }

        private void ResetCaretFormatting()
        {
            NotionEditorBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 15.0);
            NotionEditorBox.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            NotionEditorBox.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
            NotionEditorBox.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI, -apple-system, sans-serif"));
            NotionEditorBox.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            NotionEditorBox.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
        }

        private TableCell? GetParentTableCell(TextPointer? pointer)
        {
            if (pointer == null) return null;
            var current = pointer.Parent;
            while (current != null)
            {
                if (current is TableCell cell) return cell;
                if (current is TextElement te) current = te.Parent;
                else break;
            }
            return null;
        }

        private void NotionEditorBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            var cell = GetParentTableCell(NotionEditorBox.CaretPosition);
            if (cell != null)
            {
                _activeTableCell = cell;
                TableToolbar.Visibility = Visibility.Visible;
            }
            else
            {
                _activeTableCell = null;
                TableToolbar.Visibility = Visibility.Collapsed;
            }

            HandleCodeBlockSelectionChanged();
        }

        private void BtnInsertRowAbove_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTableCell != null)
            {
                NotionMarkdownHelper.InsertRow(_activeTableCell, true);
                SyncMarkdownFromEditor();
            }
        }

        private void BtnInsertRowBelow_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTableCell != null)
            {
                NotionMarkdownHelper.InsertRow(_activeTableCell, false);
                SyncMarkdownFromEditor();
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTableCell != null)
            {
                NotionMarkdownHelper.DeleteRow(_activeTableCell);
                SyncMarkdownFromEditor();
            }
        }

        private void BtnInsertColLeft_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTableCell != null)
            {
                NotionMarkdownHelper.InsertColumn(_activeTableCell, true);
                SyncMarkdownFromEditor();
                UpdateDocumentLayoutWidth();
            }
        }

        private void BtnInsertColRight_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTableCell != null)
            {
                NotionMarkdownHelper.InsertColumn(_activeTableCell, false);
                SyncMarkdownFromEditor();
                UpdateDocumentLayoutWidth();
            }
        }

        private void BtnDeleteCol_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTableCell != null)
            {
                NotionMarkdownHelper.DeleteColumn(_activeTableCell);
                SyncMarkdownFromEditor();
                UpdateDocumentLayoutWidth();
            }
        }

        private (Table? table, int colIndex, double borderX) FindTableColumnBorderNear(Point point)
        {
            if (NotionEditorBox.Document == null) return (null, -1, 0);

            foreach (var block in NotionEditorBox.Document.Blocks)
            {
                if (block is Table table && table.RowGroups.Count > 0 && table.RowGroups[0].Rows.Count > 0)
                {
                    // 1. Determine table vertical bounds (top, bottom) and horizontal left origin (tableLeft)
                    double top = double.MaxValue;
                    double bottom = double.MinValue;
                    double tableLeft = double.MaxValue;

                    foreach (var rg in table.RowGroups)
                    {
                        foreach (var row in rg.Rows)
                        {
                            if (row.Cells.Count > 0)
                            {
                                var rStart = row.Cells[0].ContentStart.GetCharacterRect(LogicalDirection.Forward);
                                if (rStart.Top > 0 && rStart.Top < top) top = rStart.Top;

                                double cellLeft = rStart.Left - row.Cells[0].Padding.Left;
                                if (cellLeft > 0 && cellLeft < tableLeft) tableLeft = cellLeft;

                                foreach (var cell in row.Cells)
                                {
                                    var rEnd = cell.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                                    if (rEnd.Bottom > bottom) bottom = rEnd.Bottom;
                                }
                            }
                        }
                    }

                    if (top == double.MaxValue || bottom == double.MinValue || tableLeft == double.MaxValue)
                    {
                        var firstRow = table.RowGroups[0].Rows[0];
                        if (firstRow.Cells.Count == 0) continue;
                        var startRect = firstRow.Cells[0].ContentStart.GetCharacterRect(LogicalDirection.Forward);
                        top = startRect.Top;
                        bottom = startRect.Bottom + 50;
                        tableLeft = startRect.Left - firstRow.Cells[0].Padding.Left;
                    }

                    // Check if Y is within table range (with 8px top margin and 14px bottom margin)
                    if (point.Y < top - 8 || point.Y > bottom + 14)
                    {
                        continue;
                    }

                    // 2. Count columns across all rows in the table
                    int colCount = table.Columns.Count;
                    foreach (var rg in table.RowGroups)
                    {
                        foreach (var row in rg.Rows)
                        {
                            if (row.Cells.Count > colCount) colCount = row.Cells.Count;
                        }
                    }
                    if (colCount == 0) continue;

                    while (table.Columns.Count < colCount)
                    {
                        table.Columns.Add(new TableColumn { Width = new GridLength(160, GridUnitType.Pixel) });
                    }

                    // 3. Evaluate each column's right border
                    double runningBorderX = tableLeft;
                    for (int c = 0; c < colCount; c++)
                    {
                        double borderX = 0;
                        bool borderFound = false;

                        // For internal column borders (c < colCount - 1), try visual start of next cell (c + 1)
                        if (c < colCount - 1)
                        {
                            foreach (var rg in table.RowGroups)
                            {
                                foreach (var row in rg.Rows)
                                {
                                    if (row.Cells.Count > c + 1)
                                    {
                                        var nextStart = row.Cells[c + 1].ContentStart.GetCharacterRect(LogicalDirection.Forward);
                                        if (nextStart.Left > 0)
                                        {
                                            borderX = nextStart.Left - row.Cells[c + 1].Padding.Left;
                                            borderFound = true;
                                            break;
                                        }
                                    }
                                }
                                if (borderFound) break;
                            }
                        }

                        // For the LAST column (c == colCount - 1) or fallback:
                        var col = table.Columns[c];
                        double colWidth = col.Width.IsAbsolute ? col.Width.Value : 160.0;
                        runningBorderX += colWidth;

                        if (!borderFound)
                        {
                            borderX = runningBorderX;
                        }
                        else
                        {
                            runningBorderX = borderX;
                        }

                        // Allow generous hit test zone (10px on either side of the border)
                        if (Math.Abs(point.X - borderX) <= 10)
                        {
                            return (table, c, borderX);
                        }
                    }
                }
            }

            return (null, -1, 0);
        }

        private void NotionEditorBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            var point = e.GetPosition(NotionEditorBox);

            if (_isDraggingColumnBorder && _resizingTable != null && _resizingColIndex >= 0)
            {
                double delta = point.X - _dragStartX;
                double newWidth = Math.Max(40.0, _initialColWidth + delta);

                if (_resizingColIndex < _resizingTable.Columns.Count)
                {
                    _resizingTable.Columns[_resizingColIndex].Width = new GridLength(newWidth, GridUnitType.Pixel);
                    UpdateDocumentLayoutWidth();
                }

                Mouse.OverrideCursor = Cursors.SizeWE;
                e.Handled = true;
                return;
            }

            var (hoverTable, _, _) = FindTableColumnBorderNear(point);
            if (hoverTable != null)
            {
                Mouse.OverrideCursor = Cursors.SizeWE;
            }
            else
            {
                var hoverTextPos = NotionEditorBox.GetPositionFromPoint(point, snapToText: false);
                if (hoverTextPos != null && hoverTextPos.Paragraph != null)
                {
                    var hp = hoverTextPos.Paragraph;
                    var hTag = hp.Tag as string;
                    if (hTag == NotionMarkdownHelper.TagH1 || hTag == NotionMarkdownHelper.TagH2 || hTag == NotionMarkdownHelper.TagH3)
                    {
                        var rangeToClick = new TextRange(hp.ContentStart, hoverTextPos);
                        bool isFolded = NotionMarkdownHelper.FoldedHeadingSections.ContainsKey(hp);
                        var trToEnd = new TextRange(hoverTextPos, hp.ContentEnd);
                        if (rangeToClick.Text.Length <= 3 || (isFolded && trToEnd.Text.Trim().Length <= 4))
                        {
                            Mouse.OverrideCursor = Cursors.Hand;
                            return;
                        }
                    }
                }

                if (Mouse.OverrideCursor == Cursors.SizeWE || Mouse.OverrideCursor == Cursors.Hand)
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void NotionEditorBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingColumnBorder)
            {
                _isDraggingColumnBorder = false;
                _resizingTable = null;
                _resizingColIndex = -1;
                NotionEditorBox.ReleaseMouseCapture();
                Mouse.OverrideCursor = null;
                SyncMarkdownFromEditor();
                UpdateDocumentLayoutWidth();
                e.Handled = true;
            }
        }

        private void NotionEditorBox_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isDraggingColumnBorder && Mouse.OverrideCursor == Cursors.SizeWE)
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void BtnAlignLeft_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTableCell != null)
            {
                NotionMarkdownHelper.AlignCellText(_activeTableCell, TextAlignment.Left);
                SyncMarkdownFromEditor();
            }
        }

        private void BtnAlignCenter_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTableCell != null)
            {
                NotionMarkdownHelper.AlignCellText(_activeTableCell, TextAlignment.Center);
                SyncMarkdownFromEditor();
            }
        }

        private void BtnAlignRight_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTableCell != null)
            {
                NotionMarkdownHelper.AlignCellText(_activeTableCell, TextAlignment.Right);
                SyncMarkdownFromEditor();
            }
        }

        private void BtnDeleteTable_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTableCell != null)
            {
                NotionMarkdownHelper.DeleteTable(_activeTableCell, NotionEditorBox);
                SyncMarkdownFromEditor();
                TableToolbar.Visibility = Visibility.Collapsed;
                UpdateDocumentLayoutWidth();
            }
        }
    }
}
