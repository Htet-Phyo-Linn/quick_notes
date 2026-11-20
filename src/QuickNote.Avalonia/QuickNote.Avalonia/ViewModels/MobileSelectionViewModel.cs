using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.ViewModels
{
    /// <summary>
    /// Encapsulates multi-selection state and batch business logic for mobile
    /// notes and folders, decoupling presentation from code-behind.
    /// </summary>
    public partial class MobileSelectionViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FolderSelectionCountText))]
        [NotifyPropertyChangedFor(nameof(SelectedFolderCount))]
        private bool _isFolderSelectionMode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NoteSelectionCountText))]
        [NotifyPropertyChangedFor(nameof(SelectedNoteCount))]
        private bool _isNoteSelectionMode;

        public HashSet<string> SelectedFolders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedNotePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int SelectedFolderCount => SelectedFolders.Count;
        public int SelectedNoteCount => SelectedNotePaths.Count;

        public string FolderSelectionCountText => $"{SelectedFolderCount} selected";
        public string NoteSelectionCountText => $"{SelectedNoteCount} selected";

        public event Action? SelectionChanged;

        // ── Folder Selection ──────────────────────────────────────────

        public void EnterFolderSelection(string initialFolder)
        {
            IsFolderSelectionMode = true;
            SelectedFolders.Clear();
            if (!string.IsNullOrWhiteSpace(initialFolder))
            {
                SelectedFolders.Add(initialFolder);
            }
            NotifyState();
        }

        public void ExitFolderSelection()
        {
            IsFolderSelectionMode = false;
            SelectedFolders.Clear();
            NotifyState();
        }

        public void ToggleFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return;
            if (SelectedFolders.Contains(folderPath))
            {
                SelectedFolders.Remove(folderPath);
            }
            else
            {
                SelectedFolders.Add(folderPath);
            }
            NotifyState();
        }

        public void ToggleSelectAllFolders(IEnumerable<string> allFolders)
        {
            var folderList = allFolders.ToList();
            bool allSelected = folderList.Count > 0 && folderList.All(f => SelectedFolders.Contains(f));

            if (allSelected)
            {
                SelectedFolders.Clear();
            }
            else
            {
                foreach (var f in folderList)
                {
                    SelectedFolders.Add(f);
                }
            }
            NotifyState();
        }

        public bool IsFolderSelected(string folderPath)
        {
            return !string.IsNullOrWhiteSpace(folderPath) && SelectedFolders.Contains(folderPath);
        }

        // ── Note Selection ────────────────────────────────────────────

        public void EnterNoteSelection(string initialNotePath)
        {
            IsNoteSelectionMode = true;
            SelectedNotePaths.Clear();
            if (!string.IsNullOrWhiteSpace(initialNotePath))
            {
                SelectedNotePaths.Add(initialNotePath);
            }
            NotifyState();
        }

        public void ExitNoteSelection()
        {
            IsNoteSelectionMode = false;
            SelectedNotePaths.Clear();
            NotifyState();
        }

        public void ToggleNote(string notePath)
        {
            if (string.IsNullOrWhiteSpace(notePath)) return;
            if (SelectedNotePaths.Contains(notePath))
            {
                SelectedNotePaths.Remove(notePath);
            }
            else
            {
                SelectedNotePaths.Add(notePath);
            }
            NotifyState();
        }

        public void ToggleSelectAllNotes(IEnumerable<string> allNotePaths)
        {
            var noteList = allNotePaths.ToList();
            bool allSelected = noteList.Count > 0 && noteList.All(p => SelectedNotePaths.Contains(p));

            if (allSelected)
            {
                SelectedNotePaths.Clear();
            }
            else
            {
                foreach (var p in noteList)
                {
                    SelectedNotePaths.Add(p);
                }
            }
            NotifyState();
        }

        public bool IsNoteSelected(string notePath)
        {
            return !string.IsNullOrWhiteSpace(notePath) && SelectedNotePaths.Contains(notePath);
        }

        // ── Batch Operations ──────────────────────────────────────────

        public async Task<int> BatchDeleteSelectedNotesAsync(INoteRepository noteRepo, ITrashBinService trashService)
        {
            if (noteRepo == null || trashService == null) return 0;
            var pathsToDelete = SelectedNotePaths.ToList();
            int count = 0;

            foreach (var path in pathsToDelete)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        await trashService.MoveToTrashAsync(path);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MobileSelectionViewModel] Batch delete failed for '{path}': {ex.Message}");
                }
            }

            ExitNoteSelection();
            return count;
        }

        public async Task<int> BatchMoveSelectedNotesAsync(string targetDir, INoteRepository noteRepo)
        {
            if (string.IsNullOrWhiteSpace(targetDir) || noteRepo == null) return 0;
            var pathsToMove = SelectedNotePaths.ToList();
            int count = 0;

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            foreach (var path in pathsToMove)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var fileName = Path.GetFileName(path);
                        var targetFullPath = Path.Combine(targetDir, fileName);
                        if (!string.Equals(path, targetFullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            var note = await noteRepo.GetNoteByPathAsync(path);
                            if (note != null)
                            {
                                File.Move(path, targetFullPath, overwrite: true);
                                note.FullPath = targetFullPath;
                                note.FolderPath = targetDir;
                                note.RelativePath = Path.GetRelativePath(noteRepo.NotesRoot, targetFullPath);
                                await noteRepo.SaveNoteFastAsync(note, oldFullPath: path);
                                count++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MobileSelectionViewModel] Batch move failed for '{path}': {ex.Message}");
                }
            }

            ExitNoteSelection();
            return count;
        }

        private void NotifyState()
        {
            OnPropertyChanged(nameof(SelectedFolderCount));
            OnPropertyChanged(nameof(SelectedNoteCount));
            OnPropertyChanged(nameof(FolderSelectionCountText));
            OnPropertyChanged(nameof(NoteSelectionCountText));
            SelectionChanged?.Invoke();
        }
    }
}
