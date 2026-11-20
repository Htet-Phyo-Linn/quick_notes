using System;
using System.IO;
using QuickNote.Core.Utilities;
using Xunit;

namespace QuickNote.Tests.Utilities
{
    public class StoragePathHelperTests
    {
        [Fact]
        public void GetRealLocalDocumentsFolder_ReturnsValidDirectory()
        {
            var folder = StoragePathHelper.GetRealLocalDocumentsFolder();
            Assert.False(string.IsNullOrWhiteSpace(folder));
            Assert.True(Directory.Exists(folder));
        }

        [Fact]
        public void GetDefaultGoogleDriveNotesPath_EndsWithQuickNoteGoogleDrive()
        {
            var path = StoragePathHelper.GetDefaultGoogleDriveNotesPath();
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.EndsWith("QuickNote-GoogleDrive", path, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetDefaultLocalNotesPath_EndsWithQuickNoteLocal()
        {
            var path = StoragePathHelper.GetDefaultLocalNotesPath();
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.EndsWith("QuickNote-Local", path, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GoogleDriveAndLocal_AreDistinctPaths()
        {
            var drive = StoragePathHelper.GetDefaultGoogleDriveNotesPath();
            var local = StoragePathHelper.GetDefaultLocalNotesPath();

            Assert.NotEqual(
                Path.GetFullPath(drive).TrimEnd('\\', '/'),
                Path.GetFullPath(local).TrimEnd('\\', '/'),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
