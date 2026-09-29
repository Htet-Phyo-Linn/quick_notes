using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;

namespace QuickNote.Infrastructure.Services
{
    public class TrashBinService : ITrashBinService
    {
        private string _notesRoot;
        private string _trashDirectory;
        private string _indexFilePath;

        public TrashBinService(string notesRoot)
        {
            _notesRoot = notesRoot ?? throw new ArgumentNullException(nameof(notesRoot));
            _trashDirectory = Path.Combine(_notesRoot, ".trash");
            _indexFilePath = Path.Combine(_trashDirectory, ".trash_index.json");
            EnsureTrashDirectory();
        }

        public void UpdateNotesRoot(string notesRoot)
        {
            if (!string.IsNullOrWhiteSpace(notesRoot))
            {
                _notesRoot = notesRoot;
                _trashDirectory = Path.Combine(_notesRoot, ".trash");
                _indexFilePath = Path.Combine(_trashDirectory, ".trash_index.json");
                EnsureTrashDirectory();
            }
        }

        private void EnsureTrashDirectory()
        {
            if (!Directory.Exists(_trashDirectory))
            {
                var dir = Directory.CreateDirectory(_trashDirectory);
                try { dir.Attributes |= FileAttributes.Hidden; } catch { }
            }
        }

        private async Task<List<TrashItem>> LoadIndexAsync()
        {
            EnsureTrashDirectory();
            if (!File.Exists(_indexFilePath)) return new List<TrashItem>();

            try
            {
                var json = await File.ReadAllTextAsync(_indexFilePath);
                var list = JsonSerializer.Deserialize<List<TrashItem>>(json);
                return list ?? new List<TrashItem>();
            }
            catch
            {
                return new List<TrashItem>();
            }
        }

        private async Task SaveIndexAsync(List<TrashItem> index)
        {
            EnsureTrashDirectory();
            try
            {
                var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_indexFilePath, json);
            }
            catch { }
        }

        public async Task<TrashItem?> MoveToTrashAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            bool isDir = Directory.Exists(path);
            bool isFile = File.Exists(path);
            if (!isDir && !isFile) return null;

            EnsureTrashDirectory();
            var index = await LoadIndexAsync();

            string id = Guid.NewGuid().ToString("N");
            string title = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(title)) title = "Untitled";

            string ext = isFile ? Path.GetExtension(path) : "";
            string trashFileName = isDir ? $"{id}_dir" : $"{id}{ext}";
            string trashPath = Path.Combine(_trashDirectory, trashFileName);

            try
            {
                if (isDir)
                {
                    int retries = 3;
                    while (true)
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(path);
                            NormalizeAttributes(dirInfo);
                            Directory.Move(path, trashPath);
                            break;
                        }
                        catch (Exception)
                        {
                            if (--retries <= 0)
                            {
                                // Fallback: copy and delete
                                CopyAndClean(path, trashPath);
                                break;
                            }
                            System.Threading.Thread.Sleep(100);
                        }
                    }
                }
                else
                {
                    try
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                    }
                    catch { }
                    File.Move(path, trashPath, overwrite: true);
                }

                var item = new TrashItem
                {
                    Id = id,
                    OriginalPath = path,
                    TrashPath = trashPath,
                    Title = title,
                    IsDirectory = isDir,
                    DeletedAt = DateTime.Now
                };

                index.Add(item);
                await SaveIndexAsync(index);
                return item;
            }
            catch
            {
                return null;
            }
        }

        private static void NormalizeAttributes(DirectoryInfo dir)
        {
            try
            {
                foreach (var file in dir.GetFiles())
                {
                    try { file.Attributes = FileAttributes.Normal; } catch { }
                }
                foreach (var subDir in dir.GetDirectories())
                {
                    NormalizeAttributes(subDir);
                }
                dir.Attributes = FileAttributes.Normal;
            }
            catch { }
        }

        private static void CopyAndClean(string sourceDir, string targetDir)
        {
            var source = new DirectoryInfo(sourceDir);
            var target = new DirectoryInfo(targetDir);
            Directory.CreateDirectory(target.FullName);

            foreach (var file in source.GetFiles())
            {
                var destFile = Path.Combine(target.FullName, file.Name);
                file.CopyTo(destFile, true);
                try { file.Attributes = FileAttributes.Normal; file.Delete(); } catch { }
            }

            foreach (var subDir in source.GetDirectories())
            {
                var nextTarget = target.CreateSubdirectory(subDir.Name);
                CopyAndClean(subDir.FullName, nextTarget.FullName);
            }

            try
            {
                source.Attributes = FileAttributes.Normal;
                source.Delete(true);
            }
            catch { }
        }

        public async Task<IEnumerable<TrashItem>> GetTrashItemsAsync()
        {
            var index = await LoadIndexAsync();
            var valid = new List<TrashItem>();
            foreach (var item in index)
            {
                if (item.IsDirectory ? Directory.Exists(item.TrashPath) : File.Exists(item.TrashPath))
                {
                    valid.Add(item);
                }
            }
            return valid.OrderByDescending(x => x.DeletedAt);
        }

        public async Task<bool> RestoreItemAsync(TrashItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.OriginalPath)) return false;

            try
            {
                var targetDir = Path.GetDirectoryName(item.OriginalPath);
                if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                if (item.IsDirectory && Directory.Exists(item.TrashPath))
                {
                    Directory.Move(item.TrashPath, item.OriginalPath);
                }
                else if (!item.IsDirectory && File.Exists(item.TrashPath))
                {
                    File.Move(item.TrashPath, item.OriginalPath, overwrite: true);
                }
                else
                {
                    return false;
                }

                var index = await LoadIndexAsync();
                index.RemoveAll(x => x.Id == item.Id);
                await SaveIndexAsync(index);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> PermanentDeleteAsync(TrashItem item)
        {
            if (item == null) return false;

            try
            {
                if (item.IsDirectory && Directory.Exists(item.TrashPath))
                {
                    Directory.Delete(item.TrashPath, recursive: true);
                }
                else if (!item.IsDirectory && File.Exists(item.TrashPath))
                {
                    File.Delete(item.TrashPath);
                }

                var index = await LoadIndexAsync();
                index.RemoveAll(x => x.Id == item.Id);
                await SaveIndexAsync(index);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task EmptyTrashAsync()
        {
            EnsureTrashDirectory();
            try
            {
                var dir = new DirectoryInfo(_trashDirectory);
                foreach (var file in dir.GetFiles())
                {
                    try { file.Delete(); } catch { }
                }
                foreach (var subDir in dir.GetDirectories())
                {
                    try { subDir.Delete(true); } catch { }
                }

                await SaveIndexAsync(new List<TrashItem>());
            }
            catch { }
        }
    }
}
