using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using QuickNote.Avalonia.Themes;
using QuickNote.Avalonia.ViewModels;

namespace QuickNote.Avalonia.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            ThemeManager.Instance.ThemeChanged += OnThemeChanged;
            UpdateActiveThemeBadges();
        }

        protected override void OnClosed(EventArgs e)
        {
            ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
            base.OnClosed(e);
        }

        private void OnThemeChanged(QuickNote.Core.Models.ThemeSchema theme)
        {
            UpdateActiveThemeBadges();
        }

        private void UpdateActiveThemeBadges()
        {
            var cur = ThemeManager.Instance.CurrentTheme?.Id ?? "android-dark";
            if (BadgeAndroidDark != null) BadgeAndroidDark.IsVisible = cur.Equals("android-dark", StringComparison.OrdinalIgnoreCase);
            if (BadgeAndroidLight != null) BadgeAndroidLight.IsVisible = cur.Equals("android-light", StringComparison.OrdinalIgnoreCase);
            if (BadgeWindowsDark != null) BadgeWindowsDark.IsVisible = cur.Equals("windows-dark", StringComparison.OrdinalIgnoreCase);
            if (BadgeWindowsLight != null) BadgeWindowsLight.IsVisible = cur.Equals("windows-light", StringComparison.OrdinalIgnoreCase);
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

        private void AndroidDarkCard_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var t = ThemeManager.Instance.AvailableThemes.FirstOrDefault(x => x.Id == "android-dark")
                 ?? ThemeManager.CreateAndroidDarkTheme();
            ThemeManager.Instance.ApplyTheme(t);
        }

        private void AndroidLightCard_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var t = ThemeManager.Instance.AvailableThemes.FirstOrDefault(x => x.Id == "android-light")
                 ?? ThemeManager.CreateAndroidLightTheme();
            ThemeManager.Instance.ApplyTheme(t);
        }

        private void WindowsDarkCard_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var t = ThemeManager.Instance.AvailableThemes.FirstOrDefault(x => x.Id == "windows-dark")
                 ?? ThemeManager.CreateWindowsDarkTheme();
            ThemeManager.Instance.ApplyTheme(t);
        }

        private void WindowsLightCard_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var t = ThemeManager.Instance.AvailableThemes.FirstOrDefault(x => x.Id == "windows-light")
                 ?? ThemeManager.CreateWindowsLightTheme();
            ThemeManager.Instance.ApplyTheme(t);
        }

        private async void ImportThemeButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel != null)
                {
                    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "Import QuickNote Theme JSON",
                        AllowMultiple = false,
                        FileTypeFilter = new[]
                        {
                            new FilePickerFileType("Theme JSON (*.json)") { Patterns = new[] { "*.json" } }
                        }
                    });

                    if (files != null && files.Count > 0)
                    {
                        using var stream = await files[0].OpenReadAsync();
                        using var reader = new StreamReader(stream);
                        var json = await reader.ReadToEndAsync();
                        var (success, msg, theme) = ThemeManager.Instance.ImportThemeFromJson(json);
                        if (TxtThemeStatus != null)
                        {
                            TxtThemeStatus.Text = msg;
                            TxtThemeStatus.IsVisible = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (TxtThemeStatus != null)
                {
                    TxtThemeStatus.Text = "Import failed: " + ex.Message;
                    TxtThemeStatus.IsVisible = true;
                }
            }
        }

        private async void ExportThemeButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var current = ThemeManager.Instance.CurrentTheme;
                if (current == null) return;

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel != null)
                {
                    var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Export Current Theme JSON",
                        DefaultExtension = "json",
                        SuggestedFileName = $"{current.Id}.json",
                        FileTypeChoices = new[]
                        {
                            new FilePickerFileType("Theme JSON (*.json)") { Patterns = new[] { "*.json" } }
                        }
                    });

                    if (file != null)
                    {
                        var json = ThemeManager.Instance.ExportThemeToJson(current);
                        using var stream = await file.OpenWriteAsync();
                        using var writer = new StreamWriter(stream);
                        await writer.WriteAsync(json);

                        if (TxtThemeStatus != null)
                        {
                            TxtThemeStatus.Text = $"Exported theme to {file.Name} successfully!";
                            TxtThemeStatus.IsVisible = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (TxtThemeStatus != null)
                {
                    TxtThemeStatus.Text = "Export failed: " + ex.Message;
                    TxtThemeStatus.IsVisible = true;
                }
            }
        }

        private void ResetThemesButton_Click(object? sender, RoutedEventArgs e)
        {
            var androidDark = ThemeManager.Instance.AvailableThemes.FirstOrDefault(x => x.Id == "android-dark")
                           ?? ThemeManager.CreateAndroidDarkTheme();
            ThemeManager.Instance.ApplyTheme(androidDark);
            if (TxtThemeStatus != null)
            {
                TxtThemeStatus.Text = "Reset to default Android Dark theme.";
                TxtThemeStatus.IsVisible = true;
            }
        }

        private async void OpenGoogleLogin_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                var loginWin = new GoogleDriveLoginWindow(vm.CloudSyncService, vm);
                await loginWin.ShowDialog(this);
                vm.ShowProfileDialog();
            }
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
