using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.ViewModels
{
    // File-tree operations: create folder, delete, move, rename, cut/paste, bulk ops.
    public partial class MainViewModel
    {
        // ── Create ─────────────────────────────────────────────────
        public async Task NewFolderAsync()
        {
            try
            {
                string parentDir = _notesRoot;
                if (SelectedFolderNode != null)
                {
                    parentDir = SelectedFolderNode.IsDirectory
                        ? SelectedFolderNode.FullPath
                        : Path.GetDirectoryName(SelectedFolderNode.FullPath) ?? _notesRoot;
                }

                string baseName  = "New Folder";
                string targetDir = Path.Combine(parentDir, baseName);
                int counter = 1;
                while (Directory.Exists(targetDir))
                    targetDir = Path.Combine(parentDir, $"{baseName} ({counter++})");

                await _folderService.CreateFolderAsync(parentDir, Path.GetFileName(targetDir));
                await RefreshFolderTreeAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"New folder failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task NewRootFolder() => await NewRootFolderAsync();

        public async Task NewRootFolderAsync()
        {
            try
            {
                string baseName  = "New Folder";
                string targetDir = Path.Combine(_notesRoot, baseName);
                int counter = 1;
                while (Directory.Exists(targetDir))
                    targetDir = Path.Combine(_notesRoot, $"{baseName} ({counter++})");

                await _folderService.CreateFolderAsync(_notesRoot, Path.GetFileName(targetDir));
                await RefreshFolderTreeAsync();
                StatusText = $"Created root folder '{Path.GetFileName(targetDir)}'";
            }
            catch (Exception ex)
            {
                StatusText = $"New folder failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task NewNoteInFolder(FolderNode? node)
        {
            if (node == null) return;
            string targetDir = node.IsDirectory
                ? node.FullPath
                : Path.GetDirectoryName(node.FullPath) ?? _notesRoot;
            await NewNoteAsync(targetDir);
        }

        [RelayCommand]
        public async Task NewSubfolderInFolder(FolderNode? node)
        {
            if (node == null) return;
            string parentDir = node.IsDirectory
                ? node.FullPath
                : Path.GetDirectoryName(node.FullPath) ?? _notesRoot;
            try
            {
                string baseName  = "New Folder";
                string targetDir = Path.Combine(parentDir, baseName);
                int counter = 1;
                while (Directory.Exists(targetDir))
                    targetDir = Path.Combine(parentDir, $"{baseName} ({counter++})");

                await _folderService.CreateFolderAsync(parentDir, Path.GetFileName(targetDir));
                await RefreshFolderTreeAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"New folder failed: {ex.Message}";
            }
        }

        // ── Open ──────────────────────────────────────────────────
        [RelayCommand]
        public void OpenNode(FolderNode? node)
        {
            if (node == null || node.IsDirectory) return;
            var note = Notes.FirstOrDefault(n =>
                string.Equals(n.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase));
            if (note != null) SelectNote(note);
        }

        // ── Delete ─────────────────────────────────────────────────
        [RelayCommand]
        public async Task DeleteNode(FolderNode? node)
        {
            if (node == null) return;
            if (!node.IsDirectory)
            {
                var note = Notes.FirstOrDefault(n =>
                    string.Equals(n.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase));
                if (note != null)
                    await DeleteNoteAsync(note);
                else
                {
                    await _trashBinService.MoveToTrashAsync(node.FullPath);
                    await RefreshFolderTreeAsync();
                }
            }
            else
            {
                try
                {
                    await _folderService.DeleteFolderAsync(node.FullPath);
                    await RefreshFolderTreeAsync();
                    StatusText = "Folder deleted";
                }
                catch (Exception ex)
                {
                    StatusText = $"Delete folder failed: {ex.Message}";
                }
            }
        }

        /// <summary>Deletes every node currently in SelectedFolderNodes.</summary>
        public async Task BulkDeleteNodesAsync()
        {
            var nodes = SelectedFolderNodes.ToList();
            if (nodes.Count == 0) return;

            StatusText = $"Deleting {nodes.Count} item(s)...";
            foreach (var node in nodes)
                await DeleteNode(node);

            SelectedFolderNodes.Clear();
            SelectedFolderNode = null;
            StatusText = $"Deleted {nodes.Count} item(s)";
        }

        // ── Move ───────────────────────────────────────────────────
        public async Task MoveItemAsync(string sourcePath, string targetDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                string.IsNullOrWhiteSpace(targetDirectoryPath)) return;
            if (!Directory.Exists(targetDirectoryPath)) return;

            try
            {
                StatusText = "Moving item...";
                await _folderService.MoveItemAsync(sourcePath, targetDirectoryPath);
                await LoadAllAsync();
                StatusText = "Item moved";
            }
            catch (Exception ex)
            {
                StatusText = $"Move failed: {ex.Message}";
            }
        }

        /// <summary>Moves every node in SelectedFolderNodes to targetDirectory.</summary>
        public async Task BulkMoveNodesAsync(string targetDirectory)
        {
            var nodes = SelectedFolderNodes.ToList();
            if (nodes.Count == 0 || string.IsNullOrWhiteSpace(targetDirectory)) return;
            if (!Directory.Exists(targetDirectory)) return;

            StatusText = $"Moving {nodes.Count} item(s)...";
            foreach (var node in nodes)
                await MoveItemAsync(node.FullPath, targetDirectory);

            SelectedFolderNodes.Clear();
            SelectedFolderNode = null;
            StatusText = $"Moved {nodes.Count} item(s)";
        }

        // ── Rename ─────────────────────────────────────────────────
        public async Task RenameItemAsync(FolderNode node, string newName)
        {
            if (node == null || string.IsNullOrWhiteSpace(newName)) return;
            try
            {
                string trimmed = newName.Trim();
                if (!node.IsDirectory)
                {
                    var note = Notes.FirstOrDefault(n =>
                        string.Equals(n.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase));
                    if (note != null)
                    {
                        note.Title = Path.GetFileNameWithoutExtension(trimmed);
                        RenameNoteFileToMatchTitle(note, trimmed);
                        if (CurrentNote == note) NoteTitle = note.Title;
                        await _noteRepository.UpdateNoteAsync(note);
                    }
                    else
                    {
                        await _folderService.RenameItemAsync(node.FullPath, trimmed);
                    }
                }
                else
                {
                    var oldPath = node.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var parentDir = Path.GetDirectoryName(oldPath) ?? _notesRoot;
                    var newPath = Path.Combine(parentDir, trimmed);

                    if (!string.Equals(oldPath, newPath, StringComparison.Ordinal))
                    {
                        await _folderService.RenameFolderAsync(oldPath, trimmed);

                        node.Name = trimmed;
                        node.Title = trimmed;
                        node.FullPath = newPath;
                        node.RelativePath = Path.GetRelativePath(_notesRoot, newPath);

                        UpdateDescendantPaths(node, oldPath, newPath);
                        UpdateNotesInRenamedFolder(oldPath, newPath);
                        UpdateTabsInRenamedFolder(oldPath, newPath);
                        UpdateExpandedKeysForRenamedFolder(oldPath, newPath);
                    }
                }

                await RefreshFolderTreeAsync();
                StatusText = $"Renamed to '{trimmed}'";
            }
            catch (Exception ex)
            {
                StatusText = $"Rename failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[RenameItemAsync error]: {ex}");
            }
        }

        private void UpdateDescendantPaths(FolderNode parent, string oldPrefix, string newPrefix)
        {
            var oldSlash = oldPrefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var newSlash = newPrefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (var child in parent.Children)
            {
                if (child.FullPath.StartsWith(oldSlash, StringComparison.OrdinalIgnoreCase))
                {
                    var relSuffix = child.FullPath.Substring(oldSlash.Length);
                    child.FullPath = Path.Combine(newPrefix, relSuffix);
                    child.RelativePath = Path.GetRelativePath(_notesRoot, child.FullPath);
                }

                if (child.IsDirectory && child.Children.Count > 0)
                {
                    UpdateDescendantPaths(child, oldPrefix, newPrefix);
                }
            }
        }

        private void UpdateNotesInRenamedFolder(string oldFolder, string newFolder)
        {
            var oldFolderPrefix = oldFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var newFolderPrefix = newFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (var note in Notes)
            {
                if (note.FullPath.StartsWith(oldFolderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = note.FullPath.Substring(oldFolderPrefix.Length);
                    note.FullPath = newFolderPrefix + suffix;
                    note.FolderPath = Path.GetDirectoryName(note.FullPath) ?? newFolder;
                    note.RelativePath = Path.GetRelativePath(_notesRoot, note.FullPath);
                }
            }
        }

        private void UpdateTabsInRenamedFolder(string oldFolder, string newFolder)
        {
            var oldFolderPrefix = oldFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var newFolderPrefix = newFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (var tab in OpenTabs)
            {
                if (tab.FullPath.StartsWith(oldFolderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = tab.FullPath.Substring(oldFolderPrefix.Length);
                    tab.Note.FullPath = newFolderPrefix + suffix;
                    tab.Note.FolderPath = Path.GetDirectoryName(tab.Note.FullPath) ?? newFolder;
                    tab.Note.RelativePath = Path.GetRelativePath(_notesRoot, tab.Note.FullPath);
                    tab.NotifyPathChanged();
                }
            }
        }

        private void UpdateExpandedKeysForRenamedFolder(string oldFolder, string newFolder)
        {
            if (_settingsService?.Settings?.FolderExpandedStates == null) return;

            var oldRel = Path.GetRelativePath(_notesRoot, oldFolder).Replace('\\', '/').Trim('/');
            var newRel = Path.GetRelativePath(_notesRoot, newFolder).Replace('\\', '/').Trim('/');

            var dict = _settingsService.Settings.FolderExpandedStates;
            var keysToUpdate = dict.Keys
                .Where(k => string.Equals(k, oldRel, StringComparison.OrdinalIgnoreCase) ||
                            k.StartsWith(oldRel + "/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var k in keysToUpdate)
            {
                var val = dict[k];
                dict.Remove(k);
                string newKey = string.Equals(k, oldRel, StringComparison.OrdinalIgnoreCase)
                    ? newRel
                    : newRel + k.Substring(oldRel.Length);
                dict[newKey] = val;
            }

            DebounceSaveSettings();
        }

        // ── Cut / Paste ──────────────────────────────────────────────
        public void CutItem(FolderNode node)
        {
            if (node == null) return;
            ClipboardCutNode = node;
            OnPropertyChanged(nameof(HasClipboardCutNode));
            StatusText = $"Cut '{node.Name}'. Right-click a folder to paste.";
        }

        public async Task PasteItemAsync(FolderNode? targetNode)
        {
            if (ClipboardCutNode == null) return;

            string targetDir = _notesRoot;
            if (targetNode != null)
            {
                targetDir = targetNode.IsDirectory
                    ? targetNode.FullPath
                    : Path.GetDirectoryName(targetNode.FullPath) ?? _notesRoot;
            }

            var itemToMove  = ClipboardCutNode;
            ClipboardCutNode = null;
            OnPropertyChanged(nameof(HasClipboardCutNode));
            await MoveItemAsync(itemToMove.FullPath, targetDir);
        }
    }
}
