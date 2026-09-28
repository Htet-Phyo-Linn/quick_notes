using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.ViewModels
{
    // Rename dialog and Move dialog logic.
    public partial class MainViewModel
    {
        // ── Rename dialog ──────────────────────────────────────────
        public void ShowRenameDialog(FolderNode node)
        {
            if (node == null) return;
            _nodeToRename      = node;
            RenameDialogTitle  = node.IsDirectory
                ? $"Rename Folder '{node.Name}'"
                : $"Rename Note '{node.Name}'";
            RenameDialogInput  = node.IsDirectory
                ? node.Name
                : Path.GetFileNameWithoutExtension(node.Name);
            IsRenameDialogVisible = true;
        }

        [RelayCommand]
        public async Task ConfirmRename()
        {
            if (_nodeToRename == null || string.IsNullOrWhiteSpace(RenameDialogInput))
            {
                IsRenameDialogVisible = false;
                return;
            }
            var node  = _nodeToRename;
            var name  = RenameDialogInput.Trim();
            IsRenameDialogVisible = false;
            _nodeToRename = null;
            await RenameItemAsync(node, name);
        }

        [RelayCommand]
        public void CancelRename()
        {
            IsRenameDialogVisible = false;
            _nodeToRename         = null;
        }

        // ── Move dialog ───────────────────────────────────────────
        public void ShowMoveDialog(FolderNode node)
        {
            if (node == null) return;
            _nodeToMove   = node;
            MoveDialogTitle = $"Move '{node.Name}' to Folder";

            AvailableFolders.Clear();
            AvailableFolders.Add(new FolderDestinationItem { Name = "/ (Root Notes)", FullPath = _notesRoot });
            CollectFolders(FolderNodes, AvailableFolders, "");

            if (node.IsDirectory)
            {
                var invalidPrefix = Path.GetFullPath(node.FullPath);
                var toRemove = AvailableFolders
                    .Where(f => string.Equals(f.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase) ||
                                f.FullPath.StartsWith(invalidPrefix + Path.DirectorySeparatorChar,
                                                      StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var r in toRemove)
                    AvailableFolders.Remove(r);
            }

            SelectedDestinationFolder = AvailableFolders.FirstOrDefault();
            IsMoveDialogVisible       = true;
        }

        private void CollectFolders(
            IEnumerable<FolderNode> nodes,
            System.Collections.ObjectModel.ObservableCollection<FolderDestinationItem> list,
            string prefix)
        {
            foreach (var node in nodes)
            {
                if (!node.IsDirectory) continue;
                string display = string.IsNullOrEmpty(prefix) ? "/" + node.Name : prefix + "/" + node.Name;
                list.Add(new FolderDestinationItem { Name = display, FullPath = node.FullPath });
                if (node.Children.Count > 0)
                    CollectFolders(node.Children, list, display);
            }
        }

        [RelayCommand]
        public async Task ConfirmMove()
        {
            if (_nodeToMove == null || SelectedDestinationFolder == null)
            {
                IsMoveDialogVisible = false;
                return;
            }
            var node      = _nodeToMove;
            var targetDir = SelectedDestinationFolder.FullPath;
            IsMoveDialogVisible = false;
            _nodeToMove         = null;
            await MoveItemAsync(node.FullPath, targetDir);
        }

        [RelayCommand]
        public void CancelMove()
        {
            IsMoveDialogVisible = false;
            _nodeToMove         = null;
        }
    }
}
