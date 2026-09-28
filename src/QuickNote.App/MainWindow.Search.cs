using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using QuickNote.App.Dialogs;
using QuickNote.App.Helpers;
using QuickNote.App.ViewModels;
using QuickNote.Core.Models;

namespace QuickNote.App
{
    public partial class MainWindow
    {
        private void OutlineTreeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindVisualChild<ScrollViewer>(OutlineTreeView);
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

        private void OutlineTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is Heading heading)
            {
                _viewModel.NavigateToHeading(heading);
                if (ActualWidth < BreakpointMedium)
                {
                    SetRightPanelVisibility(false);
                    UpdateResponsiveImages();
                    UpdateDocumentLayoutWidth();
                }
            }
        }

        private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is FolderNode node && !node.IsDirectory)
            {
                var note = _viewModel.Notes.FirstOrDefault(n => string.Equals(n.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase));
                if (note != null)
                {
                    _viewModel.CurrentNote = note;
                    if (ActualWidth < BreakpointCompact)
                    {
                        SetLeftPanelVisibility(false);
                        UpdateResponsiveImages();
                        UpdateDocumentLayoutWidth();
                    }
                }
            }
        }

        private void FolderTreeView_Expanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is FolderNode node && node.IsDirectory)
            {
                _viewModel.SetFolderExpandedState(node.RelativePath, true);
            }
        }

        private void OnNavigationRequested(object? sender, NavigationEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    NotionEditorBox.Focus();
                    int currentLine = 1;
                    foreach (var block in NotionEditorBox.Document.Blocks)
                    {
                        if (currentLine >= e.LineNumber)
                        {
                            if (e.TopAlign)
                            {
                                var sv = FindVisualChild<ScrollViewer>(NotionEditorBox);
                                if (sv != null)
                                {
                                    var rect = block.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                                    double targetOffset = sv.VerticalOffset + rect.Top - 16;
                                    sv.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0, sv.ScrollableHeight));
                                }
                                else
                                {
                                    block.BringIntoView();
                                }
                            }
                            else
                            {
                                block.BringIntoView();
                            }
                            NotionEditorBox.CaretPosition = block.ContentStart;
                            break;
                        }
                        currentLine++;
                    }
                }
                catch { }
            });
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_viewModel.EditorContent) || e.PropertyName == nameof(_viewModel.CurrentNote))
            {
                if (!_isUpdatingFromNotion)
                {
                    LoadDocumentIntoEditor();
                }
            }
            else if (e.PropertyName == nameof(_viewModel.IsReadingMode))
            {
                UpdateReadingModeUI(_viewModel.IsReadingMode);
            }
            else if (e.PropertyName == nameof(_viewModel.IsSplitMode))
            {
                UpdateSplitViewLayout(_viewModel.IsSplitMode);
            }
            else if (e.PropertyName == nameof(_viewModel.SecondaryNote) || e.PropertyName == nameof(_viewModel.SecondaryEditorContent))
            {
                if (_viewModel.IsSplitMode && !_isUpdatingSecondaryFromNotion)
                {
                    LoadSecondaryDocument();
                }
            }
        }

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                Style = (Style)FindResource("NotionContextMenuStyle")
            };
            var menuItemStyle = (Style)FindResource("NotionMenuItemStyle");

            // 1. New Folder
            var newFolderItem = new MenuItem
            {
                Header = "New Folder",
                Icon = CreateMenuIcon("IconFolderGeometry", isStroke: false),
                Style = menuItemStyle,
                Command = _viewModel.NewFolderCommand
            };
            menu.Items.Add(newFolderItem);

            // 2. Save Note
            var saveItem = new MenuItem
            {
                Header = "Save Note (Ctrl+S)",
                Icon = CreateMenuIcon("IconSaveGeometry"),
                Style = menuItemStyle,
                Command = _viewModel.SaveCommand
            };
            menu.Items.Add(saveItem);

            // 3. Delete Note
            var deleteItem = new MenuItem
            {
                Header = "Delete Note",
                Icon = CreateMenuIcon("IconTrashGeometry"),
                Style = menuItemStyle,
                Command = _viewModel.DeleteNoteCommand
            };
            menu.Items.Add(deleteItem);

            menu.Items.Add(new Separator { Background = (Brush)FindResource("NotionBorder") });

            // 4. Refresh Explorer
            var refreshItem = new MenuItem
            {
                Header = "Refresh Explorer (Ctrl+R)",
                Icon = CreateMenuIcon("IconRefreshGeometry"),
                Style = menuItemStyle,
                Command = _viewModel.RefreshCommand
            };
            menu.Items.Add(refreshItem);

            // 5. OneDrive Sync
            var syncItem = new MenuItem
            {
                Header = "OneDrive Sync",
                Icon = CreateMenuIcon("IconCloudGeometry"),
                Style = menuItemStyle,
                Command = _viewModel.OpenSyncDialogCommand
            };
            menu.Items.Add(syncItem);

            menu.Items.Add(new Separator { Background = (Brush)FindResource("NotionBorder") });

            // 6. Export Note Submenu
            var exportItem = new MenuItem
            {
                Header = "Export Note",
                Icon = CreateMenuIcon("IconExportGeometry"),
                Style = menuItemStyle
            };

            var exportMdItem = new MenuItem
            {
                Header = "Export as Markdown (.md)",
                Icon = CreateMenuIcon("IconFileGeometry"),
                Style = menuItemStyle
            };
            exportMdItem.Click += async (s, args) => await ExportCurrentNoteAsMdAsync();

            var exportDocxItem = new MenuItem
            {
                Header = "Export as Microsoft Word (.docx)",
                Icon = CreateMenuIcon("IconFileGeometry"),
                Style = menuItemStyle
            };
            exportDocxItem.Click += async (s, args) => await ExportCurrentNoteAsDocxAsync();

            exportItem.Items.Add(exportMdItem);
            exportItem.Items.Add(exportDocxItem);
            menu.Items.Add(exportItem);

            menu.Items.Add(new Separator { Background = (Brush)FindResource("NotionBorder") });

            // 7. Trash Bin
            var trashItem = new MenuItem
            {
                Header = "Trash Bin",
                Icon = CreateMenuIcon("IconTrashGeometry"),
                Style = menuItemStyle,
                Command = _viewModel.OpenTrashBinCommand
            };
            menu.Items.Add(trashItem);

            menu.Items.Add(new Separator { Background = (Brush)FindResource("NotionBorder") });

            // 8. Toggle Theme
            var themeItem = new MenuItem
            {
                Header = _viewModel.IsDarkMode ? "Switch to Light Mode" : "Switch to Dark Mode",
                Icon = CreateMenuIcon(_viewModel.IsDarkMode ? "IconSunGeometry" : "IconMoonGeometry"),
                Style = menuItemStyle,
                Command = _viewModel.ToggleThemeCommand
            };
            menu.Items.Add(themeItem);

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.CurrentNote == null)
            {
                ConfirmDialog.ShowMessage(this, "Export Note", "No note is currently selected to export.", "OK", _viewModel.IsDarkMode);
                return;
            }

            var menu = new ContextMenu
            {
                Style = (Style)FindResource("NotionContextMenuStyle")
            };
            var menuItemStyle = (Style)FindResource("NotionMenuItemStyle");

            var exportMdItem = new MenuItem
            {
                Header = "Export as Markdown (.md)",
                Icon = CreateMenuIcon("IconFileGeometry"),
                Style = menuItemStyle
            };
            exportMdItem.Click += async (s, args) => await ExportCurrentNoteAsMdAsync();

            var exportDocxItem = new MenuItem
            {
                Header = "Export as Microsoft Word (.docx)",
                Icon = CreateMenuIcon("IconFileGeometry"),
                Style = menuItemStyle
            };
            exportDocxItem.Click += async (s, args) => await ExportCurrentNoteAsDocxAsync();

            menu.Items.Add(exportMdItem);
            menu.Items.Add(exportDocxItem);

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }

        private async Task ExportCurrentNoteAsMdAsync()
        {
            if (_viewModel.CurrentNote == null) return;

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Note as Markdown",
                Filter = "Markdown Document (*.md)|*.md",
                FileName = $"{_viewModel.CurrentNoteTitle}.md"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var content = NotionMarkdownHelper.FlowDocumentToMarkdown(NotionEditorBox.Document);
                    var title = _viewModel.CurrentNoteTitle;
                    var fullMarkdown = CombineTitleAndBody(title, content);
                    await File.WriteAllTextAsync(sfd.FileName, fullMarkdown);

                    ConfirmDialog.ShowMessage(this, "Export Success", $"Successfully exported note to:\n{sfd.FileName}", "OK", _viewModel.IsDarkMode);
                }
                catch (Exception ex)
                {
                    ConfirmDialog.ShowMessage(this, "Export Failed", $"Could not export Markdown file:\n{ex.Message}", "OK", _viewModel.IsDarkMode);
                }
            }
        }

        private async Task ExportCurrentNoteAsDocxAsync()
        {
            if (_viewModel.CurrentNote == null) return;

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Note as Microsoft Word Document",
                Filter = "Microsoft Word Document (*.docx)|*.docx",
                FileName = $"{_viewModel.CurrentNoteTitle}.docx"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var content = NotionMarkdownHelper.FlowDocumentToMarkdown(NotionEditorBox.Document);
                    var tempNote = new NoteDocument
                    {
                        Title = _viewModel.CurrentNoteTitle,
                        Content = content,
                        FolderPath = _viewModel.CurrentNote?.FolderPath ?? string.Empty,
                        FullPath = _viewModel.CurrentNote?.FullPath ?? string.Empty
                    };

                    await DocxExportHelper.ExportToDocxAsync(tempNote, sfd.FileName, _settingsService.Settings.NotesRoot);

                    ConfirmDialog.ShowMessage(this, "Export Success", $"Successfully exported note to Microsoft Word format:\n{sfd.FileName}", "OK", _viewModel.IsDarkMode);
                }
                catch (Exception ex)
                {
                    ConfirmDialog.ShowMessage(this, "Export Failed", $"Could not export Word file:\n{ex.Message}", "OK", _viewModel.IsDarkMode);
                }
            }
        }

        private static string CombineTitleAndBody(string title, string body)
        {
            if (string.IsNullOrWhiteSpace(title)) return body;
            if (body.StartsWith("# ")) return body;
            return $"# {title}\n\n{body}";
        }

        #region Search Bar Interactions & Popup

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_viewModel.SearchTerm) && _viewModel.SearchResults.Count > 0)
            {
                _viewModel.HasSearchResults = true;
            }
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _viewModel.ClearSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && SearchResultsPopup != null && SearchResultsPopup.IsOpen && _viewModel.SearchResults.Count > 0)
            {
                SearchResultsListBox.Focus();
                if (SearchResultsListBox.SelectedIndex < 0)
                {
                    SearchResultsListBox.SelectedIndex = 0;
                }
                var item = SearchResultsListBox.ItemContainerGenerator.ContainerFromIndex(SearchResultsListBox.SelectedIndex) as ListBoxItem;
                item?.Focus();
                e.Handled = true;
            }
        }

        private void SearchResultsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _viewModel.HasSearchResults = false;
                SearchBox.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && SearchResultsListBox.SelectedItem is SearchResult result)
            {
                _viewModel.NavigateToSearchResult(result);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && SearchResultsListBox.SelectedIndex <= 0)
            {
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text.Length;
                e.Handled = true;
            }
        }

        private void SearchResultsListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindVisualChild<ScrollViewer>(SearchResultsListBox);
            if (sv == null) return;

            // Dampen vertical scroll speed: smooth ~48px per wheel notch
            double offsetChange = (e.Delta / 120.0) * 48.0;
            double targetVertical = Math.Clamp(sv.VerticalOffset - offsetChange, 0, sv.ScrollableHeight);
            sv.ScrollToVerticalOffset(targetVertical);
            e.Handled = true;
        }

        public void RepositionSearchPopup()
        {
            if (SearchResultsPopup != null && SearchResultsPopup.IsOpen)
            {
                var offset = SearchResultsPopup.HorizontalOffset;
                SearchResultsPopup.HorizontalOffset = offset + 0.0001;
                SearchResultsPopup.HorizontalOffset = offset;
            }
        }

        private static bool IsDescendantOf(DependencyObject? node, DependencyObject? parent)
        {
            if (node == null || parent == null) return false;
            DependencyObject? current = node;
            while (current != null)
            {
                if (current == parent) return true;
                current = (current is Visual or System.Windows.Media.Media3D.Visual3D)
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return false;
        }

        #endregion
    }
}
