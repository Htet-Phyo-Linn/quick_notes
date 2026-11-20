using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using QuickNote.Core.Models;
using QuickNote.Infrastructure.FileSystem;
using Xunit;

namespace QuickNote.Tests.FileSystem
{
    public class SqliteNoteIndexServiceTests : IDisposable
    {
        private readonly string _testDir;
        private readonly SqliteNoteIndexService _service;

        public SqliteNoteIndexServiceTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "QuickNote_SqliteTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _service = new SqliteNoteIndexService(_testDir);
        }

        public void Dispose()
        {
            _service.Dispose();
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }

        [Fact]
        public void UpsertAndGet_InsertsAndRetrievesNoteMetadata()
        {
            var noteId = Guid.NewGuid();
            var entry = new NoteIndexEntry
            {
                Id = noteId,
                RelativePath = "work/project.md",
                Title = "Project Plan",
                Snippet = "This is a secret project snippet",
                RenderText = "This is a secret project snippet",
                Content = "# Project Plan\n\nThis is a secret project snippet with critical milestones.",
                Tags = new[] { "work", "planning" },
                Headings = new[] { "Project Plan" },
                LastModified = DateTime.UtcNow,
                FileSize = 1024,
                LastFileWriteUtc = DateTime.UtcNow,
                ContentHash = "HASH123",
                IsDiskSynced = true
            };

            _service.UpsertNote(entry);

            var retrieved = _service.GetNoteById(noteId);
            Assert.NotNull(retrieved);
            Assert.Equal(noteId, retrieved.Id);
            Assert.Equal("work/project.md", retrieved.RelativePath);
            Assert.Equal("Project Plan", retrieved.Title);
            Assert.Contains("work", retrieved.Tags);
            Assert.Contains("planning", retrieved.Tags);
            Assert.True(retrieved.IsDiskSynced);

            var retrievedByPath = _service.GetNoteByPath("work/project.md");
            Assert.NotNull(retrievedByPath);
            Assert.Equal(noteId, retrievedByPath.Id);
        }

        [Fact]
        public void SearchFts_FindsMatchingNotesByContent()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            _service.UpsertNote(new NoteIndexEntry
            {
                Id = id1,
                RelativePath = "recipes/pasta.md",
                Title = "Carbonara Recipe",
                Tags = new[] { "food", "italian" }
            }, content: "Authentic spaghetti carbonara with guanciale and pecorino cheese.");

            _service.UpsertNote(new NoteIndexEntry
            {
                Id = id2,
                RelativePath = "tech/database.md",
                Title = "SQLite Architecture",
                Tags = new[] { "code", "database" }
            }, content: "SQLite B-Tree and WAL journaling engine internals.");

            var foodMatches = _service.SearchFts("guanciale");
            Assert.Contains(id1, foodMatches);
            Assert.DoesNotContain(id2, foodMatches);

            var dbMatches = _service.SearchFts("journaling");
            Assert.Contains(id2, dbMatches);
            Assert.DoesNotContain(id1, dbMatches);
        }

        [Fact]
        public void DeleteNote_RemovesFromSqliteAndFts()
        {
            var id = Guid.NewGuid();
            _service.UpsertNote(new NoteIndexEntry
            {
                Id = id,
                RelativePath = "temp.md",
                Title = "Temporary Note"
            }, content: "Disappear quickly into the void.");

            Assert.NotNull(_service.GetNoteById(id));
            Assert.Contains(id, _service.SearchFts("quickly"));

            _service.DeleteNote(id);

            Assert.Null(_service.GetNoteById(id));
            Assert.DoesNotContain(id, _service.SearchFts("quickly"));
        }

        [Fact]
        public void MigrateFromJsonIfEmpty_PopulatesSqliteFromExistingJson()
        {
            var emptyTestDir = Path.Combine(Path.GetTempPath(), "QuickNote_MigrationTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(emptyTestDir);

            try
            {
                var jsonPath = Path.Combine(emptyTestDir, "notes_index.json");
                var catalog = new NotesIndexCatalog
                {
                    Notes = new System.Collections.Generic.List<NoteIndexEntry>
                    {
                        new NoteIndexEntry
                        {
                            Id = Guid.NewGuid(),
                            RelativePath = "imported.md",
                            Title = "Imported Note",
                            Content = "Content from old json catalog."
                        }
                    }
                };
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(catalog));

                using var sqlite = new SqliteNoteIndexService(emptyTestDir);
                Assert.Equal(0, sqlite.GetNoteCount());

                sqlite.MigrateFromJsonIfEmpty(jsonPath);
                Assert.Equal(1, sqlite.GetNoteCount());

                var imported = sqlite.GetNoteByPath("imported.md");
                Assert.NotNull(imported);
                Assert.Equal("Imported Note", imported.Title);
            }
            finally
            {
                try { Directory.Delete(emptyTestDir, recursive: true); } catch { }
            }
        }
    }
}
