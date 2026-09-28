using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickNote.App.Dialogs;
using QuickNote.Core.Models;

namespace QuickNote.App
{
    public partial class MainWindow
    {
        private Point? _dragStartPoint;
        private FolderNode? _draggedNode;
        private TreeViewItem? _currentDropTargetItem;

        private void FolderTreeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindVisualChild<ScrollViewer>(FolderTreeView);
            if (sv == null) return;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (sv.ScrollableWidth > 0)
                {
                    double shiftOffsetChange = (e.Delta / 120.0) * 48.0;
                    double targetHorizontal = Math.Clamp(sv.HorizontalOffset - shiftOffsetChange, 0, sv.ScrollableWidth);
                    sv.ScrollToHorizontalOffset(targetHorizontal);
                    e.Handled = true;
                }
                return;
            }

            // Dampen vertical scroll speed to match main text area: smooth ~48px per wheel notch
            double offsetChange = (e.Delta / 120.0) * 48.0;
            double targetVertical = Math.Clamp(sv.VerticalOffset - offsetChange, 0, sv.ScrollableHeight);
            sv.ScrollToVerticalOffset(targetVertical);
            e.Handled = true;
        }

        private void FolderTreeView_Collapsed(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is FolderNode node && node.IsDirectory)
            {
                _viewModel.SetFolderExpandedState(node.RelativePath, false);
            }
        }

        #region TreeView Item Right-Click & Context Menu

        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                if (item.DataContext is FolderNode node)
                {
                    // If right-clicked on an item that is already part of multiple selected items,
                    // keep the multi-selection!
                    if (_selectedNodes.Count > 1 && _selectedNodes.Contains(node))
                    {
                        item.Focus();
                        e.Handled = false;
                        return;
                    }
                }

                // Otherwise, select only this item
                ClearNodeSelection();
                if (item.DataContext is FolderNode singleNode)
                {
                    SetNodeSelected(singleNode, true);
                    _anchorNode = singleNode;
                }
                item.IsSelected = true;
                item.Focus();
                UpdateMultiSelectBar();
                e.Handled = false;
            }
        }

        private UIElement CreateMenuIcon(string geometryKey, bool isStroke = true, double strokeThickness = 1.2)
        {
            var geom = TryFindResource(geometryKey) as Geometry;
            if (geom == null) return new Grid();

            var path = new System.Windows.Shapes.Path
            {
                Data = geom
            };

            if (isStroke)
            {
                path.Stroke = (Brush)TryFindResource("NotionTextSecondary");
                path.StrokeThickness = strokeThickness;
                path.Fill = Brushes.Transparent;
                path.StrokeStartLineCap = PenLineCap.Round;
                path.StrokeEndLineCap = PenLineCap.Round;
            }
            else
            {
                path.Fill = (Brush)TryFindResource("NotionTextSecondary");
            }

            var viewbox = new Viewbox
            {
                Width = 14,
                Height = 14,
                Child = path
            };

            return viewbox;
        }

        private void FolderTreeView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var menuStyle = TryFindResource("NotionContextMenuStyle") as Style;
            var itemStyle = TryFindResource("NotionMenuItemStyle") as Style;

            var contextMenu = new ContextMenu { Style = menuStyle };

            if (_selectedNodes.Count > 1)
            {
                // Multi-selection context menu
                int count = _selectedNodes.Count;

                var deleteMultiItem = new MenuItem
                {
                    Header = $"Delete {count} Notes",
                    Icon = CreateMenuIcon("IconTrashGeometry"),
                    Style = itemStyle
                };
                deleteMultiItem.Click += async (s, args) => await DeleteSelectedNotesAsync();
                contextMenu.Items.Add(deleteMultiItem);

                var moveMultiItem = new MenuItem
                {
                    Header = $"Move {count} Notes to...",
                    Icon = CreateMenuIcon("IconMoveGeometry", strokeThickness: 1.0),
                    Style = itemStyle
                };
                moveMultiItem.Click += async (s, args) => await MoveSelectedNotesAsync();
                contextMenu.Items.Add(moveMultiItem);

                contextMenu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

                var deselectItem = new MenuItem
                {
                    Header = "Deselect All",
                    Style = itemStyle
                };
                deselectItem.Click += (s, args) =>
                {
                    ClearNodeSelection();
                    UpdateMultiSelectBar();
                };
                contextMenu.Items.Add(deselectItem);

                FolderTreeView.ContextMenu = contextMenu;
                return;
            }

            if (FolderTreeView.SelectedItem is FolderNode node)
            {
                if (node.IsDirectory)
                {
                    // Folder context menu
                    var newNoteItem = new MenuItem 
                    { 
                        Header = "New Note Here", 
                        Icon = CreateMenuIcon("IconNewNoteGeometry"), 
                        Style = itemStyle 
                    };
                    newNoteItem.Click += async (s, args) => await _viewModel.NewNoteInFolderAsync(node.FullPath);
                    contextMenu.Items.Add(newNoteItem);

                    var newFolderItem = new MenuItem 
                    { 
                        Header = "New Subfolder Here", 
                        Icon = CreateMenuIcon("IconFolderGeometry", isStroke: false), 
                        Style = itemStyle 
                    };
                    newFolderItem.Click += async (s, args) => await _viewModel.NewSubfolderInFolderAsync(node.FullPath);
                    contextMenu.Items.Add(newFolderItem);

                    contextMenu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

                    var renameItem = new MenuItem 
                    { 
                        Header = "Rename", 
                        Icon = CreateMenuIcon("IconEditGeometry"), 
                        Style = itemStyle 
                    };
                    renameItem.Click += async (s, args) =>
                    {
                        var dlg = new PromptDialog("Rename Folder", "Enter new folder name:", node.DisplayName, _viewModel.IsDarkMode)
                        {
                            Owner = this
                        };
                        if (dlg.ShowDialog() == true)
                        {
                            await _viewModel.RenameItemAsync(node, dlg.InputText);
                        }
                    };
                    contextMenu.Items.Add(renameItem);

                    var moveItem = new MenuItem 
                    { 
                        Header = "Move to...", 
                        Icon = CreateMenuIcon("IconMoveGeometry", strokeThickness: 1.0), 
                        Style = itemStyle 
                    };
                    moveItem.Click += async (s, args) =>
                    {
                        var options = _viewModel.GetAllFolderOptions()
                            .Where(o => !string.Equals(o.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase) &&
                                        !o.FullPath.StartsWith(node.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        var dlg = new FolderPickerDialog($"Move '{node.DisplayName}' to...", "Select destination folder:", "Move", options, _viewModel.IsDarkMode)
                        {
                            Owner = this
                        };
                        if (dlg.ShowDialog() == true)
                        {
                            await _viewModel.MoveItemAsync(node, dlg.SelectedFolderPath);
                            ClearNodeSelection();
                            UpdateMultiSelectBar();
                            _anchorNode = null;
                            _dragStartPoint = null;
                            _draggedNode = null;
                        }
                    };
                    contextMenu.Items.Add(moveItem);

                    var copyItem = new MenuItem 
                    { 
                        Header = "Copy to...", 
                        Icon = CreateMenuIcon("IconCopyGeometry"), 
                        Style = itemStyle 
                    };
                    copyItem.Click += async (s, args) =>
                    {
                        var options = _viewModel.GetAllFolderOptions();
                        var dlg = new FolderPickerDialog($"Copy '{node.DisplayName}' to...", "Select destination folder:", "Copy", options, _viewModel.IsDarkMode)
                        {
                            Owner = this
                        };
                        if (dlg.ShowDialog() == true)
                        {
                            await _viewModel.CopyItemAsync(node, dlg.SelectedFolderPath);
                            ClearNodeSelection();
                            UpdateMultiSelectBar();
                            _anchorNode = null;
                            _dragStartPoint = null;
                            _draggedNode = null;
                        }
                    };
                    contextMenu.Items.Add(copyItem);

                    contextMenu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

                    var deleteItem = new MenuItem 
                    { 
                        Header = "Delete Folder", 
                        Icon = CreateMenuIcon("IconTrashGeometry"), 
                        Style = itemStyle 
                    };
                    deleteItem.Click += async (s, args) =>
                    {
                        bool confirm = ConfirmDialog.ShowConfirm(this, "Delete Folder", $"Are you sure you want to delete folder '{node.DisplayName}' and all its contents?", "Delete", "Cancel", isDanger: true, isDarkMode: _viewModel.IsDarkMode);
                        if (confirm)
                        {
                            await _viewModel.DeleteNodeAsync(node);
                            ClearNodeSelection();
                            UpdateMultiSelectBar();
                            _anchorNode = null;
                            _dragStartPoint = null;
                            _draggedNode = null;
                        }
                    };
                    contextMenu.Items.Add(deleteItem);
                }
                else
                {
                    // Note context menu
                    var renameItem = new MenuItem 
                    { 
                        Header = "Rename Note", 
                        Icon = CreateMenuIcon("IconEditGeometry"), 
                        Style = itemStyle 
                    };
                    renameItem.Click += async (s, args) =>
                    {
                        var dlg = new PromptDialog("Rename Note", "Enter new note name:", node.DisplayName, _viewModel.IsDarkMode)
                        {
                            Owner = this
                        };
                        if (dlg.ShowDialog() == true)
                        {
                            await _viewModel.RenameItemAsync(node, dlg.InputText);
                            _anchorNode = null;
                            _dragStartPoint = null;
                            _draggedNode = null;
                        }
                    };
                    contextMenu.Items.Add(renameItem);

                    var moveItem = new MenuItem 
                    { 
                        Header = "Move to...", 
                        Icon = CreateMenuIcon("IconMoveGeometry", strokeThickness: 1.0), 
                        Style = itemStyle 
                    };
                    moveItem.Click += async (s, args) =>
                    {
                        var options = _viewModel.GetAllFolderOptions();
                        var dlg = new FolderPickerDialog($"Move '{node.DisplayName}' to...", "Select destination folder:", "Move", options, _viewModel.IsDarkMode)
                        {
                            Owner = this
                        };
                        if (dlg.ShowDialog() == true)
                        {
                            await _viewModel.MoveItemAsync(node, dlg.SelectedFolderPath);
                            ClearNodeSelection();
                            UpdateMultiSelectBar();
                            _anchorNode = null;
                            _dragStartPoint = null;
                            _draggedNode = null;
                        }
                    };
                    contextMenu.Items.Add(moveItem);

                    var duplicateItem = new MenuItem 
                    { 
                        Header = "Duplicate / Copy", 
                        Icon = CreateMenuIcon("IconCopyGeometry"), 
                        Style = itemStyle 
                    };
                    duplicateItem.Click += async (s, args) =>
                    {
                        var parentDir = Path.GetDirectoryName(node.FullPath) ?? _viewModel.FolderRoot?.FullPath ?? "";
                        await _viewModel.CopyItemAsync(node, parentDir);
                        ClearNodeSelection();
                        UpdateMultiSelectBar();
                        _anchorNode = null;
                        _dragStartPoint = null;
                        _draggedNode = null;
                    };
                    contextMenu.Items.Add(duplicateItem);

                    contextMenu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

                    var deleteItem = new MenuItem 
                    { 
                        Header = "Delete Note", 
                        Icon = CreateMenuIcon("IconTrashGeometry"), 
                        Style = itemStyle 
                    };
                    deleteItem.Click += async (s, args) =>
                    {
                        bool confirm = ConfirmDialog.ShowConfirm(this, "Delete Note", $"Are you sure you want to delete note '{node.DisplayName}'?", "Delete", "Cancel", isDanger: true, isDarkMode: _viewModel.IsDarkMode);
                        if (confirm)
                        {
                            await _viewModel.DeleteNodeAsync(node);
                            ClearNodeSelection();
                            UpdateMultiSelectBar();
                            _anchorNode = null;
                            _dragStartPoint = null;
                            _draggedNode = null;
                        }
                    };
                    contextMenu.Items.Add(deleteItem);
                }
            }
            else
            {
                // Background / Root context menu
                var newNoteItem = new MenuItem 
                { 
                    Header = "New Note at Root", 
                    Icon = CreateMenuIcon("IconNewNoteGeometry"), 
                    Style = itemStyle 
                };
                newNoteItem.Click += async (s, args) => await _viewModel.NewNoteAsync();
                contextMenu.Items.Add(newNoteItem);

                var newFolderItem = new MenuItem 
                { 
                    Header = "New Folder at Root", 
                    Icon = CreateMenuIcon("IconFolderGeometry", isStroke: false), 
                    Style = itemStyle 
                };
                newFolderItem.Click += async (s, args) => await _viewModel.NewFolderAsync();
                contextMenu.Items.Add(newFolderItem);

                contextMenu.Items.Add(new Separator { Background = (Brush)TryFindResource("NotionBorder") });

                var refreshItem = new MenuItem 
                { 
                    Header = "Refresh Explorer", 
                    Icon = CreateMenuIcon("IconRefreshGeometry"), 
                    Style = itemStyle 
                };
                refreshItem.Click += async (s, args) => await _viewModel.LoadAllAsync();
                contextMenu.Items.Add(refreshItem);
            }

            FolderTreeView.ContextMenu = contextMenu;
        }

        #endregion

        #region TreeView Drag & Drop

        private void FolderTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // If clicked on expander toggle button, let WPF expand/collapse without touching selection
            if (FindAncestorOrSelf<System.Windows.Controls.Primitives.ToggleButton>((DependencyObject)e.OriginalSource) != null)
            {
                _dragStartPoint = null;
                _draggedNode = null;
                return;
            }

            _dragStartPoint = e.GetPosition(null);
            var tvi = FindAncestorOrSelf<TreeViewItem>((DependencyObject)e.OriginalSource);
            _draggedNode = tvi?.DataContext as FolderNode;

            if (_draggedNode == null)
            {
                _dragStartPoint = null;
                ClearNodeSelection();
                UpdateMultiSelectBar();
                return;
            }

            bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool isCtrl  = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            if (isShift && _anchorNode != null)
            {
                // Collect all visible note nodes in display order, then range-select
                var flat = GetFlatNoteNodes();
                int anchorIdx  = flat.IndexOf(_anchorNode);
                int clickedIdx = flat.IndexOf(_draggedNode);
                if (anchorIdx >= 0 && clickedIdx >= 0)
                {
                    ClearNodeSelection();
                    int start = Math.Min(anchorIdx, clickedIdx);
                    int end   = Math.Max(anchorIdx, clickedIdx);
                    for (int i = start; i <= end; i++)
                        SetNodeSelected(flat[i], true);
                }
                else
                {
                    ClearNodeSelection();
                    SetNodeSelected(_draggedNode, true);
                    _anchorNode = _draggedNode;
                }
                e.Handled = true; // prevent WPF TreeView from resetting selection
                tvi?.Focus();
            }
            else if (isCtrl)
            {
                // Toggle this node
                bool wasSelected = _selectedNodes.Contains(_draggedNode);
                SetNodeSelected(_draggedNode, !wasSelected);
                _anchorNode = _draggedNode;
                e.Handled = true; // prevent WPF TreeView from resetting selection
                tvi?.Focus();
            }
            else
            {
                // Plain click — clear multi-selection, select just this one
                ClearNodeSelection();
                SetNodeSelected(_draggedNode, true);
                _anchorNode = _draggedNode;
            }

            UpdateMultiSelectBar();
        }

        private void FolderTreeView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = null;
            _draggedNode = null;
        }

        /// <summary>All note-only FolderNodes in current visible tree order (depth-first).</summary>
        private List<FolderNode> GetFlatNoteNodes()
        {
            var result = new List<FolderNode>();
            void Walk(IEnumerable<FolderNode> nodes)
            {
                foreach (var n in nodes)
                {
                    if (!n.IsDirectory) result.Add(n);
                    if (n.IsDirectory && n.IsExpanded) Walk(n.Children);
                }
            }
            Walk(_viewModel.FolderNodes);
            return result;
        }

        private void ClearNodeSelection()
        {
            foreach (var n in _selectedNodes) n.IsSelected = false;
            _selectedNodes.Clear();
        }

        private void SetNodeSelected(FolderNode node, bool selected)
        {
            if (selected)
            {
                if (!_selectedNodes.Contains(node)) _selectedNodes.Add(node);
                node.IsSelected = true;
            }
            else
            {
                _selectedNodes.Remove(node);
                node.IsSelected = false;
            }
        }

        private void UpdateMultiSelectBar()
        {
            bool many = _selectedNodes.Count > 1;
            MultiSelectBar.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
            if (many)
                MultiSelectLabel.Text = $"{_selectedNodes.Count} notes selected";
        }

        private async void MultiMoveBtn_Click(object sender, RoutedEventArgs e)
        {
            await MoveSelectedNotesAsync();
        }

        private async void MultiDeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            await DeleteSelectedNotesAsync();
        }

        private async Task DeleteSelectedNotesAsync()
        {
            var nodesToDelete = _selectedNodes.ToList();
            if (nodesToDelete.Count == 0) return;

            string msg = nodesToDelete.Count == 1
                ? $"Are you sure you want to delete note '{nodesToDelete[0].DisplayName}'?"
                : $"Are you sure you want to delete {nodesToDelete.Count} selected notes?";

            bool confirm = ConfirmDialog.ShowConfirm(this, "Delete Notes", msg, "Delete", "Cancel", isDanger: true, isDarkMode: _viewModel.IsDarkMode);
            if (confirm)
            {
                foreach (var node in nodesToDelete)
                {
                    if (_viewModel.CurrentNote != null && string.Equals(_viewModel.CurrentNote.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _viewModel.CurrentNote = null;
                    }
                    await _trashBinService.MoveToTrashAsync(node.FullPath);
                }
                ClearNodeSelection();
                UpdateMultiSelectBar();
                _anchorNode = null;
                _dragStartPoint = null;
                _draggedNode = null;
                await _viewModel.LoadAllAsync();
            }
        }

        private async Task MoveSelectedNotesAsync()
        {
            var nodesToMove = _selectedNodes.ToList();
            if (nodesToMove.Count == 0) return;

            var options = _viewModel.GetAllFolderOptions();
            string title = nodesToMove.Count == 1
                ? $"Move '{nodesToMove[0].DisplayName}' to..."
                : $"Move {nodesToMove.Count} notes to...";

            var dlg = new FolderPickerDialog(title, "Select destination folder:", "Move", options, _viewModel.IsDarkMode)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var node in nodesToMove)
                {
                    await _viewModel.MoveItemAsync(node, dlg.SelectedFolderPath);
                }
                ClearNodeSelection();
                UpdateMultiSelectBar();
                _anchorNode = null;
                _dragStartPoint = null;
                _draggedNode = null;
            }
        }

        private void FolderTreeView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _dragStartPoint.HasValue && _draggedNode != null)
            {
                var currentPos = e.GetPosition(null);
                var diff = _dragStartPoint.Value - currentPos;

                double minX = Math.Max(SystemParameters.MinimumHorizontalDragDistance, 10);
                double minY = Math.Max(SystemParameters.MinimumVerticalDragDistance, 10);

                if (Math.Abs(diff.X) > minX || Math.Abs(diff.Y) > minY)
                {
                    if (!File.Exists(_draggedNode.FullPath) && !Directory.Exists(_draggedNode.FullPath))
                    {
                        _dragStartPoint = null;
                        _draggedNode = null;
                        return;
                    }

                    try
                    {
                        DragDrop.DoDragDrop(FolderTreeView, _draggedNode, DragDropEffects.Move);
                    }
                    catch { }
                    finally
                    {
                        ClearDropHighlight();
                        _dragStartPoint = null;
                        _draggedNode = null;
                    }
                }
            }
        }

        private void FolderTreeView_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(FolderNode)))
            {
                e.Effects = DragDropEffects.None;
                ClearDropHighlight();
                e.Handled = true;
                return;
            }

            var draggedNode = e.Data.GetData(typeof(FolderNode)) as FolderNode;
            if (draggedNode == null)
            {
                e.Effects = DragDropEffects.None;
                ClearDropHighlight();
                e.Handled = true;
                return;
            }

            var targetItem = FindAncestorOrSelf<TreeViewItem>((DependencyObject)e.OriginalSource);
            var targetNode = targetItem?.DataContext as FolderNode;

            if (targetNode != null)
            {
                // Target is a folder
                if (targetNode.IsDirectory)
                {
                    bool isSelf = string.Equals(draggedNode.FullPath, targetNode.FullPath, StringComparison.OrdinalIgnoreCase);
                    bool isDescendant = draggedNode.IsDirectory && targetNode.IsDescendantOf(draggedNode);
                    var currentParent = Path.GetDirectoryName(draggedNode.FullPath);
                    bool isSameFolder = string.Equals(currentParent, targetNode.FullPath, StringComparison.OrdinalIgnoreCase);

                    if (!isSelf && !isDescendant && !isSameFolder)
                    {
                        e.Effects = DragDropEffects.Move;
                        SetDropHighlight(targetItem);
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                        ClearDropHighlight();
                    }
                }
                else
                {
                    // Dropped over a file -> move into that file's parent folder
                    var parentDir = Path.GetDirectoryName(targetNode.FullPath);
                    var currentParent = Path.GetDirectoryName(draggedNode.FullPath);
                    bool isSameFolder = string.Equals(currentParent, parentDir, StringComparison.OrdinalIgnoreCase);

                    if (!isSameFolder && parentDir != null)
                    {
                        e.Effects = DragDropEffects.Move;
                        SetDropHighlight(targetItem);
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                        ClearDropHighlight();
                    }
                }
            }
            else
            {
                // Dropped onto empty space in TreeView -> move to Root
                ClearDropHighlight();
                var currentParent = Path.GetDirectoryName(draggedNode.FullPath);
                var rootDir = _viewModel.FolderRoot?.FullPath;
                if (!string.Equals(currentParent, rootDir, StringComparison.OrdinalIgnoreCase))
                {
                    e.Effects = DragDropEffects.Move;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }

            e.Handled = true;
        }

        private void FolderTreeView_DragLeave(object sender, DragEventArgs e)
        {
            ClearDropHighlight();
        }

        private async void FolderTreeView_Drop(object sender, DragEventArgs e)
        {
            ClearDropHighlight();
            _dragStartPoint = null;
            _draggedNode = null;

            if (!e.Data.GetDataPresent(typeof(FolderNode))) return;
            var draggedNode = e.Data.GetData(typeof(FolderNode)) as FolderNode;
            if (draggedNode == null) return;

            if (!File.Exists(draggedNode.FullPath) && !Directory.Exists(draggedNode.FullPath))
            {
                await _viewModel.LoadAllAsync();
                return;
            }

            var targetItem = FindAncestorOrSelf<TreeViewItem>((DependencyObject)e.OriginalSource);
            var targetNode = targetItem?.DataContext as FolderNode;

            if (targetNode != null && (draggedNode == targetNode || string.Equals(draggedNode.FullPath, targetNode.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            string targetDirectory;
            if (targetNode != null)
            {
                if (targetNode.IsDirectory)
                {
                    targetDirectory = targetNode.FullPath;
                }
                else
                {
                    targetDirectory = Path.GetDirectoryName(targetNode.FullPath) ?? _viewModel.FolderRoot?.FullPath ?? "";
                }
            }
            else
            {
                targetDirectory = _viewModel.FolderRoot?.FullPath ?? "";
            }

            if (string.IsNullOrWhiteSpace(targetDirectory)) return;

            var currentDir = Path.GetDirectoryName(draggedNode.FullPath);
            if (string.Equals(currentDir, targetDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (draggedNode.IsDirectory)
            {
                var fullSource = Path.GetFullPath(draggedNode.FullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullTarget = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(fullSource, fullTarget, StringComparison.OrdinalIgnoreCase) ||
                    fullTarget.StartsWith(fullSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            try
            {
                await _viewModel.MoveItemAsync(draggedNode, targetDirectory);
            }
            catch (Exception ex)
            {
                ConfirmDialog.ShowMessage(this, "Move Item Failed", $"Could not move item:\n{ex.Message}", "OK", _viewModel.IsDarkMode);
            }
            finally
            {
                ClearNodeSelection();
                UpdateMultiSelectBar();
                _anchorNode = null;
                _dragStartPoint = null;
                _draggedNode = null;
            }

            e.Handled = true;
        }

        private void SetDropHighlight(TreeViewItem? item)
        {
            if (_currentDropTargetItem != item)
            {
                ClearDropHighlight();
                _currentDropTargetItem = item;
                if (_currentDropTargetItem != null)
                {
                    _currentDropTargetItem.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                    _currentDropTargetItem.BorderThickness = new Thickness(1);
                }
            }
        }

        private void ClearDropHighlight()
        {
            if (_currentDropTargetItem != null)
            {
                _currentDropTargetItem.BorderBrush = Brushes.Transparent;
                _currentDropTargetItem.BorderThickness = new Thickness(0);
                _currentDropTargetItem = null;
            }
        }

        private static T? FindAncestorOrSelf<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        #endregion
    }
}
