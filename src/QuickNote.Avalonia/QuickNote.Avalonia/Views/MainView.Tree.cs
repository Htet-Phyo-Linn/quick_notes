using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.Views
{
    // File-tree drag-drop and all context-menu click handlers.
    public partial class MainView
    {
        // ── Selection ─────────────────────────────────────────────────
        private void FolderTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null) return;

            try
            {
                var selected = FolderTreeView?.SelectedItems
                    ?.OfType<FolderNode>()
                    .ToList() ?? new List<FolderNode>();

                _viewModel.SelectedFolderNodes = selected;

                var singleNode = selected.FirstOrDefault(n => !n.IsDirectory)
                              ?? selected.FirstOrDefault();
                if (singleNode != null)
                {
                    _viewModel.SelectedFolderNode = singleNode;
                    if (!singleNode.IsDirectory)
                        _viewModel.OpenNode(singleNode);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderTreeView_SelectionChanged] {ex.Message}");
            }
        }

        private void OnFolderNodeDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is Control c && c.DataContext is FolderNode node)
            {
                if (node.IsDirectory)
                {
                    node.IsExpanded = !node.IsExpanded;
                    e.Handled = true;
                }
                else
                {
                    StartInlineRename(node);
                    e.Handled = true;
                }
            }
        }

        private void OnTreeItemTextDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is Control c && c.DataContext is FolderNode node)
            {
                StartInlineRename(node);
                e.Handled = true;
            }
        }

        // ── Drag and drop ───────────────────────────────────────────
        private Point? _dragStartPoint;
        private FolderNode? _draggedNode;
        private PointerPressedEventArgs? _dragStartPointerEvent;
        private FolderNode? _lastContextNode;

        private FolderNode? GetNodeFromVisualSource(object? source)
        {
            if (source is FolderNode directNode) return directNode;

            Visual? current = source as Visual;
            while (current != null && current != FolderTreeView)
            {
                if (current is Control c && c.DataContext is FolderNode node)
                    return node;

                if (current is TreeViewItem tvi && tvi.DataContext is FolderNode tviNode)
                    return tviNode;

                current = current.GetVisualParent();
            }
            return null;
        }

        private void OnTreeContextMenuOpened(object? sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu cm && cm.PlacementTarget != null)
            {
                var node = GetNodeFromVisualSource(cm.PlacementTarget);
                if (node != null)
                {
                    _lastContextNode = node;
                    if (_viewModel != null)
                    {
                        _viewModel.SelectedFolderNode = node;
                        node.IsSelected = true;
                    }
                }
            }
        }

        private void FolderTreeView_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(FolderTreeView);
            var node  = GetNodeFromVisualSource(e.Source);

            if (point.Properties.IsLeftButtonPressed)
            {
                if (node != null)
                {
                    _dragStartPoint        = e.GetPosition(this);
                    _draggedNode           = node;
                    _dragStartPointerEvent = e;
                }
                else
                {
                    _dragStartPoint        = null;
                    _draggedNode           = null;
                    _dragStartPointerEvent = null;
                }
            }
            else if (point.Properties.IsRightButtonPressed && node != null && _viewModel != null)
            {
                _lastContextNode = node;
                _viewModel.SelectedFolderNode = node;
                node.IsSelected = true;
            }
        }

        private void FolderTreeView_ContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            var node = GetNodeFromVisualSource(e.Source);
            if (node != null)
            {
                _lastContextNode = node;
                if (_viewModel != null)
                {
                    _viewModel.SelectedFolderNode = node;
                    node.IsSelected = true;
                }
            }
        }

        private async void FolderTreeView_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_dragStartPoint.HasValue || _draggedNode == null || _dragStartPointerEvent == null)
                return;
            if (!e.GetCurrentPoint(FolderTreeView).Properties.IsLeftButtonPressed)
                return;

            var diff = _dragStartPoint.Value - e.GetPosition(this);
            if (System.Math.Abs(diff.X) > 6 || System.Math.Abs(diff.Y) > 6)
            {
                var startEvent     = _dragStartPointerEvent;
                _dragStartPoint        = null;
                _dragStartPointerEvent = null;
                var data = new DataTransfer();
                await DragDrop.DoDragDropAsync(startEvent, data, DragDropEffects.Move);
            }
        }

        private void FolderTreeView_DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = _draggedNode != null ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private async void FolderTreeView_Drop(object? sender, DragEventArgs e)
        {
            if (_viewModel == null || _draggedNode == null) return;
            var draggedNode = _draggedNode;
            _draggedNode    = null;

            var targetNode = GetNodeFromVisualSource(e.Source);
            string targetDir;
            if (targetNode != null)
            {
                if (string.Equals(draggedNode.FullPath, targetNode.FullPath,
                                  StringComparison.OrdinalIgnoreCase)) return;
                targetDir = targetNode.IsDirectory
                    ? targetNode.FullPath
                    : Path.GetDirectoryName(targetNode.FullPath) ?? _viewModel.NotesRoot;
            }
            else
            {
                targetDir = _viewModel.NotesRoot;
            }

            if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir)) return;

            var currentParent = Path.GetDirectoryName(draggedNode.FullPath);
            if (string.Equals(currentParent, targetDir, StringComparison.OrdinalIgnoreCase)) return;

            if (draggedNode.IsDirectory)
            {
                var fullSource = Path.GetFullPath(draggedNode.FullPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullTarget = Path.GetFullPath(targetDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (fullTarget.StartsWith(fullSource + Path.DirectorySeparatorChar,
                                          StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fullSource, fullTarget, StringComparison.OrdinalIgnoreCase))
                {
                    _viewModel.StatusText = "Cannot move a folder into its own subfolder.";
                    return;
                }
            }

            await _viewModel.MoveItemAsync(draggedNode.FullPath, targetDir);
        }

        // ── Context-menu helper ───────────────────────────────────────
        private FolderNode? GetNodeFromMenuItem(object? sender)
        {
            if (sender is MenuItem mi)
            {
                if (mi.DataContext is FolderNode node) return node;
                if (mi.Parent is ContextMenu cm && cm.PlacementTarget != null)
                {
                    var targetNode = GetNodeFromVisualSource(cm.PlacementTarget);
                    if (targetNode != null) return targetNode;
                }
            }
            if (_lastContextNode != null) return _lastContextNode;
            return _viewModel?.SelectedFolderNode;
        }

        // ── Per-item context-menu actions ─────────────────────────────
        private async void OnTreeNewNoteClick(object? sender, RoutedEventArgs e)
        {
            var node = GetNodeFromMenuItem(sender) ?? _viewModel?.SelectedFolderNode;
            if (_viewModel == null || node == null) return;
            string targetDir = node.IsDirectory
                ? node.FullPath
                : Path.GetDirectoryName(node.FullPath) ?? _viewModel.NotesRoot;
            await _viewModel.NewNoteAsync(targetDir);
        }

        private async void OnTreeNewSubfolderClick(object? sender, RoutedEventArgs e)
        {
            var node = GetNodeFromMenuItem(sender) ?? _viewModel?.SelectedFolderNode;
            if (_viewModel != null && node != null)
                await _viewModel.NewSubfolderInFolder(node);
        }

        private void OnTreeOpenNoteClick(object? sender, RoutedEventArgs e)
        {
            var node = GetNodeFromMenuItem(sender) ?? _viewModel?.SelectedFolderNode;
            if (_viewModel != null && node != null) _viewModel.OpenNode(node);
        }

        private void OnTreeRenameClick(object? sender, RoutedEventArgs e)
        {
            var node = GetNodeFromMenuItem(sender) ?? _viewModel?.SelectedFolderNode;
            if (node != null)
            {
                StartInlineRename(node);
            }
            else if (_viewModel?.SelectedFolderNode != null)
            {
                StartInlineRename(_viewModel.SelectedFolderNode);
            }
        }

        public void StartInlineRename(FolderNode node)
        {
            if (node == null) return;
            if (_viewModel != null)
            {
                foreach (var rootNode in _viewModel.FolderNodes)
                    CancelInlineEditingRecursive(rootNode);

                _viewModel.SelectedFolderNode = node;
            }

            node.EditingText = node.IsDirectory
                ? node.Name
                : Path.GetFileNameWithoutExtension(node.Name);
            node.IsSelected = true;
            node.IsEditing = true;
        }

        private void CancelInlineEditingRecursive(FolderNode node)
        {
            node.IsEditing = false;
            foreach (var child in node.Children)
                CancelInlineEditingRecursive(child);
        }

        private void InlineRenameBox_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    tb.Focus();
                    tb.SelectAll();
                }, DispatcherPriority.Input);
            }
        }

        private async void InlineRenameBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is FolderNode node)
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    await CommitInlineRename(node, tb.Text);
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    node.IsEditing = false;
                }
            }
        }

        private async void InlineRenameBox_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is FolderNode node && node.IsEditing)
            {
                await CommitInlineRename(node, tb.Text);
            }
        }

        private async Task CommitInlineRename(FolderNode node, string? newName)
        {
            if (!node.IsEditing) return;
            node.IsEditing = false;

            if (_viewModel == null || string.IsNullOrWhiteSpace(newName)) return;

            string trimmed = newName.Trim();
            string currentName = node.IsDirectory ? node.Name : Path.GetFileNameWithoutExtension(node.Name);
            if (string.Equals(trimmed, currentName, StringComparison.Ordinal)) return;

            await _viewModel.RenameItemAsync(node, trimmed);
        }

        public async Task CommitAllInlineTreeRenames()
        {
            if (_viewModel == null) return;
            var editingNodes = new List<FolderNode>();
            foreach (var root in _viewModel.FolderNodes)
            {
                CollectEditingNodes(root, editingNodes);
            }

            foreach (var node in editingNodes)
            {
                await CommitInlineRename(node, node.EditingText);
            }
        }

        private void CollectEditingNodes(FolderNode node, List<FolderNode> list)
        {
            if (node.IsEditing)
                list.Add(node);

            foreach (var child in node.Children)
                CollectEditingNodes(child, list);
        }

        public bool HasAnyTreeEditingNode()
        {
            if (_viewModel == null) return false;
            foreach (var root in _viewModel.FolderNodes)
            {
                if (HasTreeEditingRecursive(root)) return true;
            }
            return false;
        }

        private static bool HasTreeEditingRecursive(FolderNode node)
        {
            if (node.IsEditing) return true;
            foreach (var child in node.Children)
            {
                if (HasTreeEditingRecursive(child)) return true;
            }
            return false;
        }

        private void FolderTreeView_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2)
            {
                var node = _viewModel?.SelectedFolderNode;
                if (node != null)
                {
                    StartInlineRename(node);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete)
            {
                if (_viewModel != null)
                {
                    if (_viewModel.SelectedFolderNodes.Count > 0)
                        _ = _viewModel.BulkDeleteNodesAsync();
                    else if (_viewModel.SelectedFolderNode != null)
                        _ = _viewModel.DeleteNode(_viewModel.SelectedFolderNode);
                    e.Handled = true;
                }
            }
        }

        private void OnTreeMoveToClick(object? sender, RoutedEventArgs e)
        {
            var node = GetNodeFromMenuItem(sender) ?? _viewModel?.SelectedFolderNode;
            if (_viewModel != null && node != null) _viewModel.ShowMoveDialog(node);
        }

        private void OnTreeCutClick(object? sender, RoutedEventArgs e)
        {
            var node = GetNodeFromMenuItem(sender) ?? _viewModel?.SelectedFolderNode;
            if (_viewModel != null && node != null) _viewModel.CutItem(node);
        }

        private async void OnTreePasteClick(object? sender, RoutedEventArgs e)
        {
            var node = GetNodeFromMenuItem(sender) ?? _viewModel?.SelectedFolderNode;
            if (_viewModel != null && node != null)
                await _viewModel.PasteItemAsync(node);
        }

        private async void OnTreeDeleteClick(object? sender, RoutedEventArgs e)
        {
            var node = GetNodeFromMenuItem(sender) ?? _viewModel?.SelectedFolderNode;
            if (_viewModel != null && node != null)
                await _viewModel.DeleteNode(node);
        }

        private async void OnBulkDeleteClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            if (_viewModel.SelectedFolderNodes.Count == 0)
            {
                var node = GetNodeFromMenuItem(sender) ?? _viewModel.SelectedFolderNode;
                if (node != null) await _viewModel.DeleteNode(node);
            }
            else
            {
                await _viewModel.BulkDeleteNodesAsync();
            }
        }

        private void OnTreeVersionHistoryClick(object? sender, RoutedEventArgs e)
        {
            var node = GetNodeFromMenuItem(sender) ?? _viewModel?.SelectedFolderNode;
            if (node != null && !node.IsDirectory && _viewModel != null)
            {
                _ = OpenBackupHistoryWindowAsync(Path.GetFileNameWithoutExtension(node.Name));
            }
            else if (_viewModel != null)
            {
                _viewModel.OpenBackupHistory();
            }
        }

        private void OnOpenTrashBinClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.OpenTrashBin();
            }
        }

        // ── Empty-area context-menu actions ───────────────────────────
        private async void OnEmptyNewNoteClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
                await _viewModel.NewNoteAsync(_viewModel.NotesRoot);
        }

        private async void OnEmptyNewFolderClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
                await _viewModel.NewRootFolderAsync();
        }

        private async void OnEmptyPasteClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
                await _viewModel.PasteItemAsync(null);
        }

        private async void OnEmptyRefreshClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
                await _viewModel.LoadAllAsync();
        }

        private async void RenameInputBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (_viewModel != null)
                {
                    await _viewModel.ConfirmRename();
                }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _viewModel?.CancelRename();
            }
        }
    }
}
