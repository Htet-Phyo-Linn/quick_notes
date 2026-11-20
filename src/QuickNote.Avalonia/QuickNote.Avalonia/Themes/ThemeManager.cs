using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Core.Themes;

namespace QuickNote.Avalonia.Themes
{
    public class ThemeManager : INotifyPropertyChanged
    {
        public static ThemeManager Instance { get; } = new();

        private ISettingsService? _settingsService;
        private ThemeSchema _currentTheme;

        public event Action<ThemeSchema>? ThemeChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ThemeSchema> AvailableThemes { get; } = new();

        public ThemeSchema CurrentTheme
        {
            get => _currentTheme;
            private set
            {
                if (_currentTheme != value)
                {
                    _currentTheme = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTheme)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrentThemeDark)));
                }
            }
        }

        public bool IsCurrentThemeDark => CurrentTheme?.BaseVariant?.Equals("Dark", StringComparison.OrdinalIgnoreCase) ?? true;

        private ThemeManager()
        {
            _currentTheme = ThemeTemplateService.CreateAndroidDarkTheme();
        }

        public void Initialize(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            AvailableThemes.Clear();

            // 1. Built-in presets: Android and Main/Windows branch schemas
            var androidDark = ThemeTemplateService.CreateAndroidDarkTheme();
            var androidLight = ThemeTemplateService.CreateAndroidLightTheme();
            var windowsDark = ThemeTemplateService.CreateWindowsDarkTheme();
            var windowsLight = ThemeTemplateService.CreateWindowsLightTheme();

            AvailableThemes.Add(androidDark);
            AvailableThemes.Add(androidLight);
            AvailableThemes.Add(windowsDark);
            AvailableThemes.Add(windowsLight);

            // 2. Load custom imported themes from user settings
            if (_settingsService?.Settings?.CustomThemes != null)
            {
                foreach (var customTheme in _settingsService.Settings.CustomThemes)
                {
                    if (!AvailableThemes.Any(t => string.Equals(t.Id, customTheme.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        customTheme.IsBuiltIn = false;
                        AvailableThemes.Add(customTheme);
                    }
                }
            }

            // 3. Determine active theme from settings
            string activeId = _settingsService?.Settings?.CurrentThemeId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(activeId))
            {
                // Fallback from legacy MobileThemeStyle
                var legacy = _settingsService?.Settings?.MobileThemeStyle;
                if (string.Equals(legacy, "Windows", StringComparison.OrdinalIgnoreCase))
                {
                    activeId = "windows-dark";
                }
                else if (string.Equals(legacy, "Light", StringComparison.OrdinalIgnoreCase))
                {
                    activeId = "android-light";
                }
                else
                {
                    activeId = "android-dark";
                }
            }

            var targetTheme = AvailableThemes.FirstOrDefault(t => string.Equals(t.Id, activeId, StringComparison.OrdinalIgnoreCase))
                           ?? androidDark;

            ApplyTheme(targetTheme, save: false);
        }

        public void ApplyTheme(ThemeSchema theme, bool save = true)
        {
            if (theme == null) return;
            CurrentTheme = theme;

            if (Application.Current != null)
            {
                // Set Avalonia requested theme variant
                var variant = theme.BaseVariant.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                    ? ThemeVariant.Dark
                    : ThemeVariant.Light;
                Application.Current.RequestedThemeVariant = variant;

                // Inject and override dynamic color brush resources
                foreach (var (key, hex) in theme.Colors)
                {
                    if (Color.TryParse(hex, out var color))
                    {
                        Application.Current.Resources[key] = new SolidColorBrush(color);
                    }
                }

                // Accent brushes
                if (Color.TryParse(theme.AccentColor, out var accentColor))
                {
                    Application.Current.Resources["Accent"] = new SolidColorBrush(accentColor);
                    Application.Current.Resources["AppAccent"] = new SolidColorBrush(accentColor);
                    var accentFg = theme.GetColor("AccentForeground", "#ffffff");
                    if (Color.TryParse(accentFg, out var fgColor))
                    {
                        Application.Current.Resources["AccentForeground"] = new SolidColorBrush(fgColor);
                        Application.Current.Resources["AppAccentForeground"] = new SolidColorBrush(fgColor);
                    }
                }

                // Propagate to all open desktop windows
                if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var window in desktop.Windows)
                    {
                        window.RequestedThemeVariant = variant;
                        foreach (var (key, hex) in theme.Colors)
                        {
                            if (Color.TryParse(hex, out var color))
                            {
                                window.Resources[key] = new SolidColorBrush(color);
                            }
                        }
                        if (Color.TryParse(theme.AccentColor, out var accColor))
                        {
                            window.Resources["Accent"] = new SolidColorBrush(accColor);
                            window.Resources["AppAccent"] = new SolidColorBrush(accColor);
                            var accentFg = theme.GetColor("AccentForeground", "#ffffff");
                            if (Color.TryParse(accentFg, out var fgColor))
                            {
                                window.Resources["AccentForeground"] = new SolidColorBrush(fgColor);
                                window.Resources["AppAccentForeground"] = new SolidColorBrush(fgColor);
                            }
                        }
                    }
                }
            }

            // Persist setting
            if (_settingsService?.Settings != null)
            {
                _settingsService.Settings.CurrentThemeId = theme.Id;
                _settingsService.Settings.MobileThemeStyle = theme.Id.StartsWith("windows", StringComparison.OrdinalIgnoreCase) ? "Windows" : "Android";
                if (save)
                {
                    _ = _settingsService.SaveSettingsAsync();
                }
            }

            ThemeChanged?.Invoke(theme);
        }

        public void ToggleDarkLightMode()
        {
            if (CurrentTheme == null) return;

            string targetId;
            if (CurrentTheme.Id.Equals("android-dark", StringComparison.OrdinalIgnoreCase))
                targetId = "android-light";
            else if (CurrentTheme.Id.Equals("android-light", StringComparison.OrdinalIgnoreCase))
                targetId = "android-dark";
            else if (CurrentTheme.Id.Equals("windows-dark", StringComparison.OrdinalIgnoreCase))
                targetId = "windows-light";
            else if (CurrentTheme.Id.Equals("windows-light", StringComparison.OrdinalIgnoreCase))
                targetId = "windows-dark";
            else
            {
                // Custom theme: toggle between Dark and Light preset
                targetId = CurrentTheme.BaseVariant.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                    ? "android-light"
                    : "android-dark";
            }

            var next = AvailableThemes.FirstOrDefault(t => string.Equals(t.Id, targetId, StringComparison.OrdinalIgnoreCase));
            if (next != null)
            {
                ApplyTheme(next);
            }
        }

        public (bool Success, string Message, ThemeSchema? Theme) ImportThemeFromJson(string json)
        {
            var result = ThemeTemplateService.ParseThemeJson(json, AvailableThemes);
            if (!result.Success || result.Theme == null)
            {
                return result;
            }

            var theme = result.Theme;
            AvailableThemes.Add(theme);

            if (_settingsService?.Settings != null)
            {
                _settingsService.Settings.CustomThemes ??= new();
                _settingsService.Settings.CustomThemes.Add(theme);
            }

            ApplyTheme(theme);
            return (true, $"Imported and applied theme '{theme.Name}'.", theme);
        }

        public string ExportThemeToJson(ThemeSchema? theme = null)
        {
            var target = theme ?? CurrentTheme ?? ThemeTemplateService.CreateAndroidDarkTheme();
            return ThemeTemplateService.ExportToJson(target);
        }

        public void ResetToDefault()
        {
            var defaultTheme = AvailableThemes.FirstOrDefault(t => t.Id == "android-dark")
                            ?? ThemeTemplateService.CreateAndroidDarkTheme();
            ApplyTheme(defaultTheme);
        }

        public IBrush GetBrush(string colorKey, string fallbackHex = "#3390ec")
        {
            if (colorKey.Equals("AppAccent", StringComparison.OrdinalIgnoreCase) || colorKey.Equals("Accent", StringComparison.OrdinalIgnoreCase))
            {
                if (CurrentTheme != null && !string.IsNullOrWhiteSpace(CurrentTheme.AccentColor) && Color.TryParse(CurrentTheme.AccentColor, out var cThemeCol))
                {
                    return new SolidColorBrush(cThemeCol);
                }
                if (CurrentTheme != null && CurrentTheme.Colors.TryGetValue("Accent", out var hexAcc) && Color.TryParse(hexAcc, out var cHex))
                {
                    return new SolidColorBrush(cHex);
                }
                if (Application.Current != null && (Application.Current.Resources.TryGetResource("Accent", null, out var accRes) || Application.Current.Resources.TryGetResource("AppAccent", null, out accRes)))
                {
                    if (accRes is IBrush accB) return accB;
                }
                return new SolidColorBrush(Color.Parse("#3390ec"));
            }

            if (CurrentTheme != null && CurrentTheme.Colors.TryGetValue(colorKey, out var hex))
            {
                if (Color.TryParse(hex, out var col))
                {
                    return new SolidColorBrush(col);
                }
            }

            if (Application.Current != null && Application.Current.Resources.TryGetResource(colorKey, null, out var res))
            {
                if (res is IBrush b) return b;
            }

            if (Color.TryParse(fallbackHex, out var fbCol))
            {
                return new SolidColorBrush(fbCol);
            }

            return new SolidColorBrush(Color.Parse("#3390ec"));
        }

        // ── Preset Creators (forwarding to Core ThemeTemplateService) ─────────
        public static ThemeSchema CreateAndroidDarkTheme() => ThemeTemplateService.CreateAndroidDarkTheme();
        public static ThemeSchema CreateAndroidLightTheme() => ThemeTemplateService.CreateAndroidLightTheme();
        public static ThemeSchema CreateWindowsDarkTheme() => ThemeTemplateService.CreateWindowsDarkTheme();
        public static ThemeSchema CreateWindowsLightTheme() => ThemeTemplateService.CreateWindowsLightTheme();
    }
}
