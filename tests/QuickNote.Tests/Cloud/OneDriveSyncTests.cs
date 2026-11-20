using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Infrastructure.Cloud;
using QuickNote.Infrastructure.Interfaces;
using Xunit;

namespace QuickNote.Tests.Cloud
{
    public class OneDriveSyncTests : IDisposable
    {
        private readonly string _testBaseDir;
        private readonly FakeSettingsService _settingsService;
        private readonly FakeNoteRepository _noteRepository;
        private readonly FakeFileWatcher _fileWatcher;
        private readonly OneDriveSyncService _syncService;

        public OneDriveSyncTests()
        {
            _testBaseDir = Path.Combine(Path.GetTempPath(), "QuickNote_SyncTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testBaseDir);

            _settingsService = new FakeSettingsService();
            _noteRepository = new FakeNoteRepository(Path.Combine(_testBaseDir, "LocalNotes"));
            _fileWatcher = new FakeFileWatcher(_noteRepository.NotesRoot);

            _syncService = new OneDriveSyncService(_settingsService, _noteRepository, _fileWatcher);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testBaseDir))
                {
                    Directory.Delete(_testBaseDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void DetectOneDriveAccounts_ShouldNotThrowAndReturnValidAccounts()
        {
            var accounts = _syncService.DetectOneDriveAccounts();

            Assert.NotNull(accounts);
            foreach (var account in accounts)
            {
                Assert.False(string.IsNullOrWhiteSpace(account.AccountName));
                Assert.False(string.IsNullOrWhiteSpace(account.UserFolder));
                Assert.False(string.IsNullOrWhiteSpace(account.DefaultNotesPath));
            }
        }

        [Fact]
        public async Task EnableOneDriveSyncAsync_ShouldMigrateNotesAndRepointRoot()
        {
            // Setup source notes
            var sourceNotes = Path.Combine(_testBaseDir, "LocalNotes");
            Directory.CreateDirectory(sourceNotes);
            await File.WriteAllTextAsync(Path.Combine(sourceNotes, "Note1.md"), "# Note 1");
            var subFolder = Path.Combine(sourceNotes, "Work");
            Directory.CreateDirectory(subFolder);
            await File.WriteAllTextAsync(Path.Combine(subFolder, "Project.md"), "# Project notes");

            // Fake OneDrive user folder
            var fakeOneDrive = Path.Combine(_testBaseDir, "FakeOneDrive");
            Directory.CreateDirectory(fakeOneDrive);

            var account = new OneDriveAccountInfo
            {
                AccountName = "OneDrive - Test",
                UserFolder = fakeOneDrive,
                IsBusiness = false
            };

            var success = await _syncService.EnableOneDriveSyncAsync(account, migrateExistingNotes: true, currentNotesRoot: sourceNotes);

            Assert.True(success);
            Assert.True(_settingsService.Settings.IsOneDriveSyncEnabled);
            Assert.Equal("OneDrive - Test", _settingsService.Settings.SelectedOneDriveAccountName);

            var expectedNotesRoot = Path.Combine(fakeOneDrive, "QuickNote");
            Assert.Equal(expectedNotesRoot, _settingsService.Settings.OneDriveNotesPath);
            Assert.Equal(expectedNotesRoot, _noteRepository.NotesRoot);
            Assert.Equal(expectedNotesRoot, _fileWatcher.WatchedPath);

            // Verify migration
            Assert.True(File.Exists(Path.Combine(expectedNotesRoot, "Note1.md")));
            Assert.True(File.Exists(Path.Combine(expectedNotesRoot, "Work", "Project.md")));
            Assert.Equal("# Note 1", await File.ReadAllTextAsync(Path.Combine(expectedNotesRoot, "Note1.md")));
        }

        [Fact]
        public async Task DisableOneDriveSyncAsync_ShouldRevertNotesRoot()
        {
            var fakeOneDrive = Path.Combine(_testBaseDir, "FakeOneDrive");
            Directory.CreateDirectory(fakeOneDrive);

            var account = new OneDriveAccountInfo
            {
                AccountName = "OneDrive - Test",
                UserFolder = fakeOneDrive,
                IsBusiness = false
            };

            var localNotes = Path.Combine(_testBaseDir, "LocalNotes");
            Directory.CreateDirectory(localNotes);

            await _syncService.EnableOneDriveSyncAsync(account, false, localNotes);
            Assert.True(_settingsService.Settings.IsOneDriveSyncEnabled);

            // Now disable
            var success = await _syncService.DisableOneDriveSyncAsync(localNotes);

            Assert.True(success);
            Assert.False(_settingsService.Settings.IsOneDriveSyncEnabled);
            Assert.Equal(localNotes, _settingsService.Settings.NotesRoot);
            Assert.Equal(localNotes, _noteRepository.NotesRoot);
            Assert.Equal(localNotes, _fileWatcher.WatchedPath);

            var status = _syncService.GetSyncStatus();
            Assert.Equal(CloudSyncState.Disabled, status.State);
        }

        [Fact]
        public async Task EnableOneDriveSyncAsync_WithAssetsDirectory_MigratesAssetsAndImages()
        {
            var sourceNotes = Path.Combine(_testBaseDir, "SourceWorkNotes");
            var sourceAssets = Path.Combine(sourceNotes, ".assets");
            Directory.CreateDirectory(sourceAssets);

            var testImage = Path.Combine(sourceAssets, "diagram.png");
            await File.WriteAllTextAsync(testImage, "fake_png_data");
            await File.WriteAllTextAsync(Path.Combine(sourceNotes, "Architecture.md"), "# Architecture\n![Diagram](.assets/diagram.png)");

            var targetOneDrive = Path.Combine(_testBaseDir, "PersonalOneDrive");
            Directory.CreateDirectory(targetOneDrive);

            var account = new OneDriveAccountInfo
            {
                AccountName = "OneDrive - Personal",
                UserFolder = targetOneDrive,
                IsBusiness = false
            };

            var success = await _syncService.EnableOneDriveSyncAsync(account, migrateExistingNotes: true, currentNotesRoot: sourceNotes);
            Assert.True(success);

            var expectedTargetRoot = Path.Combine(targetOneDrive, "QuickNote");
            Assert.True(File.Exists(Path.Combine(expectedTargetRoot, "Architecture.md")));
            Assert.True(File.Exists(Path.Combine(expectedTargetRoot, ".assets", "diagram.png")));
            Assert.Equal("fake_png_data", await File.ReadAllTextAsync(Path.Combine(expectedTargetRoot, ".assets", "diagram.png")));
        }

        private class FakeSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new AppSettings();

            public Task LoadSettingsAsync() => Task.CompletedTask;
            public Task SaveSettingsAsync() => Task.CompletedTask;
        }

        private class FakeNoteRepository : INoteRepository
        {
            public string NotesRoot { get; private set; }

            public FakeNoteRepository(string initialRoot)
            {
                NotesRoot = initialRoot;
            }

            public void UpdateNotesRoot(string newNotesRoot)
            {
                NotesRoot = newNotesRoot;
            }

            public Task<NoteDocument?> GetNoteByIdAsync(Guid id) => Task.FromResult<NoteDocument?>(null);
            public Task<NoteDocument?> GetNoteByPathAsync(string fullPath) => Task.FromResult<NoteDocument?>(null);
            public Task<IEnumerable<NoteDocument>> GetAllNotesAsync() => Task.FromResult<IEnumerable<NoteDocument>>(new List<NoteDocument>());
            public Task AddNoteAsync(NoteDocument note) => Task.CompletedTask;
            public Task UpdateNoteAsync(NoteDocument note) => Task.CompletedTask;
            public Task DeleteNoteAsync(Guid id) => Task.CompletedTask;
            public Task<bool> NoteExistsAsync(Guid id) => Task.FromResult(false);
            public Task<List<NoteDocument>?> GetCachedNotesAsync() => Task.FromResult<List<NoteDocument>?>(null);
            public Task SaveNoteFastAsync(NoteDocument note, string? oldFullPath = null) => Task.CompletedTask;
            public Task FlushPendingWritesAsync() => Task.CompletedTask;
            public Task<IEnumerable<NoteDocument>> ReconcileDiskNotesAsync() => GetAllNotesAsync();
        }

        private class FakeFileWatcher : IFileWatcher
        {
            public string? WatchedPath { get; private set; }
            public bool IsEnabled { get; private set; }

            public FakeFileWatcher(string path)
            {
                WatchedPath = path;
            }

            public void Start() => IsEnabled = true;
            public void Stop() => IsEnabled = false;

            public void UpdateWatchDirectory(string newPath)
            {
                WatchedPath = newPath;
            }
        }
    }
}
