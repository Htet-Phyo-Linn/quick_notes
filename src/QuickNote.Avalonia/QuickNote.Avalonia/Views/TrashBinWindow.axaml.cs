using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Interfaces;

namespace QuickNote.Avalonia.Views
{
    public class TrashItemItemViewModel
    {
        public TrashItem Item { get; }
        public string Title => Item.Title;
        public string OriginalPath => Item.OriginalPath;
        public string DeletedAtText => Item.DeletedAt.ToString("yyyy-MM-dd hh:mm tt");
        public bool IsDirectory => Item.IsDirectory;

        public TrashItemItemViewModel(TrashItem item)
        {
            Item = item;
        }
    }

    public partial class TrashBinWindow : Window
    {
        private readonly ITrashBinService? _trashBinService;
        private readonly MainViewModel? _mainViewModel;
        private readonly List<TrashItemItemViewModel> _allItems = new();
        private readonly ObservableCollection<TrashItemItemViewModel> _filteredItems = new();

        public TrashBinWindow()
        {
            InitializeComponent();
        }

        public TrashBinWindow(ITrashBinService trashBinService, MainViewModel mainViewModel) : this()
        {
            _trashBinService = trashBinService;
            _mainViewModel = mainViewModel;
            TrashListBox.ItemsSource = _filteredItems;
            _ = LoadItemsAsync();
        }

        private async Task LoadItemsAsync()
        {
            if (_trashBinService == null) return;

            try
            {
                var items = await _trashBinService.GetTrashItemsAsync();
                _allItems.Clear();
                foreach (var it in items)
                {
                    _allItems.Add(new TrashItemItemViewModel(it));
                }
                ApplyFilter();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TrashBinWindow.LoadItemsAsync] {ex.Message}");
            }
        }

        private void ApplyFilter()
        {
            var query = SearchBox.Text?.Trim() ?? string.Empty;
            _filteredItems.Clear();

            var matches = string.IsNullOrEmpty(query)
                ? _allItems
                : _allItems.Where(x => x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                       x.OriginalPath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var m in matches)
            {
                _filteredItems.Add(m);
            }

            bool hasItems = _allItems.Count > 0;
            EmptyPanel.IsVisible = !hasItems;
            TrashListBox.IsVisible = hasItems;
            EmptyTrashButton.IsEnabled = hasItems;
            RestoreSelectedButton.IsEnabled = hasItems;
            DeleteSelectedButton.IsEnabled = hasItems;
            ItemsCountText.Text = $"{_allItems.Count} {(_allItems.Count == 1 ? "item" : "items")}";
        }

        private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private async void ItemRestoreButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TrashItemItemViewModel vm && _trashBinService != null)
            {
                bool success = await _trashBinService.RestoreItemAsync(vm.Item);
                if (success)
                {
                    _allItems.Remove(vm);
                    ApplyFilter();
                    if (_mainViewModel != null)
                    {
                        await _mainViewModel.RefreshFolderTreeAsync();
                        await _mainViewModel.Refresh();
                    }
                }
            }
        }

        private async void ItemDeleteButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TrashItemItemViewModel vm && _trashBinService != null)
            {
                bool success = await _trashBinService.PermanentDeleteAsync(vm.Item);
                if (success)
                {
                    _allItems.Remove(vm);
                    ApplyFilter();
                }
            }
        }

        private async void RestoreSelectedButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_trashBinService == null) return;

            var selected = TrashListBox.SelectedItems?.OfType<TrashItemItemViewModel>().ToList() ?? new List<TrashItemItemViewModel>();
            if (selected.Count == 0) return;

            foreach (var item in selected)
            {
                bool success = await _trashBinService.RestoreItemAsync(item.Item);
                if (success)
                {
                    _allItems.Remove(item);
                }
            }

            ApplyFilter();
            if (_mainViewModel != null)
            {
                await _mainViewModel.RefreshFolderTreeAsync();
                await _mainViewModel.Refresh();
            }
        }

        private async void DeleteSelectedButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_trashBinService == null) return;

            var selected = TrashListBox.SelectedItems?.OfType<TrashItemItemViewModel>().ToList() ?? new List<TrashItemItemViewModel>();
            if (selected.Count == 0) return;

            foreach (var item in selected)
            {
                bool success = await _trashBinService.PermanentDeleteAsync(item.Item);
                if (success)
                {
                    _allItems.Remove(item);
                }
            }

            ApplyFilter();
        }

        private async void EmptyTrashButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_trashBinService == null || _allItems.Count == 0) return;

            await _trashBinService.EmptyTrashAsync();
            _allItems.Clear();
            ApplyFilter();
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
