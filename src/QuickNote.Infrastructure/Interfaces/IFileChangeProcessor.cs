using System;

namespace QuickNote.Infrastructure.Interfaces
{
    public enum FileChangeType
    {
        Created,
        Changed,
        Deleted,
        Renamed
    }

    public interface IFileChangeProcessor
    {
        void ProcessChange(string fullPath, FileChangeType changeType);
        void ProcessChange(string oldFullPath, string newFullPath, FileChangeType changeType);
    }
}