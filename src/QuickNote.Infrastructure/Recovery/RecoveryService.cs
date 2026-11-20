using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;

namespace QuickNote.Infrastructure.Recovery
{
    public class RecoveryService : IRecoveryService
    {
        private readonly string _recoveryDirectory;

        public RecoveryService(string notesRoot)
        {
            _recoveryDirectory = Path.Combine(notesRoot, ".recovery");
            if (!Directory.Exists(_recoveryDirectory))
            {
                var dirInfo = Directory.CreateDirectory(_recoveryDirectory);
                if (OperatingSystem.IsWindows())
                {
                    try { dirInfo.Attributes |= FileAttributes.Hidden; } catch { }
                }
            }
        }

        public async Task SaveDraftAsync(string originalFilePath, string content)
        {
            if (string.IsNullOrEmpty(originalFilePath)) return;

            try
            {
                var recoveryFilePath = GetRecoveryFilePath(originalFilePath);
                var header = $"{originalFilePath}\n{DateTime.UtcNow:O}\n";
                await File.WriteAllTextAsync(recoveryFilePath, header + content, Encoding.UTF8);
            }
            catch
            {
                // Ignore transient file I/O collisions during rapid debounced saves
            }
        }

        public Task ClearDraftAsync(string originalFilePath)
        {
            if (string.IsNullOrEmpty(originalFilePath)) return Task.CompletedTask;

            var recoveryFilePath = GetRecoveryFilePath(originalFilePath);
            if (File.Exists(recoveryFilePath))
            {
                try { File.Delete(recoveryFilePath); } catch { }
            }
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<RecoveryItem>> CheckForRecoverableItemsAsync()
        {
            var items = new List<RecoveryItem>();
            if (!Directory.Exists(_recoveryDirectory)) return items;

            foreach (var file in Directory.GetFiles(_recoveryDirectory, "*.draft"))
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(file);
                    if (lines.Length >= 2)
                    {
                        var originalPath = lines[0];
                        if (DateTime.TryParse(lines[1], out var savedAt))
                        {
                            var fullContent = await File.ReadAllTextAsync(file);
                            // Skip header lines
                            var headerLength = lines[0].Length + 1 + lines[1].Length + 1;
                            var content = fullContent.Length > headerLength ? fullContent.Substring(headerLength) : string.Empty;

                            // If draft content is empty, delete it
                            if (string.IsNullOrWhiteSpace(content))
                            {
                                try { File.Delete(file); } catch { }
                                continue;
                            }

                            // If original file exists and already contains the draft content, discard the draft
                            if (File.Exists(originalPath))
                            {
                                var origContent = await File.ReadAllTextAsync(originalPath);
                                var normOrig = origContent.Replace("\r\n", "\n").Trim();
                                var normDraft = content.Replace("\r\n", "\n").Trim();

                                if (string.Equals(normOrig, normDraft, StringComparison.Ordinal) ||
                                    normOrig.EndsWith(normDraft, StringComparison.Ordinal) ||
                                    normOrig.Contains(normDraft, StringComparison.Ordinal))
                                {
                                    try { File.Delete(file); } catch { }
                                    continue;
                                }
                            }

                            items.Add(new RecoveryItem
                            {
                                OriginalFilePath = originalPath,
                                RecoveryFilePath = file,
                                Content = content,
                                SavedAt = savedAt
                            });
                        }
                    }
                }
                catch
                {
                    // Ignore corrupted draft files
                }
            }

            return items;
        }

        private string GetRecoveryFilePath(string originalFilePath)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalFilePath.ToLowerInvariant())));
            return Path.Combine(_recoveryDirectory, $"{hash}.draft");
        }
    }
}
