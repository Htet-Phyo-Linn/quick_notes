using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using QuickNote.Core.Markdown;
using QuickNote.Core.Models;
using QuickNote.Infrastructure.FileSystem;
using Xunit;

namespace QuickNote.Tests.FileSystem
{
    public class FileNoteRepositoryIndexTests : IDisposable
    {
        private readonly string _testDir;
        private readonly MarkdownParser _parser;
        private readonly AtomicFileWriter _atomicWriter;

        public FileNoteRepositoryIndexTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "QuickNote_RepoIndexTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _parser = new MarkdownParser();
            _atomicWriter = new AtomicFileWriter();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, recursive: true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task GetAllNotesAsync_CreatesNotesIndexJson_OnFirstScan()
        {
            // Arrange
            var note1Path = Path.Combine(_testDir, "Note1.md");
            await File.WriteAllTextAsync(note1Path, "# Note 1\n\nContent for note 1 with #test");

            var repo = new FileNoteRepository(_testDir, _parser, _atomicWriter);
            var indexPath = Path.Combine(_testDir, ".quicknote", "notes_index.json");

            Assert.False(File.Exists(indexPath));

            // Act
            var notes = (await repo.GetAllNotesAsync()).ToList();

            // Assert
            Assert.Single(notes);
            Assert.Equal("Note 1", notes[0].Title);
            Assert.Contains("test", notes[0].Tags);
            Assert.True(File.Exists(indexPath), "notes_index.json should be created after GetAllNotesAsync");

            var json = await File.ReadAllTextAsync(indexPath);
            var catalog = JsonSerializer.Deserialize<NotesIndexCatalog>(json);
            Assert.NotNull(catalog);
            Assert.Single(catalog.Notes);
            Assert.Equal("Note 1", catalog.Notes[0].Title);
            Assert.Contains("test", catalog.Notes[0].Tags);
        }

        [Fact]
        public async Task GetAllNotesAsync_ReusesIndex_WhenFilesUnchanged()
        {
            // Arrange
            var note1Path = Path.Combine(_testDir, "Note1.md");
            await File.WriteAllTextAsync(note1Path, "# Title Alpha\n\nBody alpha");

            var repo = new FileNoteRepository(_testDir, _parser, _atomicWriter);
            var indexPath = Path.Combine(_testDir, ".quicknote", "notes_index.json");

            // First load creates index
            var notesFirst = (await repo.GetAllNotesAsync()).ToList();
            Assert.Single(notesFirst);

            var indexWriteTimeBefore = File.GetLastWriteTimeUtc(indexPath);

            // Small delay to ensure timestamp would change if written
            await Task.Delay(50);

            // Act: second load
            var notesSecond = (await repo.GetAllNotesAsync()).ToList();

            // Assert
            Assert.Single(notesSecond);
            Assert.Equal("Title Alpha", notesSecond[0].Title);
            Assert.Equal(indexWriteTimeBefore, File.GetLastWriteTimeUtc(indexPath));
        }

        [Fact]
        public async Task AddNoteAsync_UpdatesNotesIndexJson()
        {
            // Arrange
            var repo = new FileNoteRepository(_testDir, _parser, _atomicWriter);
            var indexPath = Path.Combine(_testDir, ".quicknote", "notes_index.json");

            var newNote = new NoteDocument
            {
                Id = Guid.NewGuid(),
                FullPath = Path.Combine(_testDir, "NewNote.md"),
                RelativePath = "NewNote.md",
                FileName = "NewNote.md",
                FolderPath = _testDir,
                Title = "Added Note",
                Content = "# Added Note\n\nSome exciting content",
                LastModified = DateTime.UtcNow
            };

            // Act
            await repo.AddNoteAsync(newNote);

            // Assert
            Assert.True(File.Exists(newNote.FullPath));
            Assert.True(File.Exists(indexPath));

            var json = await File.ReadAllTextAsync(indexPath);
            var catalog = JsonSerializer.Deserialize<NotesIndexCatalog>(json);
            Assert.NotNull(catalog);
            Assert.Single(catalog.Notes);
            Assert.Equal("Added Note", catalog.Notes[0].Title);
            Assert.Equal("NewNote.md", catalog.Notes[0].RelativePath);
        }

        [Fact]
        public async Task UpdateNoteAsync_UpdatesNotesIndexJson()
        {
            // Arrange
            var repo = new FileNoteRepository(_testDir, _parser, _atomicWriter);
            var note = new NoteDocument
            {
                Id = Guid.NewGuid(),
                FullPath = Path.Combine(_testDir, "TestNote.md"),
                RelativePath = "TestNote.md",
                FileName = "TestNote.md",
                FolderPath = _testDir,
                Title = "Original Title",
                Content = "# Original Title\n\nOriginal text",
                LastModified = DateTime.UtcNow
            };
            await repo.AddNoteAsync(note);

            // Act
            note.Title = "Updated Title";
            note.Content = "# Updated Title\n\nUpdated text content with #tag1";
            note.Tags = new[] { "tag1" };
            await repo.UpdateNoteAsync(note);

            // Assert
            var indexPath = Path.Combine(_testDir, ".quicknote", "notes_index.json");
            var json = await File.ReadAllTextAsync(indexPath);
            var catalog = JsonSerializer.Deserialize<NotesIndexCatalog>(json);
            Assert.NotNull(catalog);
            Assert.Single(catalog.Notes);
            Assert.Equal("Updated Title", catalog.Notes[0].Title);
            Assert.Contains("tag1", catalog.Notes[0].Tags);
        }

