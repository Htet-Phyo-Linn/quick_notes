using System;
using System.Collections.Generic;

namespace QuickNote.Infrastructure.Cloud
{
    public static class SyncLogger
    {
        private static readonly object _lock = new();
        private static readonly List<string> _logs = new();
        private const int MaxEntries = 100;

        public static event Action<string>? LogAdded;

        public static IReadOnlyList<string> GetAllLogs()
        {
            lock (_lock)
            {
                return _logs.ToArray();
            }
        }

        public static string GetFormattedLogs()
        {
            lock (_lock)
            {
                if (_logs.Count == 0) return "No sync activity recorded yet.";
                return string.Join(Environment.NewLine, _logs);
            }
        }

        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            lock (_lock)
            {
                _logs.Add(line);
                if (_logs.Count > MaxEntries)
                {
                    _logs.RemoveAt(0);
                }
            }
            try
            {
                Console.WriteLine($"[SyncLog] {line}");
                LogAdded?.Invoke(line);
            }
            catch { }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _logs.Clear();
            }
            LogAdded?.Invoke(string.Empty);
        }
    }
}
