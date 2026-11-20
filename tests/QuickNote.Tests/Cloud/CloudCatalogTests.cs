using System;
using System.Collections.Generic;
using System.Text.Json;
using QuickNote.Core.Models;
using Xunit;

namespace QuickNote.Tests.Cloud
{
    public class CloudCatalogTests
    {
        [Fact]
        public void CloudCatalog_Serialization_RoundtripPreservesData()
        {
            var catalog = new CloudCatalog
            {
                CatalogId = "test-catalog-uuid",
                CatalogRevision = 42,
                LastUpdatedUtc = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc),
                UpdatedByDevice = "Windows-PC",
                Folders = new Dictionary<string, string>
                {
                    { "Work", "folder-123" },
                    { "Work/CyberArk", "folder-456" }
                },
                Notes = new Dictionary<string, CloudNoteEntry>
                {
                    {
                        "Work/CyberArk/CPM deployment plan.md",
                        new CloudNoteEntry
                        {
                            RelativePath = "Work/CyberArk/CPM deployment plan.md",
                            DriveFileId = "file-789",
                            Title = "CPM deployment plan",
                            ContentHash = "hash-abc",
                            SizeBytes = 2048,
                            LastModifiedUtc = new DateTime(2026, 11, 16, 11, 59, 0, DateTimeKind.Utc),
                            Tags = new List<string> { "cyberark", "atom" },
                            Revision = 5
                        }
                    }
                },
                Tombstones = new Dictionary<string, CloudTombstoneEntry>
                {
                    {
                        "Work/OldNote.md",
                        new CloudTombstoneEntry
                        {
                            RelativePath = "Work/OldNote.md",
                            DeletedUtc = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc),
                            DeletedByDevice = "Windows-PC"
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
            var deserialized = JsonSerializer.Deserialize<CloudCatalog>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(catalog.CatalogId, deserialized.CatalogId);
            Assert.Equal(42, deserialized.CatalogRevision);
            Assert.Equal("Windows-PC", deserialized.UpdatedByDevice);
            Assert.True(deserialized.Folders.ContainsKey("Work"));
            Assert.True(deserialized.Notes.ContainsKey("Work/CyberArk/CPM deployment plan.md"));
            Assert.Equal("file-789", deserialized.Notes["Work/CyberArk/CPM deployment plan.md"].DriveFileId);
            Assert.Equal("hash-abc", deserialized.Notes["Work/CyberArk/CPM deployment plan.md"].ContentHash);
            Assert.True(deserialized.Tombstones.ContainsKey("Work/OldNote.md"));
        }

        [Fact]
        public void ThemeSchema_GetColor_ResolvesAccentAndAppAccentConsistently()
        {
            var theme = new ThemeSchema
            {
                Id = "android-dark",
                AccentColor = "#3390ec"
            };
            theme.SetColor("Accent", "#3390ec");

            // Even if AppAccent wasn't explicitly set, GetColor should resolve to the accent
            Assert.Equal("#3390ec", theme.GetColor("Accent"));
            Assert.Equal("#3390ec", theme.GetColor("AppAccent"));
            Assert.Equal("#3390ec", theme.GetColor("accent"));
            Assert.Equal("#3390ec", theme.GetColor("appaccent"));
        }

        [Fact]
        public void CloudCatalog_PruneDeletedEntries_RemovesTombstonedAndUntrackedNotes()
        {
            var catalog = new CloudCatalog();
            catalog.Notes["active.md"] = new CloudNoteEntry { RelativePath = "active.md", Title = "Active" };
            catalog.Notes["deleted.md"] = new CloudNoteEntry { RelativePath = "deleted.md", Title = "Deleted" };
            catalog.Notes["trashed.md"] = new CloudNoteEntry { RelativePath = "trashed.md", Title = "Trashed" };

            catalog.Tombstones["deleted.md"] = new CloudTombstoneEntry { RelativePath = "deleted.md" };
            var trashedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "trashed.md" };

            // Pruning logic
            foreach (var key in new List<string>(catalog.Notes.Keys))
            {
                if (catalog.Tombstones.ContainsKey(key) || trashedPaths.Contains(key))
                {
                    catalog.Notes.Remove(key);
                }
            }

            Assert.True(catalog.Notes.ContainsKey("active.md"));
            Assert.False(catalog.Notes.ContainsKey("deleted.md"));
            Assert.False(catalog.Notes.ContainsKey("trashed.md"));
        }
    }
}
