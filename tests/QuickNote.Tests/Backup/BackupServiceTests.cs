using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QuickNote.Infrastructure.Services;
using Xunit;

namespace QuickNote.Tests.Backup
{
    public class BackupServiceTests : IDisposable
    {
        private readonly string _tempNotesDir;
        private readonly BackupService _backupService;

        public BackupServiceTests()
        {
            _tempNotesDir = Path.Combine(Path.GetTempPath(), "QuickNote_BackupTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempNotesDir);
            _backupService = new BackupService(_tempNotesDir);
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
        public async Task CreateManualBackupAsync_CreatesValidSnapshotFile()
        {
            // Arrange
            var noteFile = Path.Combine(_tempNotesDir, "MyNote.md");
            await File.WriteAllTextAsync(noteFile, "# My Note Content\nVersion 1");

            // Act
            var backupPath = await _backupService.CreateManualBackupAsync(noteFile);

            // Assert
            Assert.NotNull(backupPath);
            Assert.True(File.Exists(backupPath));
            Assert.Contains(".backups", backupPath);
            Assert.Contains("MyNote_manual_", backupPath);
            Assert.EndsWith(".bak", backupPath);

            var content = await File.ReadAllTextAsync(backupPath);
            Assert.Equal("# My Note Content\nVersion 1", content);
        }

        [Fact]
        public async Task GetBackupGroupsAsync_DiscoversAndGroupsVersionsCorrectly()
        {
            // Arrange
            var note1 = Path.Combine(_tempNotesDir, "DocA.md");
            var note2 = Path.Combine(_tempNotesDir, "SubFolder", "DocB.md");
            Directory.CreateDirectory(Path.GetDirectoryName(note2)!);

            await File.WriteAllTextAsync(note1, "Doc A V1");
            await File.WriteAllTextAsync(note2, "Doc B V1");

            // Create backups for DocA
            await _backupService.CreateManualBackupAsync(note1);
            await Task.Delay(10);
            await File.WriteAllTextAsync(note1, "Doc A V2");
            await _backupService.CreateManualBackupAsync(note1);

            // Create backup for DocB
            await _backupService.CreateManualBackupAsync(note2);

            // Act
            var groups = await _backupService.GetBackupGroupsAsync();

            // Assert
            Assert.Equal(2, groups.Count);

            var groupA = groups.FirstOrDefault(g => g.NoteTitle == "DocA");
            Assert.NotNull(groupA);
            Assert.Equal(2, groupA.VersionCount);
            Assert.Equal(2, groupA.Versions.Count);
            Assert.True(groupA.Versions[0].Timestamp >= groupA.Versions[1].Timestamp);

            var groupB = groups.FirstOrDefault(g => g.NoteTitle == "DocB");
            Assert.NotNull(groupB);
            Assert.Equal(1, groupB.VersionCount);
        }

        [Fact]
        public async Task ReadBackupContentAsync_ReadsFullText()
        {
            // Arrange
            var noteFile = Path.Combine(_tempNotesDir, "ReadTest.md");
            var expectedContent = "# Special Characters\n- Item 1\n- Item 2\n✨ Unicode rocket 🚀";
            await File.WriteAllTextAsync(noteFile, expectedContent);

            var backupPath = await _backupService.CreateManualBackupAsync(noteFile);
            Assert.NotNull(backupPath);

            // Act
            var readBack = await _backupService.ReadBackupContentAsync(backupPath);

            // Assert
            Assert.Equal(expectedContent, readBack);
        }

        [Fact]
        public async Task RestoreBackupAsync_RestoresNoteAndCreatesSafetyPreRestoreBackup()
        {
            // Arrange
            var noteFile = Path.Combine(_tempNotesDir, "RestoreTest.md");
            await File.WriteAllTextAsync(noteFile, "Initial Content V1");

            var v1BackupPath = await _backupService.CreateManualBackupAsync(noteFile);
            Assert.NotNull(v1BackupPath);

            // Now mutate note to V2
            await File.WriteAllTextAsync(noteFile, "Mutated Content V2 (accidental deletion)");

            // Act: Restore V1
            var success = await _backupService.RestoreBackupAsync(noteFile, v1BackupPath);

            // Assert
            Assert.True(success);
            var currentContent = await File.ReadAllTextAsync(noteFile);
            Assert.Equal("Initial Content V1", currentContent);

            // Verify that a pre-restore safety backup was created
            var backupDir = Path.Combine(_tempNotesDir, ".backups");
            var allBackups = Directory.GetFiles(backupDir, "RestoreTest_*.bak");
            Assert.True(allBackups.Length >= 2, "Expected safety pre-restore backup to be created before overwriting.");
        }

        [Fact]
        public async Task PruneBackupsAsync_RemovesExcessVersionsAboveMax()
        {
            // Arrange
            var noteFile = Path.Combine(_tempNotesDir, "PruneTest.md");
            await File.WriteAllTextAsync(noteFile, "Content");

            // Create 5 backups
            for (int i = 0; i < 5; i++)
            {
                await _backupService.CreateManualBackupAsync(noteFile);
                await Task.Delay(20);
            }

            var backupDir = Path.Combine(_tempNotesDir, ".backups");
            var beforePrune = Directory.GetFiles(backupDir, "PruneTest_*.bak");
            Assert.Equal(5, beforePrune.Length);

            // Act: Prune down to max 2 per note
            var prunedCount = await _backupService.PruneBackupsAsync(maxPerNote: 2, maxAgeDays: 30);

            // Assert
            Assert.Equal(3, prunedCount);
            var afterPrune = Directory.GetFiles(backupDir, "PruneTest_*.bak");
            Assert.Equal(2, afterPrune.Length);
        }
    }
}
