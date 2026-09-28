using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QuickNote.Infrastructure.Recovery;
using Xunit;

namespace QuickNote.Tests.Recovery
{
    public class RecoveryServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly RecoveryService _recoveryService;

        public RecoveryServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "QuickNote_RecoveryTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _recoveryService = new RecoveryService(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task CheckForRecoverableItemsAsync_EmptyDraft_DiscardedAutomatically()
        {
            var notePath = Path.Combine(_tempDir, "Note1.md");
            await _recoveryService.SaveDraftAsync(notePath, "   \n\r\n   ");

            var items = (await _recoveryService.CheckForRecoverableItemsAsync()).ToList();
            Assert.Empty(items);
        }

        [Fact]
        public async Task CheckForRecoverableItemsAsync_SavedNoteMatchingDraft_DiscardedAutomatically()
        {
            var notePath = Path.Combine(_tempDir, "Note1.md");
            await File.WriteAllTextAsync(notePath, "# My Title\n\nHello world body content");

            // Draft only contains the body
            await _recoveryService.SaveDraftAsync(notePath, "Hello world body content");

            var items = (await _recoveryService.CheckForRecoverableItemsAsync()).ToList();
            Assert.Empty(items);
        }

        [Fact]
        public async Task CheckForRecoverableItemsAsync_UnsavedNoteWithChanges_ReturnedForRecovery()
        {
            var notePath = Path.Combine(_tempDir, "Note1.md");
            await File.WriteAllTextAsync(notePath, "# My Title\n\nOriginal content");

            // Draft contains newer unsaved changes
            await _recoveryService.SaveDraftAsync(notePath, "Brand new unsaved content");

            var items = (await _recoveryService.CheckForRecoverableItemsAsync()).ToList();
            Assert.Single(items);
            Assert.Equal(notePath, items[0].OriginalFilePath);
            Assert.Equal("Brand new unsaved content", items[0].Content);

            // Once cleared, it is no longer returned
            await _recoveryService.ClearDraftAsync(notePath);
            var afterClear = (await _recoveryService.CheckForRecoverableItemsAsync()).ToList();
            Assert.Empty(afterClear);
        }
    }
}
