using System;
using QuickNote.Core.Interfaces;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.Infrastructure.Watching
{
    public class FileChangeProcessor : IFileChangeProcessor
    {
        private readonly INoteRepository _noteRepository;

        public FileChangeProcessor(INoteRepository noteRepository)
        {
            _noteRepository = noteRepository ?? throw new ArgumentNullException(nameof(noteRepository));
        }

        public void ProcessChange(string fullPath, FileChangeType changeType)
        {
            // In a real application, we would update the in-memory index based on the change.
            // For now, we'll just log the change.
            Console.WriteLine($"File change detected: {changeType} - {fullPath}");
        }

        public void ProcessChange(string oldFullPath, string newFullPath, FileChangeType changeType)
        {
            // In a real application, we would update the in-memory index for the rename.
            // For now, we'll just log the change.
            Console.WriteLine($"File rename detected: {changeType} - {oldFullPath} -> {newFullPath}");
        }
    }
}