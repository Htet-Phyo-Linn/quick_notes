using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.Views
{
    public partial class MobileMainView
    {
        private List<NoteBackupGroup> _allBackupGroups = new();
        private NoteBackupGroup? _selectedBackupGroup;
        private NoteBackupVersion? _selectedBackupVersion;

        // ── Mobile Version History Modal Handlers ─────────────────────────
        public async void OpenVersionHistoryModal(string? targetNoteTitle = null)
        {
            if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = false;
            }

            if (VersionHistoryModal != null)
            {
                VersionHistoryModal.IsVisible = true;
            }

            if (BackupSearchBox != null)
            {
                BackupSearchBox.Text = string.Empty;
            }

            // Show files view, hide preview detail by default on mobile
            ShowBackupFilesView();
            await ReloadBackupGroupsAsync(targetNoteTitle);
        }

        public void CloseVersionHistoryModal()
        {
            if (VersionHistoryModal != null)
            {
                VersionHistoryModal.IsVisible = false;
            }

            if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = true;
            }
        }

        private void OnCloseVersionHistoryClick(object? sender, RoutedEventArgs e)
        {
            CloseVersionHistoryModal();
        }

        private void OnVersionHistoryModalContentPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
        }

        private async Task ReloadBackupGroupsAsync(string? autoSelectNoteTitle = null)
        {
            if (_viewModel?.BackupService == null) return;

            try
            {
                var groups = await _viewModel.BackupService.GetBackupGroupsAsync();
                _allBackupGroups = groups.OrderBy(g => g.NoteTitle).ToList();

                PopulateBackupNotesList(BackupSearchBox?.Text);

                if (!string.IsNullOrEmpty(autoSelectNoteTitle))
                {
                    var match = _allBackupGroups.FirstOrDefault(g =>
                        string.Equals(g.NoteTitle, autoSelectNoteTitle, StringComparison.OrdinalIgnoreCase) ||
                        g.Versions.Any(v => v.OriginalFilePath.Contains(autoSelectNoteTitle, StringComparison.OrdinalIgnoreCase)));

                    if (match != null)
                    {
                        SelectBackupGroup(match);
                        return;
                    }
                }

                // If only 1 group exists, open it directly
                if (_allBackupGroups.Count == 1)
                {
                    SelectBackupGroup(_allBackupGroups[0]);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error loading backup groups: {ex.Message}");
            }
        }

        private void OnBackupSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            PopulateBackupNotesList(BackupSearchBox?.Text);
        }

        private void PopulateBackupNotesList(string? query)
        {
            if (BackupNotesListPanel == null) return;
            BackupNotesListPanel.Children.Clear();

            var clean = query?.Trim() ?? string.Empty;
            var filtered = string.IsNullOrEmpty(clean)
                ? _allBackupGroups
                : _allBackupGroups.Where(g =>
                    g.NoteTitle.Contains(clean, StringComparison.OrdinalIgnoreCase) ||
                    g.OriginalFilePath.Contains(clean, StringComparison.OrdinalIgnoreCase)).ToList();

            if (BackupNotesEmptyText != null)
            {
                BackupNotesEmptyText.IsVisible = (filtered.Count == 0);
            }

            foreach (var group in filtered)
            {
                var rowBtn = new Button
                {
                    Background = GlassSurfaceBrush,
                    BorderBrush = GlassBorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12, 10),
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                };

                var docIcon = new PathIcon
                {
                    Data = (StreamGeometry)Resources["IconDocument"]!,
                    Width = 16,
                    Height = 16,
                    Foreground = AccentBrush,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(docIcon, 0);

                var titlesStack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 2
                };
                titlesStack.Children.Add(new TextBlock
                {
                    Text = group.NoteTitle,
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = TextPrimaryBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                titlesStack.Children.Add(new TextBlock
                {
                    Text = $"{Path.GetFileName(group.OriginalFilePath)} • Latest: {group.LatestTimestampText}",
                    FontSize = 11,
                    Foreground = TextSecondaryBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                Grid.SetColumn(titlesStack, 1);

                var badgeBorder = new Border
                {
                    Background = ChipUnselectedBackgroundBrush,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                badgeBorder.Child = new TextBlock
                {
                    Text = $"{group.VersionCount} rev",
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = AccentBrush
                };
                Grid.SetColumn(badgeBorder, 2);

                grid.Children.Add(docIcon);
                grid.Children.Add(titlesStack);
                grid.Children.Add(badgeBorder);

                rowBtn.Content = grid;
                var capturedGroup = group;
                rowBtn.Click += (s, e) =>
                {
                    SelectBackupGroup(capturedGroup);
                };

                BackupNotesListPanel.Children.Add(rowBtn);
            }
        }

        private async void SelectBackupGroup(NoteBackupGroup group)
        {
            _selectedBackupGroup = group;

            if (BackupDetailNoteTitle != null)
            {
                BackupDetailNoteTitle.Text = group.NoteTitle;
            }
            if (BackupDetailNotePath != null)
            {
                BackupDetailNotePath.Text = group.OriginalFilePath;
            }

            ShowBackupVersionsView();
            PopulateBackupVersionsList(group);

            if (group.Versions.Count > 0)
            {
                await SelectBackupVersionAsync(group.Versions[0]);
            }
        }

        private void PopulateBackupVersionsList(NoteBackupGroup group)
        {
            if (BackupVersionsListPanel == null) return;
            BackupVersionsListPanel.Children.Clear();

            foreach (var version in group.Versions)
            {
                var verBtn = new Button
                {
                    Background = GlassSurfaceBrush,
                    BorderBrush = GlassBorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                };

                var historyIcon = new PathIcon
                {
                    Data = (StreamGeometry)Resources["IconHistory"]!,
                    Width = 14,
                    Height = 14,
                    Foreground = AccentBrush,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(historyIcon, 0);

                var infoStack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 2
                };
                infoStack.Children.Add(new TextBlock
                {
                    Text = version.FormattedDateTime,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = TextPrimaryBrush
                });
                infoStack.Children.Add(new TextBlock
                {
                    Text = $"{version.RelativeTimeText} • {version.FormattedSize} • {version.TypeBadgeText}",
                    FontSize = 11,
                    Foreground = TextSecondaryBrush
                });
                Grid.SetColumn(infoStack, 1);

                var chevron = new PathIcon
                {
                    Data = (StreamGeometry)Resources["IconChevronRight"]!,
                    Width = 12,
                    Height = 12,
                    Foreground = TextSecondaryBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(chevron, 2);

                grid.Children.Add(historyIcon);
                grid.Children.Add(infoStack);
                grid.Children.Add(chevron);

                verBtn.Content = grid;
                var capturedVer = version;
                verBtn.Click += async (s, e) =>
                {
                    await SelectBackupVersionAsync(capturedVer);
                };

                BackupVersionsListPanel.Children.Add(verBtn);
            }
        }

        private async Task SelectBackupVersionAsync(NoteBackupVersion version)
        {
            _selectedBackupVersion = version;

            if (BackupPreviewMetaText != null)
            {
                BackupPreviewMetaText.Text = $"Snapshot: {version.FormattedDateTime} ({version.RelativeTimeText}) • {version.FormattedSize}";
            }

            if (_viewModel?.BackupService != null)
            {
                var content = await _viewModel.BackupService.ReadBackupContentAsync(version.BackupFilePath);
                if (BackupPreviewTextBox != null)
                {
                    BackupPreviewTextBox.Text = content;
                }
            }

            if (BackupDetailViewContainer != null)
            {
                BackupDetailViewContainer.IsVisible = true;
            }
        }

        private void OnBackupBackToFilesClick(object? sender, RoutedEventArgs e)
        {
            ShowBackupFilesView();
        }

        private void ShowBackupFilesView()
        {
            if (BackupFilesViewContainer != null) BackupFilesViewContainer.IsVisible = true;
            if (BackupVersionsViewContainer != null) BackupVersionsViewContainer.IsVisible = false;
        }

        private void ShowBackupVersionsView()
        {
            if (BackupFilesViewContainer != null) BackupFilesViewContainer.IsVisible = false;
            if (BackupVersionsViewContainer != null) BackupVersionsViewContainer.IsVisible = true;
        }

        private async void OnRestoreVersionClick(object? sender, RoutedEventArgs e)
        {
            if (_selectedBackupVersion == null || _viewModel?.BackupService == null) return;

            try
            {
                ShowToast("Restoring note version...", isProgress: true);

                bool success = await _viewModel.BackupService.RestoreBackupAsync(_selectedBackupVersion.OriginalFilePath, _selectedBackupVersion.BackupFilePath);
                if (success)
                {
                    await _viewModel.RefreshFolderTreeAsync();
                    await _viewModel.Refresh();

                    // If restored note is currently open note, reload it
                    if (_viewModel.CurrentNote != null &&
                        _selectedBackupGroup != null &&
                        string.Equals(_viewModel.CurrentNote.Title, _selectedBackupGroup.NoteTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        var content = await _viewModel.BackupService.ReadBackupContentAsync(_selectedBackupVersion.BackupFilePath);
                        _viewModel.EditorContent = content;
                        if (MobileNotionEditor != null)
                        {
                            MobileNotionEditor.LoadMarkdown(content, force: true);
                        }
                    }

                    RebuildDrawerFolders();
                    ApplyFilters();
                    TriggerAutoSyncInBackground();

                    ShowToast("Version restored successfully!", isProgress: false, isError: false);
                    CloseVersionHistoryModal();
                }
                else
                {
                    ShowToast("Failed to restore note version.", isProgress: false, isError: true);
                }
            }
            catch (Exception ex)
            {
                ShowToast($"Restore error: {ex.Message}", isProgress: false, isError: true);
            }
        }

        private async void OnCopyVersionTextClick(object? sender, RoutedEventArgs e)
        {
            var text = BackupPreviewTextBox?.Text;
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(text);
                    ShowToast("Version text copied to clipboard!");
                }
            }
            catch { }
        }

        private async void OnCreateManualSnapshotClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.CurrentNote == null || _viewModel.BackupService == null)
            {
                ShowToast("Open a note first to take a manual snapshot.", isProgress: false, isError: true);
                return;
            }

            try
            {
                ShowToast("Creating backup snapshot...", isProgress: true);
                await _viewModel.SaveNoteAsync();

                var snapshot = await _viewModel.BackupService.CreateManualBackupAsync(_viewModel.CurrentNote.FullPath);
                if (snapshot != null)
                {
                    ShowToast("Manual backup snapshot created!");
                    await ReloadBackupGroupsAsync(_viewModel.CurrentNote.Title);
                }
                else
                {
                    ShowToast("Could not create snapshot.", isProgress: false, isError: true);
                }
            }
            catch (Exception ex)
            {
                ShowToast($"Snapshot error: {ex.Message}", isProgress: false, isError: true);
            }
        }

        private void OnDrawerVersionHistoryClick(object? sender, RoutedEventArgs e)
        {
            if (LeftDrawerOverlay != null) LeftDrawerOverlay.IsVisible = false;
            OpenVersionHistoryModal();
        }

        private void OnEditorVersionHistoryClick(object? sender, RoutedEventArgs e)
        {
            if (NoteEditorMenuModal != null) NoteEditorMenuModal.IsVisible = false;
            OpenVersionHistoryModal(_viewModel?.CurrentNote?.Title);
        }
    }
}
