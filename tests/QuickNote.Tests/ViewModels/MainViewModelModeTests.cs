using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using QuickNote.App.DependencyInjection;
using QuickNote.App.ViewModels;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Infrastructure.Interfaces;
using Xunit;

namespace QuickNote.Tests.ViewModels
{
    public class MainViewModelModeTests
    {
        private (MainViewModel vm, string tempDir) CreateViewModel()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "qn_mode_tests_" + Guid.NewGuid().ToString("N"));
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
                tempDir));

            var sp = services.BuildServiceProvider();
            var vm = sp.GetRequiredService<MainViewModel>();
            return (vm, tempDir);
        }

        [Fact]
        public void ReadingMode_Toggle_UpdatesCanEditAndCommands()
        {
            var (vm, tempDir) = CreateViewModel();
            try
            {
                Assert.False(vm.IsReadingMode);
                Assert.True(vm.CanEdit);

                vm.ToggleReadingModeCommand.Execute(null);

                Assert.True(vm.IsReadingMode);
                Assert.False(vm.CanEdit);

                vm.ToggleReadingModeCommand.Execute(null);

                Assert.False(vm.IsReadingMode);
                Assert.True(vm.CanEdit);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task SplitMode_Toggle_InitializesSecondaryNoteAndCloses()
        {
            var (vm, tempDir) = CreateViewModel();
            try
            {
                await Task.Delay(100);
                Assert.False(vm.IsSplitMode);

                var note = new NoteDocument
                {
                    Id = Guid.NewGuid(),
                    Title = "Test Note",
                    Content = "Hello World"
                };
                vm.CurrentNote = note;

                vm.ToggleSplitModeCommand.Execute(null);

                Assert.True(vm.IsSplitMode);
                Assert.NotNull(vm.SecondaryNote);
                Assert.Equal("Test Note", vm.SecondaryNoteTitle);
                Assert.Equal("Hello World", vm.SecondaryEditorContent);

                vm.CloseSplitCommand.Execute(null);

                Assert.False(vm.IsSplitMode);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
