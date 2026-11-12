using System;
using System.ComponentModel;
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
                CloseDrawer();
            }
        }

        private void OnEditorMarkdownChanged(string markdown)
        {
            if (_viewModel != null && _viewModel.EditorContent != markdown)
            {
                _viewModel.EditorContent = markdown;
            }
        }

        private void OnDrawerToggleClick(object? sender, RoutedEventArgs e)
        {
            ToggleDrawer();
        }

        private void OnDrawerScrimPressed(object? sender, PointerPressedEventArgs e)
        {
            CloseDrawer();
        }

        private void ToggleDrawer()
        {
            bool isVisible = DrawerPanel.IsVisible;
            SetDrawerVisible(!isVisible);
        }

        private void CloseDrawer()
        {
            SetDrawerVisible(false);
        }

        private void SetDrawerVisible(bool visible)
        {
            DrawerPanel.IsVisible = visible;
            DrawerScrim.IsVisible = visible;
        }

        private void OnSearchToggleClick(object? sender, RoutedEventArgs e)
        {
            MobileSearchBar.IsVisible = !MobileSearchBar.IsVisible;
        }

        private void OnMobileNodePressed(object? sender, PointerPressedEventArgs e)
        {
            if ((sender as Control)?.DataContext is FolderNode node && !node.IsDirectory)
            {
                _viewModel?.OpenNode(node);
                CloseDrawer();
            }
        }

        private void OnMobileNodeTapped(object? sender, TappedEventArgs e)
        {
            if ((sender as Control)?.DataContext is FolderNode node && !node.IsDirectory)
            {
                _viewModel?.OpenNode(node);
                CloseDrawer();
            }
        }

        private void OnInsertBoldClick(object? sender, RoutedEventArgs e) => AppendToEditor("**bold**");
        private void OnInsertItalicClick(object? sender, RoutedEventArgs e) => AppendToEditor("*italic*");
        private void OnInsertHeadingClick(object? sender, RoutedEventArgs e) => AppendToEditor("\n## Heading\n");
        private void OnInsertCodeClick(object? sender, RoutedEventArgs e) => AppendToEditor("`code`");
        private void OnInsertBulletClick(object? sender, RoutedEventArgs e) => AppendToEditor("\n- List item\n");
        private void OnInsertChecklistClick(object? sender, RoutedEventArgs e) => AppendToEditor("\n- [ ] Task\n");
        private void OnInsertQuoteClick(object? sender, RoutedEventArgs e) => AppendToEditor("\n> Quote\n");

        private void AppendToEditor(string text)
        {
            if (_viewModel == null || MobileNotionEditor == null) return;
            var current = MobileNotionEditor.GetMarkdown() ?? string.Empty;
            var updated = current + text;
            MobileNotionEditor.LoadMarkdown(updated, force: true);
            _viewModel.EditorContent = updated;
        }
    }
}
