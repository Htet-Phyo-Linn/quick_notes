using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using QuickNote.Avalonia.Themes;

namespace QuickNote.Avalonia.Views
{
    public partial class MobileMainView
    {
        // ── Settings, Themes & Dictionary Modal ───────────────────────────
        private int _settingsTabIndex = 0; // 0 = Themes, 1 = Dictionary
        public int SettingsTabIndex
        {
            get => _settingsTabIndex;
            set
            {
                if (_settingsTabIndex != value)
                {
                    _settingsTabIndex = value;
                    OnPropertyChanged(nameof(SettingsTabIndex));
                    OnPropertyChanged(nameof(ThemeTabBackgroundBrush));
                    OnPropertyChanged(nameof(ThemeTabForegroundBrush));
                    OnPropertyChanged(nameof(DictionaryTabBackgroundBrush));
                    OnPropertyChanged(nameof(DictionaryTabForegroundBrush));
                    if (SettingsThemesPanel != null) SettingsThemesPanel.IsVisible = (_settingsTabIndex == 0);
                    if (SettingsDictionaryPanel != null) SettingsDictionaryPanel.IsVisible = (_settingsTabIndex == 1);
                }
            }
        }

        public IBrush ThemeTabBackgroundBrush => (_settingsTabIndex == 0) ? AccentBrush : Brushes.Transparent;
        public IBrush ThemeTabForegroundBrush => (_settingsTabIndex == 0) ? Brushes.White : TextSecondaryBrush;
        public IBrush DictionaryTabBackgroundBrush => (_settingsTabIndex == 1) ? AccentBrush : Brushes.Transparent;
        public IBrush DictionaryTabForegroundBrush => (_settingsTabIndex == 1) ? Brushes.White : TextSecondaryBrush;

        public bool IsSpellCheckEnabled
        {
            get => _viewModel?.IsSpellCheckEnabled ?? true;
            set
            {
                if (_viewModel != null && _viewModel.IsSpellCheckEnabled != value)
                {
                    _viewModel.IsSpellCheckEnabled = value;
                    if (_viewModel.SettingsService != null)
                    {
                        _viewModel.SettingsService.Settings.IsSpellCheckEnabled = value;
                        _ = _viewModel.SettingsService.SaveSettingsAsync();
                    }
                    if (MobileNotionEditor != null)
                    {
                        _ = MobileNotionEditor.SetSpellCheckAsync(value);
                    }
                    OnPropertyChanged(nameof(IsSpellCheckEnabled));
                }
            }
        }

        public string IgnoredWordsCountText => $"{_viewModel?.IgnoredWords.Count ?? 0} words";

        public void OnOpenSettingsModalClick(object? sender, RoutedEventArgs e)
        {
            if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = false;
            }
            SettingsTabIndex = 0;
            RebuildIgnoredWordsChips();
            if (SettingsModal != null)
            {
                SettingsModal.IsVisible = true;
            }
        }

        public void OnCloseSettingsModalClick(object? sender, RoutedEventArgs e)
        {
            if (SettingsModal != null)
            {
                SettingsModal.IsVisible = false;
            }
            if (NoteEditorScreen?.IsVisible == true && MobileNotionEditor != null)
            {
                MobileNotionEditor.IsVisible = true;
            }
        }

