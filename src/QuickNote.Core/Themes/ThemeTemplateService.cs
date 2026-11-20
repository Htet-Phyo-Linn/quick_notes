using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using QuickNote.Core.Models;

namespace QuickNote.Core.Themes
{
    public static class ThemeTemplateService
    {
        public static ThemeSchema CreateAndroidDarkTheme()
        {
            var t = new ThemeSchema
            {
                Id = "android-dark",
                Name = "Android Dark (Liquid Glass)",
                Description = "Deep midnight space with frosted liquid glass and electric blue",
                BaseVariant = "Dark",
                IsBuiltIn = true,
                AccentColor = "#3390ec",
                PreviewBackground = "#0b0e14"
            };

            t.SetColor("AppBackground", "#0b0e14");
            t.SetColor("AppHeaderBackground", "#141a24");
            t.SetColor("AppSidebarBackground", "#111622");
            t.SetColor("AppCardBackground", "#161c26");
            t.SetColor("AppBorder", "#283548");
            t.SetColor("AppBorderSubtle", "#1e2634");
            t.SetColor("AppTextPrimary", "#f8fafc");
            t.SetColor("AppTextSecondary", "#8c97a8");
            t.SetColor("AppTextMuted", "#5a6678");
            t.SetColor("AppInputBackground", "#161c26");
            t.SetColor("AppButtonBackground", "#1e2634");
            t.SetColor("AppHover", "#1d2738");
            t.SetColor("AppActiveTab", "#161c26");
            t.SetColor("AppInactiveTab", "#0e131d");
            t.SetColor("AppStatusBarBackground", "#0d121b");
            t.SetColor("AppDivider", "#222d3e");
            t.SetColor("AppSelectedBackground", "#1a3454");
            t.SetColor("Accent", "#3390ec");
            t.SetColor("AppAccent", "#3390ec");
            t.SetColor("AccentForeground", "#ffffff");
            t.SetColor("EditorBackground", "#0b0e14");
            t.SetColor("EditorForeground", "#f8fafc");

            return t;
        }

        public static ThemeSchema CreateAndroidLightTheme()
        {
            var t = new ThemeSchema
            {
                Id = "android-light",
                Name = "Android Light (Daylight)",
                Description = "Clean daylight canvas with frosted white glass and electric blue",
                BaseVariant = "Light",
                IsBuiltIn = true,
                AccentColor = "#2481cc",
                PreviewBackground = "#f0f4f9"
            };

            t.SetColor("AppBackground", "#f0f4f9");
            t.SetColor("AppHeaderBackground", "#ffffff");
            t.SetColor("AppSidebarBackground", "#f8fafc");
            t.SetColor("AppCardBackground", "#ffffff");
            t.SetColor("AppBorder", "#e2e8f0");
            t.SetColor("AppBorderSubtle", "#cbd5e1");
            t.SetColor("AppTextPrimary", "#0f172a");
            t.SetColor("AppTextSecondary", "#475569");
            t.SetColor("AppTextMuted", "#64748b");
            t.SetColor("AppInputBackground", "#ffffff");
            t.SetColor("AppButtonBackground", "#e2e8f0");
            t.SetColor("AppHover", "#e2e8f0");
            t.SetColor("AppActiveTab", "#ffffff");
            t.SetColor("AppInactiveTab", "#f1f5f9");
            t.SetColor("AppStatusBarBackground", "#ffffff");
            t.SetColor("AppDivider", "#e2e8f0");
            t.SetColor("AppSelectedBackground", "#e0edff");
            t.SetColor("Accent", "#2481cc");
            t.SetColor("AppAccent", "#2481cc");
            t.SetColor("AccentForeground", "#ffffff");
            t.SetColor("EditorBackground", "#ffffff");
            t.SetColor("EditorForeground", "#0f172a");

            return t;
        }

        public static ThemeSchema CreateWindowsDarkTheme()
        {
            var t = new ThemeSchema
            {
                Id = "windows-dark",
                Name = "Windows Dark (Obsidian)",
                Description = "Classic dark slate desktop workspace with cobalt blue accent",
                BaseVariant = "Dark",
                IsBuiltIn = true,
                AccentColor = "#2563eb",
                PreviewBackground = "#11141c"
            };

            t.SetColor("AppBackground", "#11141c");
            t.SetColor("AppHeaderBackground", "#161b26");
            t.SetColor("AppSidebarBackground", "#141822");
            t.SetColor("AppCardBackground", "#161b26");
            t.SetColor("AppBorder", "#283244");
            t.SetColor("AppBorderSubtle", "#202736");
            t.SetColor("AppTextPrimary", "#f8fafc");
            t.SetColor("AppTextSecondary", "#94a3b8");
            t.SetColor("AppTextMuted", "#64748b");
            t.SetColor("AppInputBackground", "#161c26");
            t.SetColor("AppButtonBackground", "#1e2638");
            t.SetColor("AppHover", "#222b3d");
            t.SetColor("AppActiveTab", "#1e2638");
            t.SetColor("AppInactiveTab", "#141822");
            t.SetColor("AppStatusBarBackground", "#12151e");
            t.SetColor("AppDivider", "#283244");
            t.SetColor("AppSelectedBackground", "#1e3a5f");
            t.SetColor("Accent", "#2563eb");
            t.SetColor("AppAccent", "#2563eb");
            t.SetColor("AccentForeground", "#ffffff");
            t.SetColor("EditorBackground", "#11141c");
            t.SetColor("EditorForeground", "#f8fafc");

            return t;
        }

