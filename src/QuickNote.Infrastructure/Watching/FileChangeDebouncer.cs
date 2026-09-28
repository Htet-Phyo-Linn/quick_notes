using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.Infrastructure.Watching
{
    public class FileChangeDebouncer : IDisposable
    {
        private readonly int _debounceMilliseconds;
        private readonly Action<string, FileChangeType> _onDebouncedAction;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new ConcurrentDictionary<string, CancellationTokenSource>();
        private bool _disposed;

        public FileChangeDebouncer(Action<string, FileChangeType> onDebouncedAction, int debounceMilliseconds = 300)
        {
            _onDebouncedAction = onDebouncedAction ?? throw new ArgumentNullException(nameof(onDebouncedAction));
            _debounceMilliseconds = debounceMilliseconds;
        }

        public void Debounce(string filePath, FileChangeType changeType)
        {
            if (_disposed) return;

            // Cancel any pending timer for this file
            if (_timers.TryGetValue(filePath, out var existingCts))
            {
                try
                {
                    existingCts.Cancel();
                    existingCts.Dispose();
                }
                catch { }
            }

            var cts = new CancellationTokenSource();
            _timers[filePath] = cts;

            Task.Delay(_debounceMilliseconds, cts.Token)
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully && !_disposed)
                    {
                        _timers.TryRemove(filePath, out _);
                        _onDebouncedAction(filePath, changeType);
                    }
                }, TaskScheduler.Default);
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var kvp in _timers)
            {
                try
                {
                    kvp.Value.Cancel();
                    kvp.Value.Dispose();
                }
                catch { }
            }
            _timers.Clear();
        }
    }
}
