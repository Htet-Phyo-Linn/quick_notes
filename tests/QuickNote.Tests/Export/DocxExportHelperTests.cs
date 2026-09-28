using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using QuickNote.App.Helpers;
using QuickNote.Core.Models;
using Xunit;

namespace QuickNote.Tests.Export
{
    public class DocxExportHelperTests : IDisposable
    {
        private readonly string _testDir;

        public DocxExportHelperTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "QuickNote_Docx_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                try { Directory.Delete(_testDir, true); } catch { }
            }
        }

        [Fact]
        public async Task ExportToDocx_WithAssetsImage_EmbedsImageInZipPackage()
        {
            // Arrange: create .assets folder and a dummy PNG file
            var assetsDir = Path.Combine(_testDir, ".assets");
            Directory.CreateDirectory(assetsDir);
            var imageFile = Path.Combine(assetsDir, "test_image.png");
            
            // Minimal 1x1 PNG bytes
            var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
            await File.WriteAllBytesAsync(imageFile, pngBytes);

            var note = new NoteDocument
            {
                Title = "Test Note with Image",
                Content = "# Test Note with Image\n\nHere is an image:\n\n![My Alt Text](.assets/test_image.png)\n\nEnd of note.",
                FolderPath = _testDir
            };

            var targetDocx = Path.Combine(_testDir, "exported.docx");

            // Act
            await DocxExportHelper.ExportToDocxAsync(note, targetDocx, _testDir);

            // Assert
            Assert.True(File.Exists(targetDocx));

            using var zip = ZipFile.OpenRead(targetDocx);
            Assert.NotNull(zip.GetEntry("word/document.xml"));
            Assert.NotNull(zip.GetEntry("word/styles.xml"));
            Assert.NotNull(zip.GetEntry("word/media/image_1.png"));

            using var docStream = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
            var docXml = await docStream.ReadToEndAsync();
            Assert.Contains("rIdImg1", docXml);
            Assert.Contains("Test Note with Image", docXml);
        }

        [Fact]
        public async Task ExportToDocx_WithBase64Image_EmbedsImageInZipPackage()
        {
            // Arrange
            var b64Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            var note = new NoteDocument
            {
                Title = "Base64 Note",
                Content = $"# Base64 Note\n\n![Embedded]({b64Png})\n",
                FolderPath = _testDir
            };

            var targetDocx = Path.Combine(_testDir, "exported_b64.docx");

            // Act
            await DocxExportHelper.ExportToDocxAsync(note, targetDocx, _testDir);

            // Assert
            Assert.True(File.Exists(targetDocx));

            using var zip = ZipFile.OpenRead(targetDocx);
            Assert.NotNull(zip.GetEntry("word/media/image_1.png"));
        }

        [Fact]
        public void ResolveImagePath_WithUrlEncodedAndSubfolder_ResolvesCorrectly()
        {
            var noteDir = Path.Combine(_testDir, "Cortex", "SMEDB");
            var assetsDir = Path.Combine(_testDir, "assets");
            Directory.CreateDirectory(noteDir);
            Directory.CreateDirectory(assetsDir);

            var sampleFile = Path.Combine(assetsDir, "SMEDB Old 31 Broker VM info_image.png");
            File.WriteAllText(sampleFile, "dummy");

            var resolved = DocxExportHelper.ResolveImagePath("assets/SMEDB%20Old%2031%20Broker%20VM%20info_image.png", noteDir, _testDir);
            Assert.NotNull(resolved);
            Assert.True(File.Exists(resolved));
            Assert.Equal(sampleFile, resolved);
        }
    }
}