        public void OnSettingsModalContentPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
        }

        public void OnSettingsTabThemesClick(object? sender, TappedEventArgs e)
        {
            SettingsTabIndex = 0;
        }

        public void OnSettingsTabDictionaryClick(object? sender, TappedEventArgs e)
        {
            SettingsTabIndex = 1;
            RebuildIgnoredWordsChips();
        }

        public void OnSelectAndroidThemeClick(object? sender, TappedEventArgs e)
        {
            var targetId = IsDarkMode ? "android-dark" : "android-light";
            var t = ThemeManager.Instance.AvailableThemes.FirstOrDefault(x => x.Id == targetId)
                 ?? ThemeManager.Instance.AvailableThemes.FirstOrDefault(x => x.Id == "android-dark");
            if (t != null) ThemeManager.Instance.ApplyTheme(t);
        }

        public void OnSelectWindowsThemeClick(object? sender, TappedEventArgs e)
        {
            var targetId = IsDarkMode ? "windows-dark" : "windows-light";
            var t = ThemeManager.Instance.AvailableThemes.FirstOrDefault(x => x.Id == targetId)
                 ?? ThemeManager.Instance.AvailableThemes.FirstOrDefault(x => x.Id == "windows-dark");
            if (t != null) ThemeManager.Instance.ApplyTheme(t);
        }

        public async void OnImportThemeClick(object? sender, TappedEventArgs e)
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
                        ShowToast(msg, isProgress: false, isError: !success);
                        if (success) ApplyThemeVariantUpdates();
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToast("Import failed: " + ex.Message, isProgress: false, isError: true);
            }
        }

        public void OnEditorOpenSettingsClick(object? sender, RoutedEventArgs e)
        {
            if (NoteEditorMenuModal != null)
            {
                NoteEditorMenuModal.IsVisible = false;
            }
            OnOpenSettingsModalClick(sender, e);
        }

        public void RebuildIgnoredWordsChips()
        {
            if (IgnoredWordsChipsPanel == null) return;
            IgnoredWordsChipsPanel.Children.Clear();

            var words = _viewModel?.IgnoredWords ?? new ObservableCollection<string>();
            if (words.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "No custom ignored words yet. Type above and click Add.",
                    FontSize = 11,
                    Foreground = TextSecondaryBrush,
                    FontStyle = FontStyle.Italic,
                    Margin = new Thickness(4)
                };
                IgnoredWordsChipsPanel.Children.Add(emptyText);
                OnPropertyChanged(nameof(IgnoredWordsCountText));
                return;
            }

            foreach (var word in words)
            {
                var chip = CreateIgnoredWordChip(word);
                IgnoredWordsChipsPanel.Children.Add(chip);
            }
            OnPropertyChanged(nameof(IgnoredWordsCountText));
        }

        private Control CreateIgnoredWordChip(string word)
        {
            var border = new Border
            {
                Background = ChipUnselectedBackgroundBrush,
                BorderBrush = GlassBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 4, 6, 4),
                Margin = new Thickness(3, 3, 3, 3)
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };

            var label = new TextBlock
            {
                Text = word,
                FontSize = 12,
                Foreground = TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };

            var removeBtn = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = new PathIcon
                {
                    Data = (StreamGeometry)Resources["IconClose"]!,
                    Width = 10,
                    Height = 10,
                    Foreground = new SolidColorBrush(Color.Parse("#ef4444"))
                }
            };
            removeBtn.Click += async (s, e) =>
            {
                if (_viewModel != null)
                {
                    await _viewModel.RemoveIgnoredWord(word);
                    if (MobileNotionEditor != null)
                    {
                        await MobileNotionEditor.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
                    }
                    RebuildIgnoredWordsChips();
                }
            };

            stack.Children.Add(label);
            stack.Children.Add(removeBtn);
            border.Child = stack;
            return border;
        }

        public async void OnAddIgnoredWordClick(object? sender, RoutedEventArgs e)
        {
            await AddCurrentIgnoredWordAsync();
        }

        private void OnAddIgnoredWordKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = AddCurrentIgnoredWordAsync();
                e.Handled = true;
            }
        }

        private async Task AddCurrentIgnoredWordAsync()
        {
            if (TxtAddIgnoredWord == null || _viewModel == null) return;
            var word = TxtAddIgnoredWord.Text?.Trim();
            if (string.IsNullOrWhiteSpace(word)) return;

            await _viewModel.AddIgnoredWord(word);
            TxtAddIgnoredWord.Text = string.Empty;

            if (MobileNotionEditor != null)
            {
                await MobileNotionEditor.SetIgnoredWordsAsync(_viewModel.IgnoredWords);
            }
            RebuildIgnoredWordsChips();
        }
    }
}
