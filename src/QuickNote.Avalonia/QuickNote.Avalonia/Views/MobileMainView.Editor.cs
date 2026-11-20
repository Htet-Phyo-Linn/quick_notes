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
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace QuickNote.Avalonia.Views
{
    public partial class MobileMainView
    {
        // ── Tag Editor & Tag Management in NoteEditorScreen ──────────────────────────
        private void RebuildEditorTags()
        {
            if (EditorTagsPanel == null) return;
            EditorTagsPanel.Children.Clear();

            var currentTags = _viewModel?.CurrentNote?.Tags ?? Array.Empty<string>();
            foreach (var tag in currentTags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                var clean = tag.TrimStart('#').Trim();
                if (string.IsNullOrEmpty(clean)) continue;

                var tagPill = new Border
                {
                    Background = ChipUnselectedBackgroundBrush,
                    BorderBrush = GlassBorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Height = 24,
                    Padding = new Thickness(8, 0, 4, 0),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                var pillStack = new StackPanel
                {
                    Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 4,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                pillStack.Children.Add(new TextBlock
                {
                    Text = "#",
                    Foreground = AccentBrush,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                });
                pillStack.Children.Add(new TextBlock
                {
                    Text = clean,
                    Foreground = TextPrimaryBrush,
                    FontSize = 11,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                });
                var removeBtn = new Button
                {
                    Content = "✕",
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = TextSecondaryBrush,
                    FontSize = 9,
                    Height = 18,
                    Padding = new Thickness(2, 0),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Tag = clean
                };
                removeBtn.Click += async (s, e) =>
                {
                    if (s is Button b && b.Tag is string tToRemove)
                    {
                        await RemoveTagFromCurrentNoteAsync(tToRemove);
                    }
                };
                pillStack.Children.Add(removeBtn);
                tagPill.Child = pillStack;
                EditorTagsPanel.Children.Add(tagPill);
            }
        }

        private void OnShowAddTagClick(object? sender, RoutedEventArgs e)
        {
            if (BtnShowAddTag != null) BtnShowAddTag.IsVisible = false;
            if (EditorAddTagContainer != null)
            {
                EditorAddTagContainer.IsVisible = true;
                if (EditorNewTagTextBox != null)
                {
                    EditorNewTagTextBox.Text = string.Empty;
                    EditorNewTagTextBox.Focus();
                }
            }
        }

        private void OnCancelAddTagClick(object? sender, RoutedEventArgs e)
        {
            if (EditorAddTagContainer != null) EditorAddTagContainer.IsVisible = false;
            if (BtnShowAddTag != null) BtnShowAddTag.IsVisible = true;
        }

        private async void OnConfirmAddTagClick(object? sender, RoutedEventArgs e)
        {
            await ConfirmAddTagFromInputAsync();
        }

        private async void OnEditorNewTagKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ConfirmAddTagFromInputAsync();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                OnCancelAddTagClick(sender, e);
            }
        }

        private async Task ConfirmAddTagFromInputAsync()
        {
            var tagText = EditorNewTagTextBox?.Text?.Trim().TrimStart('#');
            if (!string.IsNullOrWhiteSpace(tagText) && _viewModel?.CurrentNote != null)
            {
                var existing = _viewModel.CurrentNote.Tags?.ToList() ?? new List<string>();
                if (!existing.Any(t => string.Equals(t.TrimStart('#'), tagText, StringComparison.OrdinalIgnoreCase)))
                {
                    existing.Add(tagText);
                    _viewModel.CurrentNote.Tags = existing.ToArray();
                    await _viewModel.SaveNoteAsync();
                    RebuildEditorTags();
                    RebuildTagChips();
                    RebuildDrawerTags();
                    ApplyFilters();
                }
            }
            OnCancelAddTagClick(this, new RoutedEventArgs());
        }

        private async Task RemoveTagFromCurrentNoteAsync(string tagName)
        {
            if (_viewModel?.CurrentNote == null) return;
            var existing = _viewModel.CurrentNote.Tags?.ToList() ?? new List<string>();
            existing.RemoveAll(t => string.Equals(t.TrimStart('#'), tagName, StringComparison.OrdinalIgnoreCase));
            _viewModel.CurrentNote.Tags = existing.ToArray();
            await _viewModel.SaveNoteAsync();
            RebuildEditorTags();
            RebuildTagChips();
            RebuildDrawerTags();
            ApplyFilters();
        }

        private void OnDeleteNoteCardClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is MobileNoteItem item && _viewModel != null)
            {
                // Optimistic UI update: Card vanishes immediately (0ms)
                FilteredNotes.Remove(item);
                _viewModel.Notes.Remove(item.Note);
                OnPropertyChanged(nameof(NoteCountStatus));
                OnPropertyChanged(nameof(IsNotesEmpty));
                OnPropertyChanged(nameof(TotalNotesCountText));

                var noteToDelete = item.Note;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (File.Exists(noteToDelete.FullPath))
                        {
                            await _viewModel.TrashBinService.MoveToTrashAsync(noteToDelete.FullPath);
                        }
                        await _viewModel.RefreshFolderTreeAsync();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (LeftDrawerOverlay?.IsVisible == true)
                            {
                                RebuildDrawerFolders();
                                RebuildDrawerTags();
                                UpdateTrashBadge();
                            }
                            TriggerAutoSyncInBackground();
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MobileMainView] Background delete note error: {ex.Message}");
                    }
                });
            }
        }

        // ── Note Editor 3-Dots More Menu Actions ──────────────────────────
        private void OnShowNoteEditorMenuClick(object? sender, RoutedEventArgs e)
        {
            if (NoteEditorMenuModal != null)
            {
                if (MobileNotionEditor != null) MobileNotionEditor.IsVisible = false;
                OnPropertyChanged(nameof(ActiveNoteTitle));
                NoteEditorMenuModal.IsVisible = true;
            }
        }

        private void OnCloseNoteEditorMenuClick(object? sender, RoutedEventArgs e)
        {
            if (NoteEditorMenuModal != null)
            {
                NoteEditorMenuModal.IsVisible = false;
                if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
                {
                    MobileNotionEditor.IsVisible = true;
                }
            }
        }

        private void OnMenuModalContentPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
        }

        private async void OnEditorCopyNoteClick(object? sender, RoutedEventArgs e)
        {
            if (NoteEditorMenuModal != null) NoteEditorMenuModal.IsVisible = false;
            if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = true;
            }
            if (_viewModel?.CurrentNote == null) return;

            try
            {
                await _viewModel.SaveNoteAsync();
                var src = _viewModel.CurrentNote.FullPath;
                if (File.Exists(src))
                {
                    var dir = Path.GetDirectoryName(src) ?? _viewModel.NotesRoot;
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
                    await _viewModel.RefreshFolderTreeAsync();
                    await _viewModel.Refresh();
                    RebuildDrawerFolders();
                    ApplyFilters();
                    TriggerAutoSyncInBackground();
                    ShowToast("Note duplicated", isProgress: false, isError: false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error copying current note: {ex.Message}");
            }
        }

        private async void OnEditorMoveNoteClick(object? sender, RoutedEventArgs e)
        {
            if (NoteEditorMenuModal != null) NoteEditorMenuModal.IsVisible = false;
            if (_viewModel?.CurrentNote == null) return;

            await _viewModel.SaveNoteAsync();

            var currentNote = _viewModel.CurrentNote;
            foreach (var item in FilteredNotes) item.IsSelected = false;
            var match = FilteredNotes.FirstOrDefault(n => n.Note.FullPath == currentNote.FullPath);
            if (match != null) match.IsSelected = true;

            if (MoveNotesModalTitle != null)
            {
                MoveNotesModalTitle.Text = $"Move '{currentNote.Title}'";
            }
            PopulateMoveNotesDestinations(string.Empty);
            if (MoveNotesSearchBox != null) MoveNotesSearchBox.Text = string.Empty;
            if (MoveNotesModal != null) MoveNotesModal.IsVisible = true;
        }

        private void OnEditorDeleteNoteClick(object? sender, RoutedEventArgs e)
        {
            if (NoteEditorMenuModal != null) NoteEditorMenuModal.IsVisible = false;
            OnDeleteCurrentNoteClick(sender, e);
        }

        private void OnDeleteCurrentNoteClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                var current = _viewModel.CurrentNote;
                if (current != null)
                {
                    var match = FilteredNotes.FirstOrDefault(n => n.Note.Id == current.Id || n.Note.FullPath == current.FullPath);
                    if (match != null) FilteredNotes.Remove(match);
                    _viewModel.Notes.Remove(current);
                }
                ShowNotesFeed();
                OnPropertyChanged(nameof(TotalNotesCountText));
                OnPropertyChanged(nameof(NoteCountStatus));
                OnPropertyChanged(nameof(IsNotesEmpty));

                var pathToDelete = current?.FullPath;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(pathToDelete) && File.Exists(pathToDelete))
                        {
                            await _viewModel.TrashBinService.MoveToTrashAsync(pathToDelete);
                        }
                        await _viewModel.RefreshFolderTreeAsync();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (LeftDrawerOverlay?.IsVisible == true)
                            {
                                RebuildDrawerFolders();
                                RebuildDrawerTags();
                                UpdateTrashBadge();
                            }
                            TriggerAutoSyncInBackground();
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MobileMainView] Background delete current note error: {ex.Message}");
                    }
                });
            }
        }

        // ── Hardware Back Button / KeyDown ─────────────────────────────────
        private void OnViewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back || e.Key == Key.Escape)
            {
                if (VersionHistoryModal?.IsVisible == true)
                {
                    e.Handled = true;
                    CloseVersionHistoryModal();
                }
                else if (SettingsModal?.IsVisible == true)
                {
                    e.Handled = true;
                    OnCloseSettingsModalClick(sender, e);
                }
                else if (NoteEditorMenuModal?.IsVisible == true)
                {
                    e.Handled = true;
                    NoteEditorMenuModal.IsVisible = false;
                    if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
                    {
                        MobileNotionEditor.IsVisible = true;
                    }
                }
                else if (ConfirmBatchDeleteFoldersModal?.IsVisible == true)
                {
                    e.Handled = true;
                    ConfirmBatchDeleteFoldersModal.IsVisible = false;
                }
                else if (MoveFoldersModal?.IsVisible == true)
                {
                    e.Handled = true;
                    MoveFoldersModal.IsVisible = false;
                }
                else if (MoveNotesModal?.IsVisible == true)
                {
                    e.Handled = true;
                    MoveNotesModal.IsVisible = false;
                    if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
                    {
                        MobileNotionEditor.IsVisible = true;
                    }
                }
                else if (ParentFolderDropdownContainer?.IsVisible == true)
                {
                    e.Handled = true;
                    ParentFolderDropdownContainer.IsVisible = false;
                }
                else if (ConfirmDeleteFolderModal?.IsVisible == true)
                {
                    e.Handled = true;
                    ConfirmDeleteFolderModal.IsVisible = false;
                    _pendingDeleteFolderName = null;
                }
                else if (ConfirmEmptyTrashModal?.IsVisible == true)
                {
                    e.Handled = true;
                    ConfirmEmptyTrashModal.IsVisible = false;
                }
                else if (TrashBinScreen?.IsVisible == true)
                {
                    e.Handled = true;
                    TrashBinScreen.IsVisible = false;
                }
                else if (_isFolderSelectionMode)
                {
                    e.Handled = true;
                    ExitFolderSelectionMode();
                }
                else if (LeftDrawerOverlay?.IsVisible == true)
                {
                    e.Handled = true;
                    LeftDrawerOverlay.IsVisible = false;
                }
                else if (IsSelectionMode)
                {
                    e.Handled = true;
                    ExitSelectionMode();
                }
                else if (NoteEditorScreen.IsVisible)
                {
                    e.Handled = true;
                    _ = SaveAndCloseEditorAsync();
                }
            }
        }

        // ── Mobile Formatting Toolbar Actions ─────────────────────────────
        private async void OnFormatH1Click(object? sender, RoutedEventArgs e) => await ExecFormat("h1");
        private async void OnFormatH2Click(object? sender, RoutedEventArgs e) => await ExecFormat("h2");
        private async void OnFormatBoldClick(object? sender, RoutedEventArgs e) => await ExecFormat("bold");
        private async void OnFormatItalicClick(object? sender, RoutedEventArgs e) => await ExecFormat("italic");
        private async void OnFormatBulletClick(object? sender, RoutedEventArgs e) => await ExecFormat("bullet");
        private async void OnFormatNumberedClick(object? sender, RoutedEventArgs e) => await ExecFormat("number");
        private async void OnFormatTodoClick(object? sender, RoutedEventArgs e) => await ExecFormat("todo");
        private async void OnFormatCodeClick(object? sender, RoutedEventArgs e) => await ExecFormat("code");
        private async void OnFormatQuoteClick(object? sender, RoutedEventArgs e) => await ExecFormat("quote");

        private async Task ExecFormat(string action)
        {
            if (MobileNotionEditor != null)
            {
                await MobileNotionEditor.ExecuteEditorActionAsync(action);
                await MobileNotionEditor.FocusEditorAsync();
            }
        }

        private async void OnInsertImageClick(object? sender, RoutedEventArgs e)
        {
            if (MobileNotionEditor == null) return;
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.StorageProvider != null)
                {
                    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "Select Image to Insert",
                        AllowMultiple = false,
                        FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
                    });

                    if (files != null && files.Count > 0)
                    {
                        var file = files[0];
                        await using var stream = await file.OpenReadAsync();
                        await MobileNotionEditor.InsertImageAsync(stream, file.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error selecting/inserting image: {ex.Message}");
            }
        }
        private void OnEditorLastModifiedClick(object? sender, RoutedEventArgs e)
        {
            OpenVersionHistoryModal(_viewModel?.CurrentNote?.Title);
        }

        private void OnNoteTitleKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = _viewModel?.SaveNoteAsync();
                _ = MobileNotionEditor?.FocusEditorAsync();
            }
        }
    }
}
