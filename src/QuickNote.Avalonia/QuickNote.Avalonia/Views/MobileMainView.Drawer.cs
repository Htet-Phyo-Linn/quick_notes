using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace QuickNote.Avalonia.Views
{
    public partial class MobileMainView
    {
        // ── Folder Discovery & Navigation ──────────────────────────────
        private HashSet<string> DiscoverAllFolders()
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var root = _viewModel?.NotesRoot;
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    var dirs = Directory.GetDirectories(root);
                    foreach (var dir in dirs)
                    {
                        var name = Path.GetFileName(dir);
                        if (!string.IsNullOrEmpty(name) && !name.StartsWith(".") && !string.Equals(name, "Assets", StringComparison.OrdinalIgnoreCase))
                        {
                            folders.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error discovering disk folders: {ex.Message}");
            }

            if (_viewModel?.Notes != null)
            {
                foreach (var n in _viewModel.Notes)
                {
                    var f = Path.GetFileName(n.FolderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!string.IsNullOrWhiteSpace(f)) folders.Add(f);
                }
            }

            return folders;
        }

        private List<string> DiscoverAllFolderPaths()
        {
            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var root = _viewModel?.NotesRoot;
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    var dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
                    foreach (var dir in dirs)
                    {
                        var rel = Path.GetRelativePath(root, dir).Replace('\\', '/');
                        var parts = rel.Split('/');
                        if (!parts.Any(p => p.StartsWith(".") || string.Equals(p, "Assets", StringComparison.OrdinalIgnoreCase)))
                        {
                            list.Add(rel);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error discovering folder paths: {ex.Message}");
            }

            if (_viewModel?.Notes != null && !string.IsNullOrEmpty(_viewModel.NotesRoot))
            {
                foreach (var note in _viewModel.Notes)
                {
                    if (!string.IsNullOrWhiteSpace(note.FolderPath) && Directory.Exists(note.FolderPath))
                    {
                        var rel = Path.GetRelativePath(_viewModel.NotesRoot, note.FolderPath).Replace('\\', '/');
                        if (!string.IsNullOrEmpty(rel) && rel != ".")
                        {
                            list.Add(rel);
                        }
                    }
                }
            }

            return list.OrderBy(f => f).ToList();
        }

        // ── New Folder Dialog Handlers ────────────────────────────────────
        private string? _selectedParentFolderPath = null;

        private void ShowNewFolderDialog()
        {
            _selectedParentFolderPath = null;
            if (SelectedParentFolderLabel != null)
            {
                SelectedParentFolderLabel.Text = "/ (Root - Top level)";
            }
            if (ParentFolderDropdownContainer != null)
            {
                ParentFolderDropdownContainer.IsVisible = false;
            }
            if (NewFolderNameTextBox != null)
            {
                NewFolderNameTextBox.Text = string.Empty;
            }
            if (NewFolderModal != null)
            {
                NewFolderModal.IsVisible = true;
                NewFolderNameTextBox?.Focus();
            }
        }

        private void OnToggleParentFolderDropdownClick(object? sender, RoutedEventArgs e)
        {
            if (ParentFolderDropdownContainer == null) return;
            ParentFolderDropdownContainer.IsVisible = !ParentFolderDropdownContainer.IsVisible;
            if (ParentFolderDropdownContainer.IsVisible)
            {
                PopulateParentFoldersList(ParentFolderSearchBox?.Text);
            }
        }

        private void OnParentFolderSearchKeyUp(object? sender, KeyEventArgs e)
        {
            PopulateParentFoldersList(ParentFolderSearchBox?.Text);
        }

        private void PopulateParentFoldersList(string? filter)
        {
            if (ParentFolderItemsPanel == null) return;
            ParentFolderItemsPanel.Children.Clear();

            var cleanFilter = filter?.Trim() ?? string.Empty;

            // 1. Root option
            if (string.IsNullOrEmpty(cleanFilter) || "root top level".Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
            {
                var rootBtn = CreateParentFolderOptionButton(null, "/ (Root - Top level)");
                ParentFolderItemsPanel.Children.Add(rootBtn);
            }

            // 2. All discovered folders
            var allFolders = DiscoverAllFolderPaths();
            foreach (var f in allFolders)
            {
                if (string.IsNullOrEmpty(cleanFilter) || f.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase))
                {
                    var folderBtn = CreateParentFolderOptionButton(f, f);
                    ParentFolderItemsPanel.Children.Add(folderBtn);
                }
            }
        }

        private Button CreateParentFolderOptionButton(string? folderPath, string displayTitle)
        {
            var isSelected = string.Equals(_selectedParentFolderPath, folderPath, StringComparison.OrdinalIgnoreCase) ||
                             (string.IsNullOrEmpty(_selectedParentFolderPath) && folderPath == null);

            var btn = new Button
            {
                Background = isSelected ? (_isDarkMode ? new SolidColorBrush(Color.Parse("#252d3d")) : new SolidColorBrush(Color.Parse("#e0f2fe"))) : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };

            var icon = new PathIcon
            {
                Data = (StreamGeometry)Resources["IconFolder"]!,
                Width = 14,
                Height = 14,
                Foreground = isSelected ? new SolidColorBrush(Color.Parse("#3b82f6")) : TextSecondaryBrush,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var text = new TextBlock
            {
                Text = displayTitle,
                FontSize = 12,
                FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = isSelected ? AccentBrush : TextPrimaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(text, 1);

            grid.Children.Add(icon);
            grid.Children.Add(text);
            btn.Content = grid;

            btn.Click += (s, e) =>
            {
                _selectedParentFolderPath = folderPath;
                if (SelectedParentFolderLabel != null)
                {
                    SelectedParentFolderLabel.Text = displayTitle;
                }
                if (ParentFolderDropdownContainer != null)
                {
                    ParentFolderDropdownContainer.IsVisible = false;
                }
            };

            return btn;
        }

        private async void OnConfirmNewFolderClick(object? sender, RoutedEventArgs e)
        {
            var folderName = NewFolderNameTextBox?.Text?.Trim();
            if (NewFolderModal != null) NewFolderModal.IsVisible = false;
            if (string.IsNullOrWhiteSpace(folderName)) return;

            // Sanitize folder name
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                folderName = folderName.Replace(c, '_');
            }

            try
            {
                var root = _viewModel?.NotesRoot;
                if (!string.IsNullOrEmpty(root))
                {
                    string targetDir;
                    if (!string.IsNullOrEmpty(_selectedParentFolderPath))
                    {
                        targetDir = Path.Combine(root, _selectedParentFolderPath.Replace('/', Path.DirectorySeparatorChar), folderName);
                    }
                    else
                    {
                        targetDir = Path.Combine(root, folderName);
                    }

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    if (_viewModel != null)
                    {
                        await _viewModel.RefreshFolderTreeAsync();
                    }
                    _selectedCategory = folderName;
                    RebuildTagChips();
                    if (LeftDrawerOverlay?.IsVisible == true)
                    {
                        RebuildDrawerFolders();
                    }
                    ApplyFilters();
                    TriggerAutoSyncInBackground();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Failed creating folder '{folderName}': {ex.Message}");
            }
        }

        private void OnCancelNewFolderClick(object? sender, RoutedEventArgs e)
        {
            if (NewFolderModal != null) NewFolderModal.IsVisible = false;
        }

        // ── Left Sidebar / Navigation Drawer ──────────────────────────────
        private void OnToggleLeftDrawerClick(object? sender, RoutedEventArgs e)
        {
            if (LeftDrawerOverlay == null) return;
            LeftDrawerOverlay.IsVisible = !LeftDrawerOverlay.IsVisible;
            if (LeftDrawerOverlay.IsVisible)
            {
                RebuildDrawerFolders();
                RebuildDrawerTags();
                UpdateTrashBadge();
                OnPropertyChanged(nameof(TotalNotesCountText));
                OnPropertyChanged(nameof(ConnectedAccountEmail));
            }
        }

        private void OnCloseLeftDrawerClick(object? sender, RoutedEventArgs e)
        {
            _drawerFolderHoldTimer?.Stop();
            _drawerFolderHoldTimer = null;
            if (_isFolderSelectionMode)
            {
                ExitFolderSelectionMode();
            }
            if (LeftDrawerOverlay != null)
            {
                LeftDrawerOverlay.IsVisible = false;
            }
        }

        private void OnDrawerAllNotesClick(object? sender, RoutedEventArgs e)
        {
            if (_isFolderSelectionMode) return;
            _selectedCategory = "All Notes";
            _selectedTag = null;
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
            RebuildTagChips();
            OnPropertyChanged(nameof(SelectedTag));
            OnPropertyChanged(nameof(HasSelectedTag));
            OnPropertyChanged(nameof(SelectedTagText));
            ApplyFilters();
        }

        private void OnDrawerTrashBinClick(object? sender, RoutedEventArgs e)
        {
            if (_isFolderSelectionMode) return;
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
            NotesFeedScreen.IsVisible = false;
            NoteEditorScreen.IsVisible = false;
            if (MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = false;
            }
            if (TrashBinScreen != null)
            {
                TrashBinScreen.IsVisible = true;
                _ = LoadTrashItemsAsync();
            }
        }

        private void OnCloseTrashBinClick(object? sender, RoutedEventArgs e)
        {
            if (TrashBinScreen != null)
            {
                TrashBinScreen.IsVisible = false;
            }
            ShowNotesFeed();
        }

        private void OnDrawerNewFolderClick(object? sender, RoutedEventArgs e)
        {
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
            ShowNewFolderDialog();
        }

        private void OnOpenCloudSyncClick(object? sender, RoutedEventArgs e)
        {
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
            UpdateModalAccountStatus();
            if (GoogleDriveAccountModal != null)
            {
                GoogleDriveAccountModal.IsVisible = true;
            }
        }

        private DispatcherTimer? _drawerFolderHoldTimer;
        private string? _pendingHoldFolder;
        private Point _drawerFolderPressStart;
        private bool _wasDrawerFolderLongPress = false;

        private void RebuildDrawerFolders()
        {
            if (DrawerFoldersPanel == null || _viewModel == null || LeftDrawerOverlay?.IsVisible != true) return;
            _drawerFolderHoldTimer?.Stop();
            _drawerFolderHoldTimer = null;
            DrawerFoldersPanel.Children.Clear();

            var allFolderPaths = DiscoverAllFolderPaths();
            if (allFolderPaths.Count == 0)
            {
                var emptyLabel = new TextBlock
                {
                    Text = "No folders created yet",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#6b7280")),
                    Margin = new Thickness(10, 8)
                };
                DrawerFoldersPanel.Children.Add(emptyLabel);
                return;
            }

            var noteCountsByFolder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (_viewModel.Notes != null)
            {
                foreach (var n in _viewModel.Notes)
                {
                    var f = Path.GetFileName(n.FolderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!string.IsNullOrEmpty(f))
                    {
                        noteCountsByFolder[f] = noteCountsByFolder.TryGetValue(f, out int cnt) ? cnt + 1 : 1;
                    }
                }
            }

            foreach (var folderPath in allFolderPaths)
            {
                var folderName = Path.GetFileName(folderPath);
                int depth = folderPath.Split('/').Length - 1;
                bool isSelectedCategory = string.Equals(_selectedCategory, folderName, StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(_selectedCategory, folderPath, StringComparison.OrdinalIgnoreCase);
                bool isFolderSelectedInBatch = _selectedFolders.Contains(folderPath);

                int noteCount = noteCountsByFolder.TryGetValue(folderName, out int cnt) ? cnt : 0;

                var folderRow = new Border
                {
                    Background = isFolderSelectedInBatch
                        ? (IsDarkMode ? new SolidColorBrush(Color.Parse("#1e293b")) : new SolidColorBrush(Color.Parse("#dbeafe")))
                        : (isSelectedCategory 
                            ? (IsDarkMode ? new SolidColorBrush(Color.Parse("#1a2230")) : new SolidColorBrush(Color.Parse("#e2e8f0"))) 
                            : Brushes.Transparent),
                    BorderBrush = isFolderSelectedInBatch 
                        ? AccentBrush 
                        : (isSelectedCategory ? GlassBorderBrush : Brushes.Transparent),
                    BorderThickness = new Thickness(isFolderSelectedInBatch || isSelectedCategory ? 1 : 0),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8),
                    Margin = new Thickness(depth * 14, 0, 0, 2),
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
                };

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                };

                // Check indicator (in selection mode) or folder icon
                Control leadingIcon;
                if (_isFolderSelectionMode)
                {
                    var checkBadge = new Border
                    {
                        Width = 18,
                        Height = 18,
                        CornerRadius = new CornerRadius(9),
                        Background = isFolderSelectedInBatch ? AccentBrush : Brushes.Transparent,
                        BorderBrush = isFolderSelectedInBatch ? AccentBrush : TextSecondaryBrush,
                        BorderThickness = new Thickness(1.5),
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                    };
                    if (isFolderSelectedInBatch)
                    {
                        checkBadge.Child = new PathIcon
                        {
                            Data = (StreamGeometry)Resources["IconCheck"]!,
                            Width = 10,
                            Height = 10,
                            Foreground = Brushes.White,
                            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                        };
                    }
                    leadingIcon = checkBadge;
                }
                else
                {
                    leadingIcon = new PathIcon
                    {
                        Data = (StreamGeometry)Resources["IconFolder"]!,
                        Width = 18,
                        Height = 18,
                        Foreground = isSelectedCategory ? AccentBrush : TextSecondaryBrush,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                    };
                }
                Grid.SetColumn(leadingIcon, 0);

                var folderText = new TextBlock
                {
                    Text = folderName,
                    FontSize = 14,
                    FontWeight = (isSelectedCategory || isFolderSelectedInBatch) ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = isFolderSelectedInBatch
                        ? AccentBrush
                        : (isSelectedCategory ? TextPrimaryBrush : TextSecondaryBrush),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(folderText, 1);

                var badgeBorder = new Border
                {
                    Background = ChipUnselectedBackgroundBrush,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                var badgeText = new TextBlock
                {
                    Text = noteCount.ToString(),
                    FontSize = 11,
                    Foreground = TextSecondaryBrush
                };
                badgeBorder.Child = badgeText;
                Grid.SetColumn(badgeBorder, 2);
                grid.Children.Add(badgeBorder);

                grid.Children.Add(leadingIcon);
                grid.Children.Add(folderText);

                folderRow.Child = grid;

                // Long press hold detection for folder selection
                var capturedFolder = folderPath;

                folderRow.PointerPressed += (s, pe) =>
                {
                    if (_isFolderSelectionMode) return;
                    _wasDrawerFolderLongPress = false;
                    _drawerFolderHoldTimer?.Stop();
                    _drawerFolderHoldTimer = null;
                    _pendingHoldFolder = capturedFolder;
                    _drawerFolderPressStart = pe.GetPosition(this);
                    _drawerFolderHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                    _drawerFolderHoldTimer.Tick += (ts, te) =>
                    {
                        _drawerFolderHoldTimer?.Stop();
                        _drawerFolderHoldTimer = null;
                        _wasDrawerFolderLongPress = true;
                        if (!string.IsNullOrEmpty(_pendingHoldFolder))
                        {
                            EnterFolderSelectionModeWithFolder(_pendingHoldFolder);
                        }
                    };
                    _drawerFolderHoldTimer.Start();
                };

                folderRow.PointerMoved += (s, pe) =>
                {
                    if (_drawerFolderHoldTimer != null)
                    {
                        var cur = pe.GetPosition(this);
                        if (Math.Abs(cur.X - _drawerFolderPressStart.X) > 48 || Math.Abs(cur.Y - _drawerFolderPressStart.Y) > 48)
                        {
                            _drawerFolderHoldTimer.Stop();
                            _drawerFolderHoldTimer = null;
                        }
                    }
                };

                folderRow.PointerReleased += (s, pe) =>
                {
                    _drawerFolderHoldTimer?.Stop();
                    _drawerFolderHoldTimer = null;
                };

                folderRow.PointerCaptureLost += (s, pe) =>
                {
                    _drawerFolderHoldTimer?.Stop();
                    _drawerFolderHoldTimer = null;
                };

                folderRow.Tapped += (s, e) =>
                {
                    if (_wasDrawerFolderLongPress)
                    {
                        _wasDrawerFolderLongPress = false;
                        return;
                    }

                    if (_isFolderSelectionMode)
                    {
                        if (_selectedFolders.Contains(capturedFolder))
                            _selectedFolders.Remove(capturedFolder);
                        else
                            _selectedFolders.Add(capturedFolder);

                        UpdateFolderSelectionState();
                        RebuildDrawerFolders();
                        return;
                    }

                    _selectedCategory = folderName;
                    if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
                    ApplyFilters();
                };

                DrawerFoldersPanel.Children.Add(folderRow);
            }
        }

        private void OnPromptDeleteFolder(string folderName)
        {
            _pendingDeleteFolderName = folderName;
            if (ConfirmDeleteFolderMessageText != null)
            {
                ConfirmDeleteFolderMessageText.Text = $"Delete folder '{folderName}' and move its notes to the Trash Bin?";
            }
            if (ConfirmDeleteFolderModal != null)
            {
                ConfirmDeleteFolderModal.IsVisible = true;
            }
        }

        private async void OnConfirmDeleteFolderClick(object? sender, RoutedEventArgs e)
        {
            var folderToDelete = _pendingDeleteFolderName;
            _pendingDeleteFolderName = null;
            if (ConfirmDeleteFolderModal != null) ConfirmDeleteFolderModal.IsVisible = false;

            if (string.IsNullOrEmpty(folderToDelete) || _viewModel == null) return;

            try
            {
                var root = _viewModel.NotesRoot;
                if (!string.IsNullOrEmpty(root))
                {
                    var folderPath = Path.Combine(root, folderToDelete);
                    if (Directory.Exists(folderPath))
                    {
                        await _viewModel.TrashBinService.MoveToTrashAsync(folderPath);
                    }

                    if (string.Equals(_selectedCategory, folderToDelete, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedCategory = "All Notes";
                    }

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
                Console.WriteLine($"[MobileMainView] Error deleting folder: {ex.Message}");
            }
        }

        private void OnCancelDeleteFolderClick(object? sender, RoutedEventArgs e)
        {
            if (ConfirmDeleteFolderModal != null) ConfirmDeleteFolderModal.IsVisible = false;
            _pendingDeleteFolderName = null;
        }

        // ── Tags Navigation & Drawer ──────────────────────────────────────
        private void RebuildDrawerTags()
        {
            if (DrawerTagsPanel == null || _viewModel?.Notes == null || LeftDrawerOverlay?.IsVisible != true) return;
            DrawerTagsPanel.Children.Clear();

            var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in _viewModel.Notes)
            {
                if (note.Tags != null)
                {
                    foreach (var t in note.Tags)
                    {
                        var clean = t.TrimStart('#').Trim();
                        if (!string.IsNullOrWhiteSpace(clean))
                        {
                            tagCounts[clean] = tagCounts.TryGetValue(clean, out var count) ? count + 1 : 1;
                        }
                    }
                }
            }

            if (tagCounts.Count == 0)
            {
                var emptyLabel = new TextBlock
                {
                    Text = "No tags found",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#6b7280")),
                    Margin = new Thickness(10, 8)
                };
                DrawerTagsPanel.Children.Add(emptyLabel);
                return;
            }

            foreach (var kvp in tagCounts.OrderBy(k => k.Key))
            {
                var tagName = kvp.Key;
                var count = kvp.Value;
                var isSelected = string.Equals(_selectedTag, tagName, StringComparison.OrdinalIgnoreCase);

                var btn = new Button
                {
                    Background = isSelected 
                        ? (IsDarkMode ? new SolidColorBrush(Color.Parse("#1a2230")) : new SolidColorBrush(Color.Parse("#e2e8f0")))
                        : Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8),
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
                };

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                };

                var tagIcon = new TextBlock
                {
                    Text = "#",
                    FontSize = 14,
                    FontWeight = FontWeight.Bold,
                    Foreground = isSelected ? AccentBrush : TextSecondaryBrush,
                    Margin = new Thickness(4, 0, 10, 0),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(tagIcon, 0);

                var tagText = new TextBlock
                {
                    Text = tagName,
                    FontSize = 14,
                    FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = isSelected ? TextPrimaryBrush : TextSecondaryBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(tagText, 1);

                var badgeBorder = new Border
                {
                    Background = ChipUnselectedBackgroundBrush,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                var badgeText = new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 11,
                    Foreground = TextSecondaryBrush
                };
                badgeBorder.Child = badgeText;
                Grid.SetColumn(badgeBorder, 2);

                grid.Children.Add(tagIcon);
                grid.Children.Add(tagText);
                grid.Children.Add(badgeBorder);

                btn.Content = grid;
                btn.Click += (s, e) =>
                {
                    SelectedTag = tagName;
                    if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
                };

                DrawerTagsPanel.Children.Add(btn);
            }
        }

        private void OnClearSelectedTagClick(object? sender, RoutedEventArgs e)
        {
            SelectedTag = null;
        }
    }
}
