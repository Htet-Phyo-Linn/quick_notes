using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;

namespace QuickNote.Infrastructure.Services
{
    public class BackupService : IBackupService
    {
        private string _notesRoot;
        private string _backupDirectory;

        public string BackupDirectory => _backupDirectory;

        public BackupService(string notesRoot)
        {
            _notesRoot = notesRoot ?? throw new ArgumentNullException(nameof(notesRoot));
            _backupDirectory = Path.Combine(_notesRoot, ".backups");
            EnsureDirectoryExists();
        }

        public void UpdateNotesRoot(string newNotesRoot)
        {
            if (string.IsNullOrWhiteSpace(newNotesRoot)) return;
            _notesRoot = newNotesRoot;
            _backupDirectory = Path.Combine(_notesRoot, ".backups");
            EnsureDirectoryExists();
        }

        private void EnsureDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_backupDirectory))
                {
                    var dir = Directory.CreateDirectory(_backupDirectory);
                    if (OperatingSystem.IsWindows())
                    {
                        try { dir.Attributes |= FileAttributes.Hidden; } catch { }
                    }
                }
            }
            catch { }
        }

        public async Task<List<NoteBackupGroup>> GetBackupGroupsAsync()
        {
            return await Task.Run(() =>
            {
                var groups = new Dictionary<string, NoteBackupGroup>(StringComparer.OrdinalIgnoreCase);
                if (!Directory.Exists(_backupDirectory)) return new List<NoteBackupGroup>();

                var files = Directory.GetFiles(_backupDirectory, "*.bak");
                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        var rawName = Path.GetFileNameWithoutExtension(file);

                        // Expected pattern: {NoteTitle}_{yyyyMMdd_HHmmss} or {NoteTitle}_manual_{yyyyMMdd_HHmmss}
                        string noteTitle = rawName;
                        DateTime timestamp = fi.LastWriteTime;
                        bool isManual = false;

                        var match = Regex.Match(rawName, @"^(?<title>.+?)(?:_(?<type>manual|PreRestore))?_(?<date>\d{8}_\d{6}(?:_\d{3})?)$", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            noteTitle = match.Groups["title"].Value;
                            isManual = string.Equals(match.Groups["type"].Value, "manual", StringComparison.OrdinalIgnoreCase);
                            var dateStr = match.Groups["date"].Value;
                            string[] formats = new[] { "yyyyMMdd_HHmmss", "yyyyMMdd_HHmmss_fff" };

                            if (DateTime.TryParseExact(dateStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDt))
                            {
                                timestamp = parsedDt;
                            }
                        }

                        // Try locating the active note path in notesRoot
                        var targetNotePath = Path.Combine(_notesRoot, $"{noteTitle}.md");
                        if (!File.Exists(targetNotePath) && Directory.Exists(_notesRoot))
                        {
                            try
                            {
                                var found = Directory.GetFiles(_notesRoot, $"{noteTitle}.*", SearchOption.AllDirectories)
                                    .FirstOrDefault(f => !f.Contains(".backups", StringComparison.OrdinalIgnoreCase) && !f.Contains(".trash", StringComparison.OrdinalIgnoreCase));
                                if (!string.IsNullOrEmpty(found))
                                {
                                    targetNotePath = found;
                                }
                            }
                            catch { }
                        }

                        var version = new NoteBackupVersion
                        {
                            OriginalFilePath = targetNotePath,
                            BackupFilePath = file,
                            NoteTitle = noteTitle,
                            Timestamp = timestamp,
                            FileSize = fi.Length,
                            IsManual = isManual
                        };

                        if (!groups.TryGetValue(noteTitle, out var group))
                        {
                            group = new NoteBackupGroup
                            {
                                OriginalFilePath = targetNotePath,
                                NoteTitle = noteTitle
                            };
                            groups[noteTitle] = group;
                        }

                        group.Versions.Add(version);
                    }
                    catch { }
                }

                // Sort versions within each group newest first
                foreach (var g in groups.Values)
                {
                    g.Versions.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
                }

                // Return groups sorted with most recent revisions first
                return groups.Values
                    .Where(g => g.Versions.Count > 0)
                    .OrderByDescending(g => g.LatestTimestamp)
                    .ToList();
            });
        }

        public async Task<string> ReadBackupContentAsync(string backupFilePath)
        {
            if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
            {
                return string.Empty;
            }
            return await File.ReadAllTextAsync(backupFilePath);
        }

        public async Task<bool> RestoreBackupAsync(string targetNotePath, string backupFilePath)
        {
            if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
            {
                return false;
            }

            try
            {
                EnsureDirectoryExists();

                // 1. Safety snapshot of existing file before overwrite
                if (!string.IsNullOrEmpty(targetNotePath) && File.Exists(targetNotePath))
                {
                    var title = Path.GetFileNameWithoutExtension(targetNotePath);
                    var safetyFile = Path.Combine(_backupDirectory, $"{title}_PreRestore_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
                    if (File.Exists(safetyFile))
                    {
                        safetyFile = Path.Combine(_backupDirectory, $"{title}_PreRestore_{DateTime.Now:yyyyMMdd_HHmmss_fff}.bak");
                    }
                    File.Copy(targetNotePath, safetyFile, overwrite: true);
                }

                // 2. Read restored content and overwrite target note
                var content = await File.ReadAllTextAsync(backupFilePath);
                var targetDir = Path.GetDirectoryName(targetNotePath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                await File.WriteAllTextAsync(targetNotePath, content);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupService] Restore failed: {ex.Message}");
                return false;
            }
        }

        public async Task<string?> CreateManualBackupAsync(string notePath)
        {
            if (string.IsNullOrWhiteSpace(notePath) || !File.Exists(notePath))
            {
                return null;
            }

            try
            {
                EnsureDirectoryExists();
                var title = Path.GetFileNameWithoutExtension(notePath);
                var dest = Path.Combine(_backupDirectory, $"{title}_manual_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
                if (File.Exists(dest))
                {
                    dest = Path.Combine(_backupDirectory, $"{title}_manual_{DateTime.Now:yyyyMMdd_HHmmss_fff}.bak");
                }
                await Task.Run(() => File.Copy(notePath, dest, overwrite: true));
                return dest;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupService] Manual backup error: {ex.Message}");
                return null;
            }
        }

        public async Task<int> PruneBackupsAsync(int maxPerNote = 20, int maxAgeDays = 30)
        {
            return await Task.Run(() =>
            {
                int deletedCount = 0;
                var groups = GetBackupGroupsAsync().GetAwaiter().GetResult();
                var cutoffDate = DateTime.Now.AddDays(-maxAgeDays);

                foreach (var g in groups)
                {
                    // Keep at least 3 most recent backups regardless of age
                    for (int i = 0; i < g.Versions.Count; i++)
                    {
                        var ver = g.Versions[i];
                        bool shouldDelete = false;

                        if (i >= maxPerNote)
                        {
                            shouldDelete = true;
                        }
                        else if (i >= 3 && ver.Timestamp < cutoffDate)
                        {
                            shouldDelete = true;
                        }

                        if (shouldDelete)
                        {
                            try
                            {
                                if (File.Exists(ver.BackupFilePath))
                                {
                                    File.Delete(ver.BackupFilePath);
                                    deletedCount++;
                                }
                            }
                            catch { }
                        }
                    }
                }

                return deletedCount;
            });
        }
    }
}