        [Fact]
        public async Task DeleteNoteAsync_RemovesFromNotesIndexJson()
        {
            // Arrange
            var repo = new FileNoteRepository(_testDir, _parser, _atomicWriter);
            var note = new NoteDocument
            {
                Id = Guid.NewGuid(),
                FullPath = Path.Combine(_testDir, "ToDelete.md"),
                RelativePath = "ToDelete.md",
                FileName = "ToDelete.md",
                FolderPath = _testDir,
                Title = "Delete Me",
                Content = "# Delete Me",
                LastModified = DateTime.UtcNow
            };
            await repo.AddNoteAsync(note);

            // Act
            await repo.DeleteNoteAsync(note.Id);

            // Assert
            Assert.False(File.Exists(note.FullPath));
            var indexPath = Path.Combine(_testDir, ".quicknote", "notes_index.json");
            var json = await File.ReadAllTextAsync(indexPath);
            var catalog = JsonSerializer.Deserialize<NotesIndexCatalog>(json);
            Assert.NotNull(catalog);
            Assert.Empty(catalog.Notes);
        }

        [Fact]
        public async Task GetAllNotesAsync_DetectsDirectDiskEdits_AndRefreshesIndex()
        {
            // Arrange
            var notePath = Path.Combine(_testDir, "ExternalEdit.md");
            await File.WriteAllTextAsync(notePath, "# Initial Note\n\nInitial body");

            var repo = new FileNoteRepository(_testDir, _parser, _atomicWriter);
            var initialNotes = (await repo.GetAllNotesAsync()).ToList();
            Assert.Equal("Initial Note", initialNotes[0].Title);

            // Simulate external change (e.g. cloud sync or external editor)
            await Task.Delay(20);
            await File.WriteAllTextAsync(notePath, "# Modified Externally\n\nNew body with #sync");

            // Act
            var refreshedNotes = (await repo.GetAllNotesAsync()).ToList();

            // Assert
            Assert.Single(refreshedNotes);
            Assert.Equal("Modified Externally", refreshedNotes[0].Title);
            Assert.Contains("sync", refreshedNotes[0].Tags);

            var indexPath = Path.Combine(_testDir, ".quicknote", "notes_index.json");
            var json = await File.ReadAllTextAsync(indexPath);
            var catalog = JsonSerializer.Deserialize<NotesIndexCatalog>(json);
            Assert.NotNull(catalog);
            Assert.Equal("Modified Externally", catalog.Notes[0].Title);
        }

        [Fact]
        public async Task SaveNoteFastAsync_UpdatesIndexImmediately_AndFlushesToDiskInBackground()
        {
            // Arrange
            var repo = new FileNoteRepository(_testDir, _parser, _atomicWriter);
            var note = new NoteDocument
            {
                Id = Guid.NewGuid(),
                FullPath = Path.Combine(_testDir, "FastNote.md"),
                RelativePath = "FastNote.md",
                FileName = "FastNote.md",
                FolderPath = _testDir,
                Title = "Fast Note",
                Content = "# Fast Note\n\nInstant text preview with #fast",
                LastModified = DateTime.UtcNow
            };

            // Act - Fast Save
            await repo.SaveNoteFastAsync(note);

            // Index catalog should be updated immediately with RenderText
            var cached = await repo.GetCachedNotesAsync();
            Assert.NotNull(cached);
            Assert.Single(cached);
            Assert.Equal("Fast Note", cached[0].Title);
            Assert.Equal("Instant text preview with fast", cached[0].RenderText);
            Assert.Contains("fast", cached[0].Tags);

            // Wait for background queue to flush to physical disk
            await repo.FlushPendingWritesAsync();

            Assert.True(File.Exists(note.FullPath));
            var diskContent = await File.ReadAllTextAsync(note.FullPath);
            Assert.Equal("# Fast Note\n\nInstant text preview with #fast", diskContent);
        }

        [Fact]
        public async Task BuildFolderTreeFromNotes_BuildsCorrectHierarchyWithoutDiskQueries()
        {
            // Arrange
            var folderService = new QuickNote.Core.Services.FolderService();
            var notes = new[]
            {
                new NoteDocument
                {
                    Id = Guid.NewGuid(),
                    FullPath = Path.Combine(_testDir, "Root.md"),
                    RelativePath = "Root.md",
                    FileName = "Root.md",
                    Title = "Root Note",
                    Content = "Root"
                },
                new NoteDocument
                {
                    Id = Guid.NewGuid(),
                    FullPath = Path.Combine(_testDir, "Projects", "Work", "Doc.md"),
                    RelativePath = "Projects/Work/Doc.md",
                    FileName = "Doc.md",
                    Title = "Work Document",
                    Content = "Work"
                }
            };

            // Act: Build tree in memory
            var root = folderService.BuildFolderTreeFromNotes(_testDir, notes);

            // Assert
            Assert.NotNull(root);
            Assert.Equal(2, root.Children.Count); // "Projects" folder and "Root" file

            var projectsFolder = root.Children.FirstOrDefault(c => c.IsDirectory && c.Name == "Projects");
            Assert.NotNull(projectsFolder);

            var workFolder = projectsFolder.Children.FirstOrDefault(c => c.IsDirectory && c.Name == "Work");
            Assert.NotNull(workFolder);

            var workDoc = workFolder.Children.FirstOrDefault(c => !c.IsDirectory && c.DisplayName == "Work Document");
            Assert.NotNull(workDoc);
        }
    }
}
