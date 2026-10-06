using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using QuickNote.Avalonia.ViewModels;

namespace QuickNote.Avalonia.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void SwitchProfileButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                await vm.ConfirmProfileSwitch();
            }
            Close();
        }

        private void NewIgnoredWord_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is MainViewModel vm)
            {
                if (vm.AddIgnoredWordCommand.CanExecute(null))
                {
                    vm.AddIgnoredWordCommand.Execute(null);
                }
            }
        }
    }
}