        public static ThemeSchema CreateWindowsLightTheme()
        {
            var t = new ThemeSchema
            {
                Id = "windows-light",
                Name = "Windows Light (Modern Slate)",
                Description = "Modern high-contrast slate white workspace with cobalt blue accent",
                BaseVariant = "Light",
                IsBuiltIn = true,
                AccentColor = "#2563eb",
                PreviewBackground = "#ffffff"
            };

            t.SetColor("AppBackground", "#ffffff");
            t.SetColor("AppHeaderBackground", "#f8fafc");
            t.SetColor("AppSidebarBackground", "#f1f5f9");
            t.SetColor("AppCardBackground", "#ffffff");
            t.SetColor("AppBorder", "#cbd5e1");
            t.SetColor("AppBorderSubtle", "#e2e8f0");
            t.SetColor("AppTextPrimary", "#0f172a");
            t.SetColor("AppTextSecondary", "#475569");
            t.SetColor("AppTextMuted", "#94a3b8");
            t.SetColor("AppInputBackground", "#ffffff");
            t.SetColor("AppButtonBackground", "#e2e8f0");
            t.SetColor("AppHover", "#e2e8f0");
            t.SetColor("AppActiveTab", "#ffffff");
            t.SetColor("AppInactiveTab", "#f1f5f9");
            t.SetColor("AppStatusBarBackground", "#f8fafc");
            t.SetColor("AppDivider", "#cbd5e1");
            t.SetColor("AppSelectedBackground", "#dbeafe");
            t.SetColor("Accent", "#2563eb");
            t.SetColor("AppAccent", "#2563eb");
            t.SetColor("AccentForeground", "#ffffff");
            t.SetColor("EditorBackground", "#ffffff");
            t.SetColor("EditorForeground", "#0f172a");

            return t;
        }

        public static List<ThemeSchema> GetBuiltInThemes()
        {
            return new List<ThemeSchema>
            {
                CreateAndroidDarkTheme(),
                CreateAndroidLightTheme(),
                CreateWindowsDarkTheme(),
                CreateWindowsLightTheme()
            };
        }

        public static List<ThemeSchema> GetBuiltInPresets() => GetBuiltInThemes();

        public static (bool Success, string Message, ThemeSchema? Theme) ParseThemeJson(string json, IEnumerable<ThemeSchema>? existingThemes = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                return (false, "Theme configuration is empty.", null);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var theme = new ThemeSchema();

                // Format A: Full ThemeSchema { "name": "...", "colors": { "AppBackground": "..." } }
                if (root.TryGetProperty("colors", out var colorsElement) && colorsElement.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                        theme.Id = idProp.GetString() ?? string.Empty;

                    if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                        theme.Name = nameProp.GetString() ?? string.Empty;

                    if (root.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
                        theme.Description = descProp.GetString() ?? string.Empty;

                    if (root.TryGetProperty("baseVariant", out var baseProp) && baseProp.ValueKind == JsonValueKind.String)
                        theme.BaseVariant = baseProp.GetString() ?? "Dark";

                    if (root.TryGetProperty("accentColor", out var accProp) && accProp.ValueKind == JsonValueKind.String)
                        theme.AccentColor = accProp.GetString() ?? "#3390ec";

                    if (root.TryGetProperty("previewBackground", out var prevProp) && prevProp.ValueKind == JsonValueKind.String)
                        theme.PreviewBackground = prevProp.GetString() ?? "#0b0e14";

                    foreach (var prop in colorsElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var colorStr = prop.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(colorStr))
                            {
                                theme.Colors[prop.Name] = colorStr.Trim();
                            }
                        }
                    }
                }
                else
                {
                    // Format B: Flat key-value colors dictionary
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var val = prop.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                if (prop.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                                    theme.Name = val;
                                else if (prop.Name.Equals("description", StringComparison.OrdinalIgnoreCase))
                                    theme.Description = val;
                                else if (prop.Name.Equals("baseVariant", StringComparison.OrdinalIgnoreCase))
                                    theme.BaseVariant = val;
                                else if (prop.Name.Equals("accentColor", StringComparison.OrdinalIgnoreCase) || prop.Name.Equals("Accent", StringComparison.OrdinalIgnoreCase))
                                {
                                    theme.AccentColor = val;
                                    theme.Colors["Accent"] = val;
                                }
                                else
                                {
                                    theme.Colors[prop.Name] = val;
                                }
                            }
                        }
                    }
                }

                // Fallback template for missing keys based on baseVariant
                var template = theme.BaseVariant.Equals("Light", StringComparison.OrdinalIgnoreCase)
                    ? CreateAndroidLightTheme()
                    : CreateAndroidDarkTheme();

                foreach (var (k, v) in template.Colors)
                {
                    if (!theme.Colors.ContainsKey(k))
                    {
                        theme.Colors[k] = v;
                    }
                }

                if (string.IsNullOrWhiteSpace(theme.Name))
                {
                    theme.Name = "Custom Theme " + Guid.NewGuid().ToString("N")[..4];
                }

                var existing = existingThemes?.ToList() ?? new List<ThemeSchema>();
                if (string.IsNullOrWhiteSpace(theme.Id) || existing.Any(t => string.Equals(t.Id, theme.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    theme.Id = "custom-" + Guid.NewGuid().ToString("N")[..8];
                }

                theme.IsBuiltIn = false;
                theme.PreviewBackground = theme.GetColor("AppBackground", template.PreviewBackground);
                theme.AccentColor = theme.GetColor("Accent", template.AccentColor);

                return (true, $"Theme '{theme.Name}' parsed successfully.", theme);
            }
            catch (Exception ex)
            {
                return (false, $"Invalid theme JSON format: {ex.Message}", null);
            }
        }

        public static string ExportToJson(ThemeSchema theme)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(theme, options);
        }
    }
}
