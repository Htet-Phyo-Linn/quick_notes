using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace QuickNote.Avalonia.Views
{
    public partial class MobileMainView
    {
        // ── In-App Trash Bin Handlers ─────────────────────────────────────
        private async void UpdateTrashBadge()
        {
            try
            {
                if (_viewModel?.TrashBinService != null && DrawerTrashBadgeText != null && LeftDrawerOverlay?.IsVisible == true)
                {
                    var items = await _viewModel.TrashBinService.GetTrashItemsAsync();
                    DrawerTrashBadgeText.Text = items.Count().ToString();
                }
            }
            catch { }
        }

        private async Task LoadTrashItemsAsync()
        {
            if (TrashItemsPanel == null || _viewModel?.TrashBinService == null) return;
            TrashItemsPanel.Children.Clear();

            try
            {
                var items = (await _viewModel.TrashBinService.GetTrashItemsAsync()).OrderByDescending(i => i.DeletedAt).ToList();

                if (DrawerTrashBadgeText != null)
                {
                    DrawerTrashBadgeText.Text = items.Count.ToString();
                }

                if (items.Count == 0)
                {
                    if (TrashEmptyState != null) TrashEmptyState.IsVisible = true;
                    return;
                }

                if (TrashEmptyState != null) TrashEmptyState.IsVisible = false;

                foreach (var item in items)
                {
                    var card = new Border
                    {
                        Background = GlassSurfaceBrush,
                        BorderBrush = GlassBorderBrush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(14, 12),
                        Margin = new Thickness(0, 0, 0, 4)
                    };

                    var grid = new Grid
                    {
                        RowDefinitions = new RowDefinitions("Auto,Auto"),
                        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                    };

                    var icon = new PathIcon
                    {
                        Data = (StreamGeometry)Resources[item.IsDirectory ? "IconFolder" : "IconDocument"]!,
                        Width = 20,
                        Height = 20,
                        Foreground = new SolidColorBrush(Color.Parse(item.IsDirectory ? "#3b82f6" : "#9ca3af")),
                        Margin = new Thickness(0, 2, 12, 0),
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top
                    };
                    Grid.SetRowSpan(icon, 2);
                    Grid.SetColumn(icon, 0);

                    var titleText = new TextBlock
                    {
                        Text = !string.IsNullOrWhiteSpace(item.Title) ? item.Title : Path.GetFileName(item.OriginalPath),
                        FontSize = 15,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = TextPrimaryBrush,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Grid.SetRow(titleText, 0);
                    Grid.SetColumn(titleText, 1);

                    var pathText = new TextBlock
                    {
                        Text = $"{item.DeletedAt.ToLocalTime():MMM d, h:mm tt} • {Path.GetFileName(Path.GetDirectoryName(item.OriginalPath))}",
                        FontSize = 11,
                        Foreground = TextSecondaryBrush,
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    Grid.SetRow(pathText, 1);
                    Grid.SetColumn(pathText, 1);

                    var actionsPanel = new StackPanel
                    {
                        Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8,
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                    };
                    Grid.SetRowSpan(actionsPanel, 2);
                    Grid.SetColumn(actionsPanel, 2);

                    var restoreBtn = new Button
                    {
                        Content = "Restore",
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        Background = new SolidColorBrush(Color.Parse("#2563eb")),
                        Foreground = Brushes.White,
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 6)
                    };
                    var capturedItem = item;
                    restoreBtn.Click += async (s, e) =>
                    {
                        if (_viewModel?.TrashBinService == null) return;
                        restoreBtn.IsEnabled = false;
                        card.Opacity = 0.4;
                        TrashItemsPanel.Children.Remove(card);
                        if (TrashItemsPanel.Children.Count == 0 && TrashEmptyState != null)
                        {
                            TrashEmptyState.IsVisible = true;
                        }

                        try
                        {
                            await _viewModel.TrashBinService.RestoreItemAsync(capturedItem);
                            await _viewModel.RefreshFolderTreeAsync();
                            await _viewModel.Refresh();
                            RebuildTagChips();
                            RebuildDrawerFolders();
                            RebuildDrawerTags();
                            ApplyFilters();
                            UpdateTrashBadge();
                            OnPropertyChanged(nameof(TotalNotesCountText));
                            TriggerAutoSyncInBackground();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[MobileMainView] Error restoring trash item: {ex.Message}");
                            await LoadTrashItemsAsync();
                        }
                    };
                    actionsPanel.Children.Add(restoreBtn);

                    var permDelBtn = new Button
                    {
                        Background = Brushes.Transparent,
                        Foreground = new SolidColorBrush(Color.Parse("#ef4444")),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(6)
                    };
                    permDelBtn.Content = new PathIcon
                    {
                        Data = (StreamGeometry)Resources["IconTrash"]!,
                        Width = 16,
                        Height = 16,
                        Foreground = new SolidColorBrush(Color.Parse("#ef4444"))
                    };
                    permDelBtn.Click += async (s, e) =>
                    {
                        if (_viewModel?.TrashBinService == null) return;
                        permDelBtn.IsEnabled = false;
                        card.Opacity = 0.4;
                        TrashItemsPanel.Children.Remove(card);
                        if (TrashItemsPanel.Children.Count == 0 && TrashEmptyState != null)
                        {
                            TrashEmptyState.IsVisible = true;
                        }

                        try
                        {
                            await _viewModel.TrashBinService.PermanentDeleteAsync(capturedItem);
                            UpdateTrashBadge();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[MobileMainView] Error deleting trash item: {ex.Message}");
                            await LoadTrashItemsAsync();
                        }
                    };
                    actionsPanel.Children.Add(permDelBtn);

                    grid.Children.Add(icon);
                    grid.Children.Add(titleText);
                    grid.Children.Add(pathText);
                    grid.Children.Add(actionsPanel);

                    card.Child = grid;
                    TrashItemsPanel.Children.Add(card);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Error loading trash items: {ex.Message}");
            }
        }

        private async void OnRestoreAllTrashClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.TrashBinService == null) return;
            try
            {
                var items = (await _viewModel.TrashBinService.GetTrashItemsAsync()).ToList();
                foreach (var item in items)
                {
                    await _viewModel.TrashBinService.RestoreItemAsync(item);
                }
                await _viewModel.RefreshFolderTreeAsync();
                await _viewModel.Refresh();
                RebuildTagChips();
                RebuildDrawerFolders();
                RebuildDrawerTags();
                UpdateTrashBadge();
                ApplyFilters();
                OnPropertyChanged(nameof(TotalNotesCountText));
                await LoadTrashItemsAsync();
                TriggerAutoSyncInBackground();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] OnRestoreAllTrashClick error: {ex.Message}");
            }
        }

        private void OnPromptEmptyTrashClick(object? sender, RoutedEventArgs e)
        {
            if (ConfirmEmptyTrashModal != null)
            {
                ConfirmEmptyTrashModal.IsVisible = true;
            }
        }

        private async void OnConfirmEmptyTrashClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.TrashBinService != null)
            {
                await _viewModel.TrashBinService.EmptyTrashAsync();
                await LoadTrashItemsAsync();
                await _viewModel.RefreshFolderTreeAsync();
                await _viewModel.Refresh();
                RebuildTagChips();
                RebuildDrawerFolders();
                RebuildDrawerTags();
                ApplyFilters();
            }
            if (ConfirmEmptyTrashModal != null)
            {
                ConfirmEmptyTrashModal.IsVisible = false;
            }
        }

        private void OnCancelEmptyTrashClick(object? sender, RoutedEventArgs e)
        {
            if (ConfirmEmptyTrashModal != null)
            {
                ConfirmEmptyTrashModal.IsVisible = false;
            }
        }
    }
}
