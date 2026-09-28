using System;
using System.IO;
using System.Threading.Tasks;
using QuickNote.Infrastructure.FileSystem;
using Xunit;

namespace QuickNote.Tests.FileSystem
{
    public class AtomicFileWriterTests : IDisposable
    {
        private readonly string _testDir;
        private readonly AtomicFileWriter _writer = new AtomicFileWriter();

        public AtomicFileWriterTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "QuickNoteTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        [Fact]
        public async Task WriteAllTextAsync_WritesContentAtomicallyAndLeavesNoTempFiles()
        {
            var targetFile = Path.Combine(_testDir, "test.md");
            var content = "# Hello World\nTesting atomic save.";

            await _writer.WriteAllTextAsync(targetFile, content);

            Assert.True(File.Exists(targetFile));
            var saved = await File.ReadAllTextAsync(targetFile);
            Assert.Equal(content, saved);

            // Verify no .tmp files remain
            var tmpFiles = Directory.GetFiles(_testDir, "*.tmp");
            Assert.Empty(tmpFiles);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }
    }
}
