using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;

namespace QuickNote.Infrastructure.FileSystem
{
    public class FileNoteRepository : INoteRepository
    {
        private string _notesRoot;
        private readonly IMarkdownParser _markdownParser;
        private readonly AtomicFileWriter _atomicFileWriter;

        public string NotesRoot => _notesRoot;

        public void UpdateNotesRoot(string newNotesRoot)
        {
            if (string.IsNullOrWhiteSpace(newNotesRoot)) return;
            _notesRoot = newNotesRoot;
            if (!Directory.Exists(_notesRoot))
            {
                Directory.CreateDirectory(_notesRoot);
            }
        }

        public FileNoteRepository(string notesRoot, IMarkdownParser markdownParser, AtomicFileWriter? atomicFileWriter = null)
        {
            _notesRoot = notesRoot ?? throw new ArgumentNullException(nameof(notesRoot));
            _markdownParser = markdownParser ?? throw new ArgumentNullException(nameof(markdownParser));
            _atomicFileWriter = atomicFileWriter ?? new AtomicFileWriter();

            if (!Directory.Exists(_notesRoot))
            {
                Directory.CreateDirectory(_notesRoot);
            }
        }

        public async Task<NoteDocument?> GetNoteByIdAsync(Guid id)
        {
            var notes = await GetAllNotesAsync();
            return notes.FirstOrDefault(n => n.Id == id);
        }

        public async Task<NoteDocument?> GetNoteByPathAsync(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return null;
            }

            return await LoadNoteFromFileAsync(fullPath);
        }

        public async Task<IEnumerable<NoteDocument>> GetAllNotesAsync()
        {
            if (!Directory.Exists(_notesRoot))
            {
                return Enumerable.Empty<NoteDocument>();
            }

            var files = Directory.GetFiles(_notesRoot, "*.md", SearchOption.AllDirectories);
            var notes = new List<NoteDocument>();

            foreach (var file in files)
            {
                // Skip hidden files or files inside .recovery or dot directories
                var relative = Path.GetRelativePath(_notesRoot, file);
                if (relative.StartsWith(".") || relative.Contains(Path.DirectorySeparatorChar + "."))
                {
                    continue;
                }

                var note = await LoadNoteFromFileAsync(file);
                if (note != null)
                {
                    notes.Add(note);
                }
            }

            return notes;
        }

        public async Task AddNoteAsync(NoteDocument note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            await _atomicFileWriter.WriteAllTextAsync(note.FullPath, note.Content ?? string.Empty);
        }

        public async Task UpdateNoteAsync(NoteDocument note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            await _atomicFileWriter.WriteAllTextAsync(note.FullPath, note.Content ?? string.Empty);
        }

        public Task DeleteNoteAsync(Guid id)
        {
            var files = Directory.GetFiles(_notesRoot, "*.md", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var hash = System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(file.ToLowerInvariant()));
                var fileId = new Guid(hash.Take(16).ToArray());
                if (fileId == id)
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                    break;
                }
            }
            return Task.CompletedTask;
        }

        public async Task<bool> NoteExistsAsync(Guid id)
        {
            var note = await GetNoteByIdAsync(id);
            return note != null;
        }

        private async Task<NoteDocument?> LoadNoteFromFileAsync(string filePath)
        {
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                var doc = _markdownParser.Parse(filePath, content);
                doc.RelativePath = Path.GetRelativePath(_notesRoot, filePath);
                return doc;
            }
            catch
            {
                return null;
            }
        }
    }
}