using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Interfaces;

namespace QuickNote.Avalonia.Views
{
    public partial class BackupHistoryWindow : Window
    {
        private BackupHistoryViewModel? _viewModel;

        public BackupHistoryWindow()
        {
            InitializeComponent();
        }

        public BackupHistoryWindow(IBackupService backupService, MainViewModel mainViewModel, string? preferredNoteTitle = null) : this()
        {
            _viewModel = new BackupHistoryViewModel(backupService, mainViewModel);
            DataContext = _viewModel;
            _ = _viewModel.InitializeAsync(preferredNoteTitle);
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
