using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using QuickNote.Avalonia.ViewModels;

namespace QuickNote.Avalonia.Views
{
    public partial class MoveToWindow : Window
    {
        private readonly MainViewModel? _viewModel;
        private readonly List<FolderDestinationItem> _allFolders = new();
        private readonly ObservableCollection<FolderDestinationItem> _filteredFolders = new();
        private bool _isConfirming;

        public MoveToWindow()
        {
            InitializeComponent();
        }

        public MoveToWindow(MainViewModel viewModel) : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;
            TitleText.Text = string.IsNullOrWhiteSpace(viewModel.MoveDialogTitle)
                ? "Move to Folder"
                : viewModel.MoveDialogTitle;

            _allFolders.AddRange(viewModel.AvailableFolders);
            FoldersListBox.ItemsSource = _filteredFolders;
            ApplyFilter();

            KeyDown += MoveToWindow_KeyDown;
        }

        private void ApplyFilter()
        {
            var query = SearchBox.Text?.Trim() ?? string.Empty;
            _filteredFolders.Clear();

            var matches = string.IsNullOrEmpty(query)
                ? _allFolders
                : _allFolders.Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var m in matches)
            {
                _filteredFolders.Add(m);
            }

            if (_viewModel != null)
            {
                if (_viewModel.SelectedDestinationFolder != null && _filteredFolders.Contains(_viewModel.SelectedDestinationFolder))
                {
                    FoldersListBox.SelectedItem = _viewModel.SelectedDestinationFolder;
                }
                else
                {
                    var first = _filteredFolders.FirstOrDefault();
                    FoldersListBox.SelectedItem = first;
                    _viewModel.SelectedDestinationFolder = first;
                }
            }

            MoveHereButton.IsEnabled = FoldersListBox.SelectedItem != null;
        }

        private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void FoldersListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_viewModel != null && FoldersListBox.SelectedItem is FolderDestinationItem item)
            {
                _viewModel.SelectedDestinationFolder = item;
            }
            MoveHereButton.IsEnabled = FoldersListBox.SelectedItem != null;
        }

        private async void FoldersListBox_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (FoldersListBox.SelectedItem is FolderDestinationItem)
            {
                await ConfirmAndCloseAsync();
            }
        }

        private async void MoveHereButton_Click(object? sender, RoutedEventArgs e)
        {
            await ConfirmAndCloseAsync();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            _viewModel?.CancelMove();
            Close();
        }

        private async void MoveToWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _viewModel?.CancelMove();
                Close();
            }
            else if (e.Key == Key.Enter && FoldersListBox.SelectedItem != null)
            {
                e.Handled = true;
                await ConfirmAndCloseAsync();
            }
        }

        private async Task ConfirmAndCloseAsync()
        {
            if (_isConfirming || _viewModel == null) return;
            if (FoldersListBox.SelectedItem is FolderDestinationItem item)
            {
                _viewModel.SelectedDestinationFolder = item;
            }
            if (_viewModel.SelectedDestinationFolder == null) return;

            _isConfirming = true;
            MoveHereButton.IsEnabled = false;
            try
            {
                await _viewModel.ConfirmMove();
            }
            finally
            {
                Close();
            }
        }
    }
}
