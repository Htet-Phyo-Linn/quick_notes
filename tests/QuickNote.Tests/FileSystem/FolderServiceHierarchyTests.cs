using System;
using System.IO;
using System.Threading.Tasks;
using QuickNote.Core.Services;
using Xunit;

namespace QuickNote.Tests.FileSystem
{
    public class FolderServiceHierarchyTests : IDisposable
    {
        private readonly string _testDir;
        private readonly FolderService _folderService;

        public FolderServiceHierarchyTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "QuickNote_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _folderService = new FolderService();
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

        [Fact]
        public async Task MoveItemAsync_ShouldMoveFileToSubfolder()
        {
            var file = Path.Combine(_testDir, "test.md");
            await File.WriteAllTextAsync(file, "# Hello");

            var subfolder = Path.Combine(_testDir, "Sub");
            Directory.CreateDirectory(subfolder);

            var result = await _folderService.MoveItemAsync(file, subfolder);

            Assert.False(File.Exists(file));
            Assert.True(File.Exists(result));
            Assert.Equal(Path.Combine(subfolder, "test.md"), result);
        }

        [Fact]
        public async Task MoveItemAsync_ShouldMoveDirectoryToAnotherDirectory()
        {
            var folderA = Path.Combine(_testDir, "FolderA");
            var folderB = Path.Combine(_testDir, "FolderB");
            Directory.CreateDirectory(folderA);
            Directory.CreateDirectory(folderB);

            await File.WriteAllTextAsync(Path.Combine(folderA, "note.md"), "content");

            var result = await _folderService.MoveItemAsync(folderA, folderB);

            Assert.False(Directory.Exists(folderA));
            Assert.True(Directory.Exists(result));
            Assert.True(File.Exists(Path.Combine(result, "note.md")));
        }

        [Fact]
        public async Task MoveItemAsync_ShouldThrowWhenMovingFolderIntoItself()
        {
            var folderA = Path.Combine(_testDir, "FolderA");
            var subFolder = Path.Combine(folderA, "Sub");
            Directory.CreateDirectory(subFolder);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _folderService.MoveItemAsync(folderA, subFolder);
            });
        }

        [Fact]
        public async Task CopyItemAsync_ShouldDuplicateFileWithNaming()
        {
            var file = Path.Combine(_testDir, "note.md");
            await File.WriteAllTextAsync(file, "hello");

            var copyResult = await _folderService.CopyItemAsync(file, _testDir);

            Assert.True(File.Exists(file));
            Assert.True(File.Exists(copyResult));
            Assert.Contains("note - Copy.md", copyResult);
        }

        [Fact]
        public async Task RenameItemAsync_ShouldRenameFile()
        {
            var file = Path.Combine(_testDir, "old.md");
            await File.WriteAllTextAsync(file, "content");

            var newFile = await _folderService.RenameItemAsync(file, "new.md");

            Assert.False(File.Exists(file));
            Assert.True(File.Exists(newFile));
            Assert.Equal("new.md", Path.GetFileName(newFile));
        }

        [Fact]
        public async Task RenameItemAsync_ShouldRenameFolder()
        {
            var folder = Path.Combine(_testDir, "OldFolder");
            Directory.CreateDirectory(folder);

            var newFolder = await _folderService.RenameItemAsync(folder, "NewFolder");

            Assert.False(Directory.Exists(folder));
            Assert.True(Directory.Exists(newFolder));
            Assert.Equal("NewFolder", Path.GetFileName(newFolder));
        }

        [Fact]
        public async Task BuildFolderTreeAsync_ShouldPreserveExpandedStates()
        {
            var folderA = Path.Combine(_testDir, "FolderA");
            var folderB = Path.Combine(_testDir, "FolderB");
            var subFolder = Path.Combine(folderA, "SubA");
            Directory.CreateDirectory(folderA);
            Directory.CreateDirectory(folderB);
            Directory.CreateDirectory(subFolder);

            var states = new System.Collections.Generic.Dictionary<string, bool>
            {
                ["FolderA"] = true,
                ["FolderA/SubA"] = false,
                ["FolderB"] = false
            };

            var root = await _folderService.BuildFolderTreeAsync(_testDir, states);

            var nodeA = System.Linq.Enumerable.FirstOrDefault(root.Children, c => c.Name == "FolderA");
            var nodeB = System.Linq.Enumerable.FirstOrDefault(root.Children, c => c.Name == "FolderB");
            Assert.NotNull(nodeA);
            Assert.NotNull(nodeB);

            Assert.True(nodeA.IsExpanded);
            Assert.False(nodeB.IsExpanded);

            var subNode = System.Linq.Enumerable.FirstOrDefault(nodeA.Children, c => c.Name == "SubA");
            Assert.NotNull(subNode);
            Assert.False(subNode.IsExpanded);
        }

        [Fact]
        public async Task MoveItemAsync_ShouldBeNoOpWhenMovingFileToSameDirectory()
        {
            var file = Path.Combine(_testDir, "noop.md");
            await File.WriteAllTextAsync(file, "content");

            var result = await _folderService.MoveItemAsync(file, _testDir);

            Assert.True(File.Exists(file));
            Assert.Equal(Path.GetFullPath(file), Path.GetFullPath(result));
        }

        [Fact]
        public async Task MoveItemAsync_ShouldBeNoOpWhenMovingFolderToSameParentDirectory()
        {
            var folder = Path.Combine(_testDir, "SameFolder");
            Directory.CreateDirectory(folder);

            var result = await _folderService.MoveItemAsync(folder, _testDir);

            Assert.True(Directory.Exists(folder));
            Assert.Equal(Path.GetFullPath(folder), Path.GetFullPath(result));
        }

        [Fact]
        public async Task BuildFolderTreeAsync_ShouldIgnoreAssetsAndDotFolders()
        {
            var userFolder = Path.Combine(_testDir, "WorkNotes");
            var assetsFolder = Path.Combine(_testDir, "assets");
            var dotAssetsFolder = Path.Combine(_testDir, ".assets");
            var binFolder = Path.Combine(_testDir, "bin");

            Directory.CreateDirectory(userFolder);
            Directory.CreateDirectory(assetsFolder);
            Directory.CreateDirectory(dotAssetsFolder);
            Directory.CreateDirectory(binFolder);

            await File.WriteAllTextAsync(Path.Combine(userFolder, "note.md"), "hello");
            await File.WriteAllTextAsync(Path.Combine(assetsFolder, "img.png"), "data");
            await File.WriteAllTextAsync(Path.Combine(dotAssetsFolder, "img2.png"), "data");

            var rootNode = await _folderService.BuildFolderTreeAsync(_testDir);

            var childFolderNames = System.Linq.Enumerable.Select(
                System.Linq.Enumerable.Where(rootNode.Children, c => c.IsDirectory),
                c => c.Name
            ).ToList();

            Assert.Contains("WorkNotes", childFolderNames);
            Assert.DoesNotContain("assets", childFolderNames, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(".assets", childFolderNames, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("bin", childFolderNames, StringComparer.OrdinalIgnoreCase);
        }
    }
}

