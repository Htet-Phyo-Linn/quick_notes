using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickNote.App.DependencyInjection;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Interfaces;
using QuickNote.Infrastructure.Interfaces;
using QuickNote.Infrastructure.Services;
using Xunit;

namespace QuickNote.Tests.Backup
{
    public class BackupHistoryViewModelTests : IDisposable
    {
        private readonly string _tempNotesDir;
        private readonly BackupService _backupService;
        private readonly QuickNote.Avalonia.ViewModels.MainViewModel _mainViewModel;

        public BackupHistoryViewModelTests()
        {
            _tempNotesDir = Path.Combine(Path.GetTempPath(), "QuickNote_VMBackupTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempNotesDir);

            var services = new ServiceCollection();
            services.AddInfrastructure(_tempNotesDir);
            var sp = services.BuildServiceProvider();

            _backupService = new BackupService(_tempNotesDir);
            _mainViewModel = new QuickNote.Avalonia.ViewModels.MainViewModel(
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
                _tempNotesDir,
                backupService: _backupService);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempNotesDir))
                {
                    Directory.Delete(_tempNotesDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task ChangingSelectedGroup_UpdatesVersionsAndPreviewToNewNote()
        {
            // Arrange
            var backupDir = Path.Combine(_tempNotesDir, ".backups");
            Directory.CreateDirectory(backupDir);

            // Create files for NoteA (e.g. Essentials CMD)
            var fileA1 = Path.Combine(backupDir, "NoteA_20261103_131000.bak");
            var fileA2 = Path.Combine(backupDir, "NoteA_20261103_130900.bak");
            await File.WriteAllTextAsync(fileA1, "Content of NoteA Revision 1");
            await File.WriteAllTextAsync(fileA2, "Content of NoteA Revision 2");

            // Create files for NoteB (e.g. Untitled)
            var fileB1 = Path.Combine(backupDir, "NoteB_20261104_140000.bak");
            var fileB2 = Path.Combine(backupDir, "NoteB_20261104_130000.bak");
            await File.WriteAllTextAsync(fileB1, "Content of NoteB Revision 1");
            await File.WriteAllTextAsync(fileB2, "Content of NoteB Revision 2");

            var vm = new BackupHistoryViewModel(_backupService, _mainViewModel);
            await vm.InitializeAsync();

            // Assert NoteB was initially selected (since its timestamp is newer: Nov 4)
            Assert.NotNull(vm.SelectedGroup);
            Assert.Equal("NoteB", vm.SelectedGroup.NoteTitle);
            Assert.Equal(2, vm.Versions.Count);
            Assert.NotNull(vm.SelectedVersion);
            Assert.Equal(fileB1, vm.SelectedVersion.BackupFilePath);

            // Wait a brief moment for async preview load
            for (int i = 0; i < 20 && string.IsNullOrEmpty(vm.PreviewContent); i++)
            {
                await Task.Delay(25);
            }
            Assert.Equal("Content of NoteB Revision 1", vm.PreviewContent);

            // Act: User selects NoteA in the left ListBox (Avalonia sets SelectedGroup)
            var groupA = vm.FilteredGroups.First(g => g.NoteTitle == "NoteA");
            vm.SelectedGroup = groupA;

            // Assert: Versions must now be NoteA's revisions!
            Assert.Equal(2, vm.Versions.Count);
            Assert.All(vm.Versions, v => Assert.Equal("NoteA", v.NoteTitle));
            Assert.NotNull(vm.SelectedVersion);
            Assert.Equal(fileA1, vm.SelectedVersion.BackupFilePath);

            // Wait for preview load
            for (int i = 0; i < 20 && vm.PreviewContent != "Content of NoteA Revision 1"; i++)
            {
                await Task.Delay(25);
            }
            Assert.Equal("Content of NoteA Revision 1", vm.PreviewContent);
            Assert.Contains("NoteA", vm.StatusMessage);
        }

        [Fact]
        public async Task ChangingSelectedVersion_UpdatesPreviewContent()
        {
            // Arrange
            var backupDir = Path.Combine(_tempNotesDir, ".backups");
            Directory.CreateDirectory(backupDir);

            var file1 = Path.Combine(backupDir, "DocX_20261103_150000.bak");
            var file2 = Path.Combine(backupDir, "DocX_20261103_140000.bak");
            await File.WriteAllTextAsync(file1, "DocX Newest Version Content");
            await File.WriteAllTextAsync(file2, "DocX Older Version Content");

            var vm = new BackupHistoryViewModel(_backupService, _mainViewModel);
            await vm.InitializeAsync("DocX");

            Assert.Equal(2, vm.Versions.Count);
            Assert.Equal(file1, vm.SelectedVersion?.BackupFilePath);

            // Act: Select the older version in timeline
            vm.SelectedVersion = vm.Versions[1];

            // Assert: Preview content updates to the older version's text
            for (int i = 0; i < 20 && vm.PreviewContent != "DocX Older Version Content"; i++)
            {
                await Task.Delay(25);
            }
            Assert.Equal("DocX Older Version Content", vm.PreviewContent);
        }

        [Fact]
        public async Task SearchText_FiltersGroupsAndUpdatesSelection()
        {
            // Arrange
            var backupDir = Path.Combine(_tempNotesDir, ".backups");
            Directory.CreateDirectory(backupDir);

            await File.WriteAllTextAsync(Path.Combine(backupDir, "Alpha_20261101_100000.bak"), "Alpha content");
            await File.WriteAllTextAsync(Path.Combine(backupDir, "Beta_20261101_100000.bak"), "Beta content");

            var vm = new BackupHistoryViewModel(_backupService, _mainViewModel);
            await vm.InitializeAsync();

            Assert.Equal(2, vm.FilteredGroups.Count);

            // Act: Filter by "Beta"
            vm.SearchText = "Beta";

            // Assert
            Assert.Single(vm.FilteredGroups);
            Assert.Equal("Beta", vm.FilteredGroups[0].NoteTitle);
            Assert.NotNull(vm.SelectedGroup);
            Assert.Equal("Beta", vm.SelectedGroup.NoteTitle);
        }
    }
}
