using System;
using System.Text.Json;
using QuickNote.Core.Models;
using QuickNote.Core.Themes;
using Xunit;

namespace QuickNote.Tests.Themes
{
    public class ThemeTemplateServiceTests
    {
        [Fact]
        public void BuiltInPresets_ShouldContainAllFourPresetsWithValidTokens()
        {
            var presets = ThemeTemplateService.GetBuiltInPresets();
            Assert.Equal(4, presets.Count);

            var androidDark = ThemeTemplateService.CreateAndroidDarkTheme();
            Assert.Equal("android-dark", androidDark.Id);
            Assert.Equal("Dark", androidDark.BaseVariant);
            Assert.True(androidDark.IsBuiltIn);
            Assert.Equal("#0b0e14", androidDark.GetColor("AppBackground"));
            Assert.Equal("#3390ec", androidDark.AccentColor);

            var androidLight = ThemeTemplateService.CreateAndroidLightTheme();
            Assert.Equal("android-light", androidLight.Id);
            Assert.Equal("Light", androidLight.BaseVariant);
            Assert.True(androidLight.IsBuiltIn);
            Assert.Equal("#f0f4f9", androidLight.GetColor("AppBackground"));
            Assert.Equal("#2481cc", androidLight.AccentColor);

            var windowsDark = ThemeTemplateService.CreateWindowsDarkTheme();
            Assert.Equal("windows-dark", windowsDark.Id);
            Assert.Equal("Dark", windowsDark.BaseVariant);
            Assert.True(windowsDark.IsBuiltIn);
            Assert.Equal("#11141c", windowsDark.GetColor("AppBackground"));
            Assert.Equal("#2563eb", windowsDark.AccentColor);

            var windowsLight = ThemeTemplateService.CreateWindowsLightTheme();
            Assert.Equal("windows-light", windowsLight.Id);
            Assert.Equal("Light", windowsLight.BaseVariant);
            Assert.True(windowsLight.IsBuiltIn);
            Assert.Equal("#ffffff", windowsLight.GetColor("AppBackground"));
            Assert.Equal("#2563eb", windowsLight.AccentColor);
        }

        [Fact]
        public void ParseThemeJson_FullSchemaFormat_ShouldParseSuccessfully()
        {
            var json = @"{
                ""id"": ""nord-frost"",
                ""name"": ""Nord Frost"",
                ""description"": ""Arctic themed palette"",
                ""baseVariant"": ""Dark"",
                ""accentColor"": ""#88c0d0"",
                ""previewBackground"": ""#2e3440"",
                ""colors"": {
                    ""AppBackground"": ""#2e3440"",
                    ""AppHeaderBackground"": ""#3b4252"",
                    ""AppTextPrimary"": ""#eceff4"",
                    ""Accent"": ""#88c0d0""
                }
            }";

            var (success, message, theme) = ThemeTemplateService.ParseThemeJson(json);

            Assert.True(success);
            Assert.NotNull(theme);
            Assert.Equal("nord-frost", theme.Id);
            Assert.Equal("Nord Frost", theme.Name);
            Assert.Equal("Dark", theme.BaseVariant);
            Assert.Equal("#88c0d0", theme.AccentColor);
            Assert.Equal("#2e3440", theme.GetColor("AppBackground"));
            Assert.Equal("#3b4252", theme.GetColor("AppHeaderBackground"));
            // Fallback tokens filled from Dark template
            Assert.False(string.IsNullOrEmpty(theme.GetColor("AppSidebarBackground")));
        }

        [Fact]
        public void ParseThemeJson_FlatDictionaryFormat_ShouldParseAndFillFallbacks()
        {
            var json = @"{
                ""name"": ""Solarized Dark"",
                ""baseVariant"": ""Dark"",
                ""Accent"": ""#268bd2"",
                ""AppBackground"": ""#002b36"",
                ""AppHeaderBackground"": ""#073642""
            }";

            var (success, message, theme) = ThemeTemplateService.ParseThemeJson(json);

            Assert.True(success);
            Assert.NotNull(theme);
            Assert.Equal("Solarized Dark", theme.Name);
            Assert.Equal("Dark", theme.BaseVariant);
            Assert.Equal("#268bd2", theme.AccentColor);
            Assert.Equal("#002b36", theme.GetColor("AppBackground"));
            Assert.Equal("#073642", theme.GetColor("AppHeaderBackground"));
            Assert.False(string.IsNullOrEmpty(theme.GetColor("AppSidebarBackground")));
        }

        [Fact]
        public void ParseThemeJson_InvalidJson_ShouldReturnFailure()
        {
            var (success1, _, _) = ThemeTemplateService.ParseThemeJson("");
            Assert.False(success1);

            var (success2, _, _) = ThemeTemplateService.ParseThemeJson("{ not valid json }");
            Assert.False(success2);
        }

        [Fact]
        public void ExportToJson_ShouldProduceValidReParseableJson()
        {
            var original = ThemeTemplateService.CreateWindowsDarkTheme();
            var exportedJson = ThemeTemplateService.ExportToJson(original);

            Assert.False(string.IsNullOrWhiteSpace(exportedJson));

            var (success, _, reimported) = ThemeTemplateService.ParseThemeJson(exportedJson);
            Assert.True(success);
            Assert.NotNull(reimported);
            Assert.Equal(original.Name, reimported.Name);
            Assert.Equal(original.BaseVariant, reimported.BaseVariant);
            Assert.Equal(original.AccentColor, reimported.AccentColor);
            Assert.Equal(original.GetColor("AppBackground"), reimported.GetColor("AppBackground"));
        }
    }
}
