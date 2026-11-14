using System;
using System.IO;
using System.Threading.Tasks;
using QuickNote.Core.Models;
using QuickNote.Infrastructure.Cloud;
using Xunit;

namespace QuickNote.Tests.Cloud
{
    public class SyncManifestManagerTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly SyncManifestManager _manager;

        public SyncManifestManagerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "QuickNote_SyncTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _manager = new SyncManifestManager(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task LoadManifestAsync_WhenFileDoesNotExist_ReturnsEmptyManifest()
        {
            var manifest = await _manager.LoadManifestAsync();

            Assert.NotNull(manifest);
            Assert.Empty(manifest.Entries);
        }

        [Fact]
        public async Task SaveAndLoadManifestAsync_PersistsEntriesCorrectly()
        {
            _manager.Manifest.QuickNoteFolderId = "folder-12345";
            _manager.Manifest.Entries["test.md"] = new SyncFileEntry
            {
                RelativePath = "test.md",
                LocalHash = "ABC123HASH",
                RemoteId = "remote-999",
                RemoteETag = "etag-1",
                Status = SyncStatus.Synced
            };

            await _manager.SaveManifestAsync();

            var reloadedManager = new SyncManifestManager(_tempDir);
            var reloaded = await reloadedManager.LoadManifestAsync();

            Assert.Equal("folder-12345", reloaded.QuickNoteFolderId);
            Assert.True(reloaded.Entries.ContainsKey("test.md"));
            Assert.Equal("ABC123HASH", reloaded.Entries["test.md"].LocalHash);
            Assert.Equal("remote-999", reloaded.Entries["test.md"].RemoteId);
            Assert.Equal(SyncStatus.Synced, reloaded.Entries["test.md"].Status);
        }

        [Fact]
        public void ComputeHash_ProducesConsistentHexOutput()
        {
            var text = "# Test Note\nHello world";
            var hash1 = SyncManifestManager.ComputeTextHash(text);
            var hash2 = SyncManifestManager.ComputeTextHash(text);

            Assert.False(string.IsNullOrEmpty(hash1));
            Assert.Equal(hash1, hash2);

            var filePath = Path.Combine(_tempDir, "sample.md");
            File.WriteAllText(filePath, text);
            var fileHash = SyncManifestManager.ComputeFileHash(filePath);

            Assert.Equal(hash1, fileHash);
        }

        [Fact]
        public void ScanLocalChanges_DetectsNewAndModifiedNotes()
        {
            var note1 = Path.Combine(_tempDir, "note1.md");
            File.WriteAllText(note1, "Content 1");

            var pending = _manager.ScanLocalChanges(_tempDir);

            Assert.Contains("note1.md", pending);
            Assert.Equal(SyncStatus.PendingUpload, _manager.Manifest.Entries["note1.md"].Status);

            // Record successful upload
            _manager.RecordSuccessfulUpload("note1.md", "id-1", "etag-1", DateTime.UtcNow,
                _manager.Manifest.Entries["note1.md"].LocalHash, File.GetLastWriteTimeUtc(note1));
            Assert.Equal(SyncStatus.Synced, _manager.Manifest.Entries["note1.md"].Status);

            // Rescan without modifications should yield no pending uploads
            var pendingAfter = _manager.ScanLocalChanges(_tempDir);
            Assert.DoesNotContain("note1.md", pendingAfter);

            // Modify the file
            File.WriteAllText(note1, "Content 1 Modified with new text");
            File.SetLastWriteTimeUtc(note1, DateTime.UtcNow.AddMinutes(5));

            var pendingAfterMod = _manager.ScanLocalChanges(_tempDir);
            Assert.Contains("note1.md", pendingAfterMod);
            Assert.Equal(SyncStatus.PendingUpload, _manager.Manifest.Entries["note1.md"].Status);
        }

        [Fact]
        public void CreateConflictCopy_CreatesSafeFileWithoutOverwritingOriginal()
        {
            var originalPath = Path.Combine(_tempDir, "my_project.md");
            File.WriteAllText(originalPath, "Local modifications");

            var conflictPath = SyncManifestManager.CreateConflictCopy(originalPath, "Remote cloud modifications", DateTime.UtcNow);

            Assert.True(File.Exists(originalPath));
            Assert.Equal("Local modifications", File.ReadAllText(originalPath));

            Assert.True(File.Exists(conflictPath));
            Assert.Contains("conflict", Path.GetFileName(conflictPath));
            Assert.Equal("Remote cloud modifications", File.ReadAllText(conflictPath));
        }
    }
}
