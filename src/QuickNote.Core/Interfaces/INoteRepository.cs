using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using QuickNote.Core.Models;

namespace QuickNote.Core.Interfaces
{
    public interface INoteRepository
    {
        string NotesRoot { get; }
        void UpdateNotesRoot(string newNotesRoot);
        Task<NoteDocument?> GetNoteByIdAsync(Guid id);
        Task<NoteDocument?> GetNoteByPathAsync(string fullPath);
        Task<IEnumerable<NoteDocument>> GetAllNotesAsync();
        Task AddNoteAsync(NoteDocument note);
        Task UpdateNoteAsync(NoteDocument note);
        Task DeleteNoteAsync(Guid id);
        Task<bool> NoteExistsAsync(Guid id);
        Task<List<NoteDocument>?> GetCachedNotesAsync();
        Task SaveNoteFastAsync(NoteDocument note, string? oldFullPath = null);
        Task FlushPendingWritesAsync();
        Task<IEnumerable<NoteDocument>> ReconcileDiskNotesAsync();
    }
}