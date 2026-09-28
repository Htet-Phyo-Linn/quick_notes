using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickNote.App.ViewModels;

namespace QuickNote.App
{
    public partial class MainWindow
    {
        private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is NoteTabViewModel tab)
            {
                _viewModel.ActiveTab = tab;
                e.Handled = true;
            }
        }

        private async void TabCloseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is NoteTabViewModel tab)
            {
                await _viewModel.CloseTabAsync(tab);
                e.Handled = true;
            }
        }

        private async void NewTabBtn_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.NewNoteAsync();
        }

        private void TabScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                if (e.Delta < 0)
                    sv.LineRight();
                else
                    sv.LineLeft();
                e.Handled = true;
            }
        }
    }
}
