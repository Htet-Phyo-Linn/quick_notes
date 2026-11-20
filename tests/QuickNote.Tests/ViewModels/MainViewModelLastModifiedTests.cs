using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickNote.Avalonia.DependencyInjection;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Infrastructure.Interfaces;
using Xunit;

namespace QuickNote.Tests.ViewModels
{
    public class MainViewModelLastModifiedTests
    {
        private (MainViewModel vm, string tempDir) CreateViewModel()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "qn_modified_tests_" + Guid.NewGuid().ToString("N"));
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
            return (vm, tempDir);
        }

        [Fact]
        public void WhenCurrentNoteIsNull_HasCurrentNoteLastModifiedIsFalse()
        {
            var (vm, tempDir) = CreateViewModel();
            try
            {
                vm.CurrentNote = null;
                Assert.False(vm.HasCurrentNoteLastModified);
                Assert.Equal(string.Empty, vm.CurrentNoteLastModifiedText);
                Assert.Equal(string.Empty, vm.CurrentNoteLastModifiedFullText);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void WhenCurrentNoteIsSet_FormatsDateAndTimeAccurately()
        {
            var (vm, tempDir) = CreateViewModel();
            try
            {
                var fixedTime = new DateTime(2026, 11, 16, 9, 30, 45, DateTimeKind.Local);
                var note = new NoteDocument
                {
                    Id = Guid.NewGuid(),
                    Title = "CPM deployment plan",
                    Content = "# CPM deployment plan",
                    LastModified = fixedTime
                };

                vm.CurrentNote = note;

                Assert.True(vm.HasCurrentNoteLastModified);
                Assert.Contains("Last modified:", vm.CurrentNoteLastModifiedText);
                Assert.Contains(fixedTime.ToString("MMM d, yyyy"), vm.CurrentNoteLastModifiedText);
                Assert.Contains(fixedTime.ToString("h:mm tt"), vm.CurrentNoteLastModifiedText);

                // Tooltip text check
                Assert.Contains("Click to view version history & backups", vm.CurrentNoteLastModifiedFullText);
                Assert.Contains(fixedTime.ToString("MMMM d, yyyy"), vm.CurrentNoteLastModifiedFullText);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void OpenBackupHistoryCommand_SetsPreferredTitleToCurrentNote()
        {
            var (vm, tempDir) = CreateViewModel();
            try
            {
                var note = new NoteDocument
                {
                    Id = Guid.NewGuid(),
                    Title = "CyberArk CBS Note",
                    Content = "Secret vault content",
                    LastModified = DateTime.Now
                };
                vm.CurrentNote = note;

                vm.OpenBackupHistoryCommand.Execute(null);

                Assert.Equal("CyberArk CBS Note", vm.BackupHistoryPreferredTitle);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void RightCurrentNote_SplitView_FormatsDateAndTimeAccurately()
        {
            var (vm, tempDir) = CreateViewModel();
            try
            {
                var fixedTime = new DateTime(2026, 10, 25, 16, 15, 0, DateTimeKind.Local);
                var note = new NoteDocument
                {
                    Id = Guid.NewGuid(),
                    Title = "Split Secondary Note",
                    Content = "Secondary pane content",
                    LastModified = fixedTime
                };

                vm.RightCurrentNote = note;

                Assert.True(vm.HasRightCurrentNoteLastModified);
                Assert.Contains("Last modified:", vm.RightCurrentNoteLastModifiedText);
                Assert.Contains(fixedTime.ToString("MMM d, yyyy"), vm.RightCurrentNoteLastModifiedText);
                Assert.Contains(fixedTime.ToString("h:mm tt"), vm.RightCurrentNoteLastModifiedText);

                vm.OpenRightBackupHistoryCommand.Execute(null);
                Assert.Equal("Split Secondary Note", vm.BackupHistoryPreferredTitle);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
