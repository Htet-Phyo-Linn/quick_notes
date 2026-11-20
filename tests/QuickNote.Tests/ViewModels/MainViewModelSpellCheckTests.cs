using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickNote.Avalonia.DependencyInjection;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Interfaces;
using QuickNote.Infrastructure.Interfaces;
using Xunit;

namespace QuickNote.Tests.ViewModels
{
    public class MainViewModelSpellCheckTests
    {
        private (MainViewModel vm, ISettingsService settings, string tempDir) CreateViewModel()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "qn_spell_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var services = new ServiceCollection();
            services.AddInfrastructure(tempDir);
            services.AddTransient<MainViewModel>(sp => new MainViewModel(
                sp.GetRequiredService<INoteRepository>(),
                sp.GetRequiredService<ISearchEngine>(),
                sp.GetRequiredService<IOutlineService>(),
                sp.GetRequiredService<ITagService>(),
                sp.GetRequiredService<IFolderService>(),
                sp.GetRequiredService<IRecoveryService>(),
                sp.GetRequiredService<ITrashBinService>(),
                sp.GetRequiredService<IFileWatcher>(),
                sp.GetRequiredService<ICloudSyncService>(),
                sp.GetRequiredService<ISettingsService>(),
                tempDir,
                sp.GetService<IMarkdownParser>(),
                sp.GetService<IBackupService>()));

            var sp = services.BuildServiceProvider();
            var vm = sp.GetRequiredService<MainViewModel>();
            var settings = sp.GetRequiredService<ISettingsService>();
            settings.Settings.IgnoredWords.Clear();
            vm.IgnoredWords.Clear();
            return (vm, settings, tempDir);
        }

        [Fact]
        public async Task AddIgnoredWord_AddsToCollectionAndPersistsToSettings()
        {
            var (vm, settings, tempDir) = CreateViewModel();
            try
            {
                await vm.AddIgnoredWord("CyberArk");

                Assert.Contains("CyberArk", vm.IgnoredWords);
                Assert.Contains("CyberArk", settings.Settings.IgnoredWords);

                // Adding same word with different casing or surrounding punctuation should not duplicate
                await vm.AddIgnoredWord("\"cyberark.\"");
                Assert.Single(vm.IgnoredWords);

                // Adding another word
                await vm.AddIgnoredWord("WinRM");
                Assert.Equal(2, vm.IgnoredWords.Count);
                Assert.Contains("WinRM", vm.IgnoredWords);
                Assert.Contains("WinRM", settings.Settings.IgnoredWords);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task RemoveIgnoredWord_RemovesFromCollectionAndSettings()
        {
            var (vm, settings, tempDir) = CreateViewModel();
            try
            {
                await vm.AddIgnoredWord("LDAPS");
                await vm.AddIgnoredWord("PowerShell");
                Assert.Equal(2, vm.IgnoredWords.Count);

                await vm.RemoveIgnoredWord("ldaps"); // case-insensitive remove
                Assert.Single(vm.IgnoredWords);
                Assert.DoesNotContain("LDAPS", vm.IgnoredWords);
                Assert.DoesNotContain("LDAPS", settings.Settings.IgnoredWords);
                Assert.Contains("PowerShell", vm.IgnoredWords);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task IgnoredWords_PersistAcrossViewModelReloads()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "qn_spell_persist_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var services1 = new ServiceCollection();
                services1.AddInfrastructure(tempDir);
                services1.AddTransient<MainViewModel>(sp => new MainViewModel(
                    sp.GetRequiredService<INoteRepository>(),
                    sp.GetRequiredService<ISearchEngine>(),
                    sp.GetRequiredService<IOutlineService>(),
                    sp.GetRequiredService<ITagService>(),
                    sp.GetRequiredService<IFolderService>(),
                    sp.GetRequiredService<IRecoveryService>(),
                    sp.GetRequiredService<ITrashBinService>(),
                    sp.GetRequiredService<IFileWatcher>(),
                    sp.GetRequiredService<ICloudSyncService>(),
                    sp.GetRequiredService<ISettingsService>(),
                    tempDir,
                    sp.GetService<IMarkdownParser>(),
                    sp.GetService<IBackupService>()));

                var sp1 = services1.BuildServiceProvider();
                var vm1 = sp1.GetRequiredService<MainViewModel>();
                await vm1.AddIgnoredWord("Kubernetes");
                await vm1.AddIgnoredWord("kubectl");

                // Now simulate reopening the app by creating a new ServiceProvider & ViewModel pointing to the same tempDir
                var services2 = new ServiceCollection();
                services2.AddInfrastructure(tempDir);
                services2.AddTransient<MainViewModel>(sp => new MainViewModel(
                    sp.GetRequiredService<INoteRepository>(),
                    sp.GetRequiredService<ISearchEngine>(),
                    sp.GetRequiredService<IOutlineService>(),
                    sp.GetRequiredService<ITagService>(),
                    sp.GetRequiredService<IFolderService>(),
                    sp.GetRequiredService<IRecoveryService>(),
                    sp.GetRequiredService<ITrashBinService>(),
                    sp.GetRequiredService<IFileWatcher>(),
                    sp.GetRequiredService<ICloudSyncService>(),
                    sp.GetRequiredService<ISettingsService>(),
                    tempDir,
                    sp.GetService<IMarkdownParser>(),
                    sp.GetService<IBackupService>()));

                var sp2 = services2.BuildServiceProvider();
                var vm2 = sp2.GetRequiredService<MainViewModel>();

                Assert.Contains("Kubernetes", vm2.IgnoredWords);
                Assert.Contains("kubectl", vm2.IgnoredWords);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task ToggleSpellCheck_UpdatesPropertyAndSettings()
        {
            var (vm, settings, tempDir) = CreateViewModel();
            try
            {
                var initial = vm.IsSpellCheckEnabled;
                await vm.ToggleSpellCheck();

                Assert.Equal(!initial, vm.IsSpellCheckEnabled);
                Assert.Equal(!initial, settings.Settings.IsSpellCheckEnabled);

                await vm.ToggleSpellCheck();
                Assert.Equal(initial, vm.IsSpellCheckEnabled);
                Assert.Equal(initial, settings.Settings.IsSpellCheckEnabled);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
