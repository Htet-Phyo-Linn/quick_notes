using System;

namespace QuickNote.Infrastructure.Interfaces
{
    public interface IFileWatcher
    {
        void Start();
        void Stop();
        void UpdateWatchDirectory(string newPath);
        bool IsEnabled { get; }
    }
}