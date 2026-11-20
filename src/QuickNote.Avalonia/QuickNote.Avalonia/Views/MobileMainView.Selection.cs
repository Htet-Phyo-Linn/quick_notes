using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.Views
{
    public partial class MobileMainView
    {
        // ── Folder Multi-Select Mode in Left Drawer ───────────────────────
        public MobileSelectionViewModel SelectionViewModel { get; } = new();

        private bool _isFolderSelectionMode => SelectionViewModel.IsFolderSelectionMode;
        private HashSet<string> _selectedFolders => SelectionViewModel.SelectedFolders;

        private void EnterFolderSelectionModeWithFolder(string folder)
        {
            SelectionViewModel.EnterFolderSelection(folder);
            UpdateFolderSelectionState();
            RebuildDrawerFolders();
        }

        private void ExitFolderSelectionMode()
        {
            SelectionViewModel.ExitFolderSelection();
            UpdateFolderSelectionState();
            RebuildDrawerFolders();
        }

        private void UpdateFolderSelectionState()
        {
            if (DrawerNormalHeader != null) DrawerNormalHeader.IsVisible = !_isFolderSelectionMode;
            if (DrawerFolderSelectionHeader != null) DrawerFolderSelectionHeader.IsVisible = _isFolderSelectionMode;
            if (DrawerNormalFooter != null) DrawerNormalFooter.IsVisible = !_isFolderSelectionMode;
            if (DrawerFolderSelectionFooter != null) DrawerFolderSelectionFooter.IsVisible = _isFolderSelectionMode;

            if (DrawerFolderSelectionCountText != null)
            {
                DrawerFolderSelectionCountText.Text = $"{_selectedFolders.Count} selected";
            }

            if (DrawerFolderSelectAllBtnText != null)
            {
                var all = DiscoverAllFolderPaths();
                bool allSelected = all.Count > 0 && all.All(f => _selectedFolders.Contains(f));
                DrawerFolderSelectAllBtnText.Text = allSelected ? "None" : "All";
            }
        }

        private void OnCancelFolderSelectionClick(object? sender, RoutedEventArgs e)
        {
            ExitFolderSelectionMode();
        }

        private void OnToggleSelectAllFoldersClick(object? sender, RoutedEventArgs e)
        {
            var all = DiscoverAllFolderPaths();
            SelectionViewModel.ToggleSelectAllFolders(all);
            UpdateFolderSelectionState();
            RebuildDrawerFolders();
        }

        private void OnBatchMoveFoldersClick(object? sender, RoutedEventArgs e)
        {
            if (_selectedFolders.Count == 0) return;
            if (MoveFoldersModalTitle != null)
            {
                MoveFoldersModalTitle.Text = $"Move {_selectedFolders.Count} Folder(s)";
            }
            if (MoveFoldersModal != null)
            {
                MoveFoldersModal.IsVisible = true;
                PopulateMoveFoldersDestinations(null);
            }
        }

        private void OnCancelMoveFoldersClick(object? sender, RoutedEventArgs e)
        {
            if (MoveFoldersModal != null) MoveFoldersModal.IsVisible = false;
        }

        private void OnMoveFoldersSearchKeyUp(object? sender, KeyEventArgs e)
        {
            PopulateMoveFoldersDestinations(MoveFoldersSearchBox?.Text);
        }

        private void PopulateMoveFoldersDestinations(string? filter)
        {
            if (MoveFoldersDestinationsPanel == null) return;
            MoveFoldersDestinationsPanel.Children.Clear();

            var cleanFilter = filter?.Trim() ?? string.Empty;

            // 1. Root option
            if (string.IsNullOrEmpty(cleanFilter) || "root top level".Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
            {
                var rootBtn = CreateMoveFolderDestinationButton(null, "/ (Root - Top level)");
                MoveFoldersDestinationsPanel.Children.Add(rootBtn);
            }

            // 2. Discover available destination folders
            var allFolders = DiscoverAllFolderPaths();
            foreach (var folder in allFolders)
            {
                // Disallow moving a folder into itself or its own descendants
                bool isInvalidDest = _selectedFolders.Any(sf =>
                    string.Equals(sf, folder, StringComparison.OrdinalIgnoreCase) ||
                    folder.StartsWith(sf + "/", StringComparison.OrdinalIgnoreCase));

                if (!isInvalidDest)
                {
                    if (string.IsNullOrEmpty(cleanFilter) || folder.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        var btn = CreateMoveFolderDestinationButton(folder, folder);
                        MoveFoldersDestinationsPanel.Children.Add(btn);
                    }
                }
            }
        }

        private Button CreateMoveFolderDestinationButton(string? targetRelPath, string displayTitle)
        {
            var btn = new Button
            {
                Background = _isDarkMode ? new SolidColorBrush(Color.Parse("#181820")) : new SolidColorBrush(Color.Parse("#f1f5f9")),
                BorderBrush = GlassBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var icon = new PathIcon
            {
                Data = (StreamGeometry)Resources["IconFolder"]!,
                Width = 16,
                Height = 16,
                Foreground = new SolidColorBrush(Color.Parse("#3b82f6")),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var text = new TextBlock
            {
                Text = displayTitle,
                FontSize = 13,
                Foreground = TextPrimaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(text, 1);

            grid.Children.Add(icon);
            grid.Children.Add(text);
            btn.Content = grid;

            btn.Click += async (s, e) =>
            {
                try
                {
                    var root = _viewModel?.NotesRoot;
                    if (!string.IsNullOrEmpty(root))
                    {
                        foreach (var selFolder in _selectedFolders.ToList())
                        {
                            var srcDir = Path.Combine(root, selFolder.Replace('/', Path.DirectorySeparatorChar));
                            var folderName = Path.GetFileName(srcDir);
                            var destParent = string.IsNullOrEmpty(targetRelPath)
                                ? root
                                : Path.Combine(root, targetRelPath.Replace('/', Path.DirectorySeparatorChar));
                            var destDir = Path.Combine(destParent, folderName);

                            if (Directory.Exists(srcDir) && !string.Equals(srcDir, destDir, StringComparison.OrdinalIgnoreCase))
                            {
                                if (!Directory.Exists(destParent))
                                {
                                    Directory.CreateDirectory(destParent);
                                }
                                if (!Directory.Exists(destDir))
                                {
                                    Directory.Move(srcDir, destDir);
                                }
                            }
                        }

                        if (MoveFoldersModal != null) MoveFoldersModal.IsVisible = false;
                        ExitFolderSelectionMode();

                        if (_viewModel != null)
                        {
                            await _viewModel.RefreshFolderTreeAsync();
                            await _viewModel.Refresh();
                        }

                        RebuildDrawerFolders();
                        ApplyFilters();
                        TriggerAutoSyncInBackground();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MobileMainView] Error moving folders: {ex.Message}");
                    if (MoveFoldersModal != null) MoveFoldersModal.IsVisible = false;
                }
            };

            return btn;
        }

        private void OnBatchDeleteFoldersClick(object? sender, RoutedEventArgs e)
        {
            if (_selectedFolders.Count == 0) return;
            if (ConfirmBatchDeleteFoldersMessageText != null)
            {
                ConfirmBatchDeleteFoldersMessageText.Text = $"Delete {_selectedFolders.Count} selected folder(s) and move their notes to the Trash Bin?";
            }
            if (ConfirmBatchDeleteFoldersModal != null)
            {
                ConfirmBatchDeleteFoldersModal.IsVisible = true;
            }
        }

        private void OnCancelBatchDeleteFoldersClick(object? sender, RoutedEventArgs e)
        {
            if (ConfirmBatchDeleteFoldersModal != null)
            {
                ConfirmBatchDeleteFoldersModal.IsVisible = false;
            }
        }

        private async void OnConfirmBatchDeleteFoldersClick(object? sender, RoutedEventArgs e)
        {
            if (ConfirmBatchDeleteFoldersModal != null) ConfirmBatchDeleteFoldersModal.IsVisible = false;
            var selectedFoldersList = _selectedFolders.ToList();
            ExitFolderSelectionMode();

            try
            {
                var root = _viewModel?.NotesRoot;
                if (!string.IsNullOrEmpty(root) && _viewModel != null)
                {
                    foreach (var folder in selectedFoldersList)
                    {
                        var folderPath = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
                        if (Directory.Exists(folderPath))
                        {
                            await _viewModel.TrashBinService.MoveToTrashAsync(folderPath);
                        }
                    }

                    _selectedCategory = "All Notes";
                    await _viewModel.RefreshFolderTreeAsync();
                    await _viewModel.Refresh();
                    RebuildTagChips();
                    if (LeftDrawerOverlay?.IsVisible == true)
                    {
                        RebuildDrawerFolders();
                        RebuildDrawerTags();
                        UpdateTrashBadge();
                    }
                    ApplyFilters();
                    OnPropertyChanged(nameof(TotalNotesCountText));
                    TriggerAutoSyncInBackground();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error in batch delete folders: {ex.Message}");
            }
        }

        // ── Note Long-Press & Multi-Select Handlers ───────────────────────
        private DispatcherTimer? _noteHoldTimer;
        private bool _wasNoteLongPress = false;
        private Point _notePressStartPoint;
        private object? _pendingHoldItem;
        private const double NoteHoldTouchSlop = 60.0;

        private void StartNoteHoldTimer(Point startPoint, object? item)
        {
            if (IsSelectionMode) return;
            _wasNoteLongPress = false;
            _noteHoldTimer?.Stop();
            _noteHoldTimer = null;
            _notePressStartPoint = startPoint;
            _pendingHoldItem = item;

            _noteHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
            _noteHoldTimer.Tick += (s, ev) =>
            {
                _noteHoldTimer?.Stop();
                _noteHoldTimer = null;
                _wasNoteLongPress = true;

                if (_pendingHoldItem is MobileNoteItem noteItem)
                {
                    EnterSelectionModeWithNote(noteItem);
                }
                else if (_pendingHoldItem is FolderNode node)
                {
                    EnterSelectionModeWithFolderNode(node);
                }
            };
            _noteHoldTimer.Start();
        }

        private void CheckNoteHoldMovement(Point currentPoint)
        {
            if (_noteHoldTimer != null)
            {
                double dx = Math.Abs(currentPoint.X - _notePressStartPoint.X);
                double dy = Math.Abs(currentPoint.Y - _notePressStartPoint.Y);
                if (dx > NoteHoldTouchSlop || dy > NoteHoldTouchSlop)
                {
                    _noteHoldTimer.Stop();
                    _noteHoldTimer = null;
                    _pendingHoldItem = null;
                }
            }
        }

        private void StopNoteHoldTimer()
        {
            _noteHoldTimer?.Stop();
            _noteHoldTimer = null;
            _pendingHoldItem = null;
        }

        private void OnNoteCardPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if ((sender as Control)?.DataContext is MobileNoteItem item)
            {
                StartNoteHoldTimer(e.GetPosition(this), item);
            }
        }

        private void OnNoteCardPointerMoved(object? sender, PointerEventArgs e)
        {
            CheckNoteHoldMovement(e.GetPosition(this));
        }

        private void OnNoteCardPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            StopNoteHoldTimer();
        }

        private void OnNoteCardPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            // Do NOT stop hold timer on spurious capture loss from parent scroll viewers
        }

        private void OnFolderNodePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if ((sender as Control)?.DataContext is FolderNode node)
            {
                StartNoteHoldTimer(e.GetPosition(this), node);
            }
        }

        private void OnFolderNodePointerMoved(object? sender, PointerEventArgs e)
        {
            CheckNoteHoldMovement(e.GetPosition(this));
        }

        private void OnFolderNodePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            StopNoteHoldTimer();
        }

        private void OnFolderNodePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            // Do NOT stop hold timer on spurious capture loss from parent TreeView/ScrollViewer
        }

        private void OnFlatFilePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if ((sender as Control)?.DataContext is MobileNoteItem item)
            {
                StartNoteHoldTimer(e.GetPosition(this), item);
            }
        }

        private void OnFlatFilePointerMoved(object? sender, PointerEventArgs e)
        {
            CheckNoteHoldMovement(e.GetPosition(this));
        }

        private void OnFlatFilePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            StopNoteHoldTimer();
        }

        private void OnFlatFilePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            // Do NOT stop hold timer on spurious capture loss from parent ScrollViewer
        }

        private void EnterSelectionModeWithNote(MobileNoteItem item)
        {
            IsSelectionMode = true;
            item.IsSelected = true;
            UpdateSelectionState();
        }

        private void EnterSelectionModeWithFolderNode(FolderNode node)
        {
            IsSelectionMode = true;
            node.IsSelected = true;
            if (!node.IsDirectory)
            {
                var match = FilteredNotes.FirstOrDefault(n =>
                    (!string.IsNullOrEmpty(node.FullPath) && string.Equals(n.Note?.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(n.Title, node.DisplayName, StringComparison.OrdinalIgnoreCase));
                if (match != null) match.IsSelected = true;
            }
            UpdateSelectionState();
        }

        private void ExitSelectionMode()
        {
            IsSelectionMode = false;
            foreach (var n in FilteredNotes)
            {
                n.IsSelected = false;
            }
            UnselectAllFolderNodes(FolderNodes);
            UpdateSelectionState();
        }

        private void UnselectAllFolderNodes(IEnumerable<FolderNode> nodes)
        {
            foreach (var node in nodes)
            {
                node.IsSelected = false;
                if (node.Children?.Count > 0)
                {
                    UnselectAllFolderNodes(node.Children);
                }
            }
        }

        private void UpdateSelectionState()
        {
            OnPropertyChanged(nameof(SelectionCountText));
            OnPropertyChanged(nameof(SelectAllButtonText));
        }

        private void OnCancelSelectionClick(object? sender, RoutedEventArgs e)
        {
            ExitSelectionMode();
        }

        private void OnToggleSelectAllNotesClick(object? sender, RoutedEventArgs e)
        {
            if (IsFolderTreeVisible)
            {
                var all = GetAllFolderNodesFlat(FolderNodes);
                bool allSelected = all.Count > 0 && all.All(n => n.IsSelected);
                foreach (var n in all)
                {
                    n.IsSelected = !allSelected;
                }
                foreach (var noteItem in FilteredNotes)
                {
                    noteItem.IsSelected = !allSelected;
                }
            }
            else
            {
                bool allSelected = FilteredNotes.Count > 0 && FilteredNotes.All(n => n.IsSelected);
                foreach (var n in FilteredNotes)
                {
                    n.IsSelected = !allSelected;
                }
            }
            UpdateSelectionState();
        }

        private List<FolderNode> GetAllFolderNodesFlat(IEnumerable<FolderNode> nodes)
        {
            var list = new List<FolderNode>();
            foreach (var node in nodes)
            {
                list.Add(node);
                if (node.Children?.Count > 0)
                {
                    list.AddRange(GetAllFolderNodesFlat(node.Children));
                }
            }
            return list;
        }

        private void CollectSelectedPaths(IEnumerable<FolderNode> nodes, HashSet<string> notePaths, HashSet<string> folderPaths)
        {
            foreach (var node in nodes)
            {
                if (node.IsSelected)
                {
                    if (node.IsDirectory && !string.IsNullOrEmpty(node.FullPath))
                    {
                        folderPaths.Add(node.FullPath);
                    }
                    else if (!node.IsDirectory && !string.IsNullOrEmpty(node.FullPath))
                    {
                        notePaths.Add(node.FullPath);
                    }
                }
                if (node.Children?.Count > 0)
                {
                    CollectSelectedPaths(node.Children, notePaths, folderPaths);
                }
            }
        }

        private void OnNoteCardTapped(object? sender, TappedEventArgs e)
        {
            if (_wasNoteLongPress)
            {
                _wasNoteLongPress = false;
                return;
            }

            if ((sender as Control)?.DataContext is MobileNoteItem item)
            {
                if (IsSelectionMode)
                {
                    item.IsSelected = !item.IsSelected;
                    UpdateSelectionState();
                    return;
                }

                if (_viewModel != null)
                {
                    _viewModel.SelectNote(item.Note);
                    ActiveNoteTitle = item.Note.Title;
                    ShowNoteEditor();
                }
            }
        }

        // ── Batch Note Operations (Move, Copy, Delete) ─────────────────────
        private void OnBatchMoveNotesClick(object? sender, RoutedEventArgs e)
        {
            int totalSelected = GetTotalSelectedCount();
            if (totalSelected == 0) return;

            if (MoveNotesModalTitle != null)
            {
                MoveNotesModalTitle.Text = $"Move {totalSelected} Item(s)";
            }
            if (MoveNotesModal != null)
            {
                MoveNotesModal.IsVisible = true;
                PopulateMoveNotesDestinations(null);
            }
        }

        private void OnCancelMoveNotesClick(object? sender, RoutedEventArgs e)
        {
            if (MoveNotesModal != null) MoveNotesModal.IsVisible = false;
            if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = true;
            }
        }

        private void OnMoveNotesSearchKeyUp(object? sender, KeyEventArgs e)
        {
            PopulateMoveNotesDestinations(MoveNotesSearchBox?.Text);
        }

        private void PopulateMoveNotesDestinations(string? filter)
        {
            if (MoveNotesDestinationsPanel == null) return;
            MoveNotesDestinationsPanel.Children.Clear();

            var cleanFilter = filter?.Trim() ?? string.Empty;

            // 1. Root option
            if (string.IsNullOrEmpty(cleanFilter) || "root top level".Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
            {
                var rootBtn = CreateMoveNoteDestinationButton(null, "/ (Root - Top level)");
                MoveNotesDestinationsPanel.Children.Add(rootBtn);
            }

            // 2. Discovered folders
            var allFolders = DiscoverAllFolderPaths();
            foreach (var folder in allFolders)
            {
                if (string.IsNullOrEmpty(cleanFilter) || folder.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
                {
                    var btn = CreateMoveNoteDestinationButton(folder, folder);
                    MoveNotesDestinationsPanel.Children.Add(btn);
                }
            }
        }

        private Button CreateMoveNoteDestinationButton(string? targetRelPath, string displayTitle)
        {
            var btn = new Button
            {
                Background = _isDarkMode ? new SolidColorBrush(Color.Parse("#181820")) : new SolidColorBrush(Color.Parse("#f1f5f9")),
                BorderBrush = GlassBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var icon = new PathIcon
            {
                Data = (StreamGeometry)Resources["IconFolder"]!,
                Width = 16,
                Height = 16,
                Foreground = new SolidColorBrush(Color.Parse("#3b82f6")),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var text = new TextBlock
            {
                Text = displayTitle,
                FontSize = 13,
                Foreground = TextPrimaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(text, 1);

            grid.Children.Add(icon);
            grid.Children.Add(text);
            btn.Content = grid;

            btn.Click += async (s, e) =>
            {
                try
                {
                    var root = _viewModel?.NotesRoot;
                    if (!string.IsNullOrEmpty(root) && _viewModel != null)
                    {
                        var destDir = string.IsNullOrEmpty(targetRelPath)
                            ? root
                            : Path.Combine(root, targetRelPath.Replace('/', Path.DirectorySeparatorChar));

                        if (!Directory.Exists(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }

                        var selectedNotePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var selectedFolderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var item in FilteredNotes.Where(n => n.IsSelected))
                        {
                            if (!string.IsNullOrEmpty(item.Note?.FullPath)) selectedNotePaths.Add(item.Note.FullPath);
                        }
                        CollectSelectedPaths(FolderNodes, selectedNotePaths, selectedFolderPaths);

                        foreach (var srcFile in selectedNotePaths)
                        {
                            if (File.Exists(srcFile))
                            {
                                var fileName = Path.GetFileName(srcFile);
                                var targetFile = Path.Combine(destDir, fileName);

                                if (!string.Equals(srcFile, targetFile, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (File.Exists(targetFile))
                                    {
                                        var baseName = Path.GetFileNameWithoutExtension(fileName);
                                        var ext = Path.GetExtension(fileName);
                                        int c = 1;
                                        while (File.Exists(Path.Combine(destDir, $"{baseName} ({c}){ext}"))) c++;
                                        targetFile = Path.Combine(destDir, $"{baseName} ({c}){ext}");
                                    }
                                    var oldRel = Path.GetRelativePath(root, srcFile);
                                    var newRel = Path.GetRelativePath(root, targetFile);
                                    File.Move(srcFile, targetFile);
                                    _viewModel.CloudSyncService?.RecordFileMoved(oldRel, newRel);

                                    var matchNote = FilteredNotes.FirstOrDefault(n => n.Note?.FullPath == srcFile);
                                    if (matchNote != null)
                                    {
                                        matchNote.Note.FullPath = targetFile;
                                        matchNote.Note.FolderPath = destDir;
                                        matchNote.Note.RelativePath = newRel;
                                        matchNote.Note.FileName = Path.GetFileName(targetFile);
                                    }

                                    if (_viewModel.CurrentNote != null && string.Equals(_viewModel.CurrentNote.FullPath, srcFile, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _viewModel.CurrentNote.FullPath = targetFile;
                                        _viewModel.CurrentNote.FolderPath = destDir;
                                        _viewModel.CurrentNote.RelativePath = newRel;
                                        _viewModel.CurrentNote.FileName = Path.GetFileName(targetFile);
                                        if (MobileNotionEditor != null)
                                        {
                                            MobileNotionEditor.CurrentNotePath = targetFile;
                                            MobileNotionEditor.CurrentNoteFolderPath = destDir;
                                        }
                                    }
                                }
                            }
                        }

                        foreach (var srcDir in selectedFolderPaths)
                        {
                            if (Directory.Exists(srcDir))
                            {
                                var folderName = Path.GetFileName(srcDir);
                                var targetDir = Path.Combine(destDir, folderName);
                                if (!string.Equals(srcDir, targetDir, StringComparison.OrdinalIgnoreCase) && !targetDir.StartsWith(srcDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!Directory.Exists(targetDir))
                                    {
                                        Directory.Move(srcDir, targetDir);
                                    }
                                }
                            }
                        }

                        if (MoveNotesModal != null) MoveNotesModal.IsVisible = false;
                        if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
                        {
                            MobileNotionEditor.IsVisible = true;
                        }
                        ExitSelectionMode();

                        await _viewModel.RefreshFolderTreeAsync();
                        await _viewModel.LoadAllAsync();
                        RebuildDrawerFolders();
                        ApplyFilters();
                        TriggerAutoSyncInBackground();
                        int totalMoved = selectedNotePaths.Count + selectedFolderPaths.Count;
                        ShowToast($"Moved {totalMoved} item(s)", isProgress: false, isError: false);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MobileMainView] Error moving notes: {ex.Message}");
                    if (MoveNotesModal != null) MoveNotesModal.IsVisible = false;
                    if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
                    {
                        MobileNotionEditor.IsVisible = true;
                    }
                }
            };

            return btn;
        }

        private async void OnBatchCopyNotesClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_viewModel == null) return;
                var selectedNotePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dummyFolders = new HashSet<string>();
                foreach (var item in FilteredNotes.Where(n => n.IsSelected))
                {
                    if (!string.IsNullOrEmpty(item.Note?.FullPath)) selectedNotePaths.Add(item.Note.FullPath);
                }
                CollectSelectedPaths(FolderNodes, selectedNotePaths, dummyFolders);

                if (selectedNotePaths.Count == 0) return;

                var root = _viewModel.NotesRoot;
                foreach (var src in selectedNotePaths)
                {
                    if (File.Exists(src))
                    {
                        var dir = Path.GetDirectoryName(src) ?? root;
                        var baseName = Path.GetFileNameWithoutExtension(src);
                        var ext = Path.GetExtension(src);

                        string copyPath = Path.Combine(dir, $"{baseName} (Copy){ext}");
                        int c = 2;
                        while (File.Exists(copyPath))
                        {
                            copyPath = Path.Combine(dir, $"{baseName} (Copy {c}){ext}");
                            c++;
                        }
                        File.Copy(src, copyPath);
                    }
                }

                ExitSelectionMode();
                await _viewModel.RefreshFolderTreeAsync();
                await _viewModel.Refresh();
                RebuildDrawerFolders();
                ApplyFilters();
                TriggerAutoSyncInBackground();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error copying notes: {ex.Message}");
            }
        }

        private void OnBatchDeleteNotesClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_viewModel == null) return;

                var selectedNotePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var selectedFolderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in FilteredNotes.Where(n => n.IsSelected))
                {
                    if (!string.IsNullOrEmpty(item.Note?.FullPath))
                    {
                        selectedNotePaths.Add(item.Note.FullPath);
                    }
                }

                CollectSelectedPaths(FolderNodes, selectedNotePaths, selectedFolderPaths);

                if (selectedNotePaths.Count == 0 && selectedFolderPaths.Count == 0) return;

                // Optimistic UI update: Remove matching items from FilteredNotes immediately
                var itemsToRemove = FilteredNotes.Where(n => n.Note?.FullPath != null && selectedNotePaths.Contains(n.Note.FullPath)).ToList();
                foreach (var item in itemsToRemove)
                {
                    FilteredNotes.Remove(item);
                    _viewModel.Notes.Remove(item.Note);
                }

                ExitSelectionMode();
                OnPropertyChanged(nameof(NoteCountStatus));
                OnPropertyChanged(nameof(IsNotesEmpty));
                OnPropertyChanged(nameof(TotalNotesCountText));

                var pathsToDelete = selectedNotePaths.ToList();
                var foldersToDelete = selectedFolderPaths.ToList();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var path in pathsToDelete)
                        {
                            if (File.Exists(path))
                            {
                                await _viewModel.TrashBinService.MoveToTrashAsync(path);
                            }
                        }
                        foreach (var folder in foldersToDelete)
                        {
                            if (Directory.Exists(folder))
                            {
                                await _viewModel.TrashBinService.MoveToTrashAsync(folder);
                            }
                        }
                        await _viewModel.RefreshFolderTreeAsync();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ApplyFilters();
                            OnPropertyChanged(nameof(TotalNotesCountText));
                            if (LeftDrawerOverlay?.IsVisible == true)
                            {
                                RebuildDrawerFolders();
                                RebuildDrawerTags();
                                UpdateTrashBadge();
                            }
                            TriggerAutoSyncInBackground();
                            int totalDel = pathsToDelete.Count + foldersToDelete.Count;
                            ShowToast($"Moved {totalDel} item(s) to trash", isProgress: false, isError: false);
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MobileMainView] Error batch deleting notes in background: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error batch deleting notes: {ex.Message}");
            }
        }
    }
}
