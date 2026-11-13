using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.Views
{
    public partial class MobileMainView : UserControl
    {
        private MainViewModel? _viewModel;

        public MobileMainView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            KeyDown += OnViewKeyDown;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = DataContext as MainViewModel;

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;

                if (MobileNotionEditor != null)
                {
                    MobileNotionEditor.NotesRoot = _viewModel.NotesRoot;
                    MobileNotionEditor.CurrentNoteFolderPath = _viewModel.CurrentNote?.FolderPath;
                    MobileNotionEditor.CurrentNotePath = _viewModel.CurrentNote?.FullPath;
                    MobileNotionEditor.MarkdownChanged += OnEditorMarkdownChanged;

                    if (!string.IsNullOrEmpty(_viewModel.EditorContent))
                    {
                        MobileNotionEditor.LoadMarkdown(_viewModel.EditorContent, force: true);
                    }
                }

                // If a note is already selected on start, open editor, otherwise show explorer
                if (_viewModel.CurrentNote != null)
                {
                    ShowEditorView();
                }
                else
                {
                    ShowExplorerView();
                }
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null || MobileNotionEditor == null) return;

            if (e.PropertyName == nameof(MainViewModel.EditorContent))
            {
                if (MobileNotionEditor.GetMarkdown() != _viewModel.EditorContent)
                {
                    MobileNotionEditor.LoadMarkdown(_viewModel.EditorContent ?? string.Empty, force: true);
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.CurrentNote))
            {
                MobileNotionEditor.CurrentNoteFolderPath = _viewModel.CurrentNote?.FolderPath;
                MobileNotionEditor.CurrentNotePath = _viewModel.CurrentNote?.FullPath;
                MobileNotionEditor.LoadMarkdown(_viewModel.EditorContent ?? string.Empty, force: true);

                if (_viewModel.CurrentNote != null)
                {
                    ShowEditorView();
                }
            }
        }

        private void OnEditorMarkdownChanged(string markdown)
        {
            if (_viewModel != null && _viewModel.EditorContent != markdown)
            {
                _viewModel.EditorContent = markdown;
            }
        }

        public void ShowEditorView()
        {
            ExplorerView.IsVisible = false;
            EditorView.IsVisible = true;
            if (MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = true;
            }
        }

        public void ShowExplorerView()
        {
            EditorView.IsVisible = false;
            if (MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = false;
            }
            ExplorerView.IsVisible = true;
        }

        private async void OnBackToExplorerClick(object? sender, RoutedEventArgs e)
        {
            await SaveAndCloseEditorAsync();
        }

        private async Task SaveAndCloseEditorAsync()
        {
            if (MobileNotionEditor != null)
            {
                await MobileNotionEditor.FlushPendingChangesAsync();
            }
            if (_viewModel != null)
            {
                await _viewModel.SaveNoteAsync();
            }
            ShowExplorerView();
        }

        private async void OnNewNoteClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            await _viewModel.NewNoteAsync();
            ShowEditorView();
        }

        private void OnNodeItemTapped(object? sender, TappedEventArgs e)
        {
            if ((sender as Control)?.DataContext is FolderNode node)
            {
                if (node.IsDirectory)
                {
                    node.IsExpanded = !node.IsExpanded;
                }
                else
                {
                    _viewModel?.OpenNode(node);
                    ShowEditorView();
                }
            }
        }

        private async void OnDeleteNodeClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is FolderNode node && _viewModel != null)
            {
                await _viewModel.DeleteNode(node);
            }
        }

        private async void OnDeleteCurrentNoteClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                await _viewModel.Delete();
                ShowExplorerView();
            }
        }

        private void OnViewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back || e.Key == Key.Escape)
            {
                if (EditorView.IsVisible)
                {
                    e.Handled = true;
                    _ = SaveAndCloseEditorAsync();
                }
            }
        }

        // Formatting actions
        private async void OnActionBoldClick(object? sender, RoutedEventArgs e) => await ExecuteFormatAction("bold");
        private async void OnActionItalicClick(object? sender, RoutedEventArgs e) => await ExecuteFormatAction("italic");
        private async void OnActionH1Click(object? sender, RoutedEventArgs e) => await ExecuteFormatAction("h1");
        private async void OnActionH2Click(object? sender, RoutedEventArgs e) => await ExecuteFormatAction("h2");
        private async void OnActionBulletClick(object? sender, RoutedEventArgs e) => await ExecuteFormatAction("bullet");
        private async void OnActionNumberedClick(object? sender, RoutedEventArgs e) => await ExecuteFormatAction("number");
        private async void OnActionTodoClick(object? sender, RoutedEventArgs e) => await ExecuteFormatAction("todo");
        private async void OnActionCodeClick(object? sender, RoutedEventArgs e) => await ExecuteFormatAction("code");
        private async void OnActionQuoteClick(object? sender, RoutedEventArgs e) => await ExecuteFormatAction("quote");

        private async Task ExecuteFormatAction(string action)
        {
            if (MobileNotionEditor != null)
            {
                await MobileNotionEditor.ExecuteEditorActionAsync(action);
            }
        }
    }
}
