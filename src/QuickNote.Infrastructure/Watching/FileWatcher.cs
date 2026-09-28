using System;
using System.IO;
using QuickNote.Core.Interfaces;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.Infrastructure.Watching
{
    public class FileWatcher : IFileWatcher, IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly IFileChangeProcessor _changeProcessor;
        private readonly FileChangeDebouncer _debouncer;
        private readonly string _notesRoot;
        private bool _isEnabled;

        public FileWatcher(string notesRoot, IFileChangeProcessor changeProcessor)
        {
            _notesRoot = notesRoot ?? throw new ArgumentNullException(nameof(notesRoot));
            _changeProcessor = changeProcessor ?? throw new ArgumentNullException(nameof(changeProcessor));

            if (!Directory.Exists(_notesRoot))
            {
                Directory.CreateDirectory(_notesRoot);
            }

            _debouncer = new FileChangeDebouncer((path, type) =>
            {
                _changeProcessor.ProcessChange(path, type);
            }, debounceMilliseconds: 300);

            _watcher = new FileSystemWatcher(_notesRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _watcher.Changed += OnChanged;
            _watcher.Created += OnCreated;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
        }

        public void Start()
        {
            _watcher.EnableRaisingEvents = true;
            _isEnabled = true;
        }

        public void Stop()
        {
            _watcher.EnableRaisingEvents = false;
            _isEnabled = false;
        }

        public void UpdateWatchDirectory(string newPath)
        {
            if (string.IsNullOrWhiteSpace(newPath)) return;
            bool wasEnabled = _isEnabled;
            Stop();
            if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
            }
            _watcher.Path = newPath;
            if (wasEnabled)
            {
                Start();
            }
        }

        public bool IsEnabled => _isEnabled;

        private bool ShouldIgnore(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            var name = Path.GetFileName(path);
            if (name.StartsWith(".") || name.EndsWith(".tmp") || name.EndsWith(".draft") || name.StartsWith("~") || name.EndsWith(".crswap")) return true;
            if (path.Contains(Path.DirectorySeparatorChar + ".") || path.Contains(Path.AltDirectorySeparatorChar + ".")) return true;
            return false;
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            if (!_isEnabled || ShouldIgnore(e.FullPath)) return;
            _debouncer.Debounce(e.FullPath, FileChangeType.Changed);
        }

        private void OnCreated(object source, FileSystemEventArgs e)
        {
            if (!_isEnabled || ShouldIgnore(e.FullPath)) return;
            _debouncer.Debounce(e.FullPath, FileChangeType.Created);
        }

        private void OnDeleted(object source, FileSystemEventArgs e)
        {
            if (!_isEnabled || ShouldIgnore(e.FullPath)) return;
            _debouncer.Debounce(e.FullPath, FileChangeType.Deleted);
        }

        private void OnRenamed(object source, RenamedEventArgs e)
        {
            if (!_isEnabled) return;
            if (ShouldIgnore(e.FullPath) && ShouldIgnore(e.OldFullPath)) return;
            _changeProcessor.ProcessChange(e.OldFullPath, e.FullPath, FileChangeType.Renamed);
        }

        private void OnError(object source, ErrorEventArgs e)
        {
            // Ignore or log error
        }

        public void Dispose()
        {
            _watcher.Dispose();
            _debouncer.Dispose();
        }
    }
}