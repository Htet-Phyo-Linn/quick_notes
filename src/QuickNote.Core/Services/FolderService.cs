using System;
using System.IO;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;

namespace QuickNote.Core.Services
{
    public class FolderService : IFolderService
    {
        public Task<FolderNode> BuildFolderTreeAsync(string rootPath)
        {
            return BuildFolderTreeAsync(rootPath, null);
        }

        public Task<FolderNode> BuildFolderTreeAsync(string rootPath, System.Collections.Generic.IDictionary<string, bool>? expandedStates)
        {
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            var rootInfo = new DirectoryInfo(rootPath);
            var rootNode = new FolderNode
            {
                Name = rootInfo.Name,
                FullPath = rootInfo.FullName,
                RelativePath = string.Empty,
                IsDirectory = true,
                IsExpanded = true
            };

            PopulateSubFolders(rootNode, rootPath, expandedStates);
            return Task.FromResult(rootNode);
        }

        public FolderNode BuildFolderTreeFromNotes(string rootPath, System.Collections.Generic.IEnumerable<NoteDocument> notes, System.Collections.Generic.IDictionary<string, bool>? expandedStates = null)
        {
            var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(rootName)) rootName = "Notes";

            var rootNode = new FolderNode
            {
                Name = rootName,
                FullPath = rootPath,
                RelativePath = string.Empty,
                IsDirectory = true,
                IsExpanded = true
            };

            var folderMap = new System.Collections.Generic.Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
            folderMap[""] = rootNode;

            FolderNode GetOrCreateFolder(string relativeDir)
            {
                var normKey = NormalizePathKey(relativeDir);
                if (folderMap.TryGetValue(normKey, out var existing))
                    return existing;

                var parentDir = Path.GetDirectoryName(relativeDir)?.Replace('\\', '/') ?? string.Empty;
                var parentNode = GetOrCreateFolder(parentDir);

                var dirName = Path.GetFileName(relativeDir);
                bool isExpanded = false;
                if (expandedStates != null && expandedStates.TryGetValue(normKey, out bool savedState))
                {
                    isExpanded = savedState;
                }

                var fullDirPath = Path.Combine(rootPath, relativeDir.Replace('/', Path.DirectorySeparatorChar));
                var folderNode = new FolderNode
                {
                    Name = dirName,
                    FullPath = fullDirPath,
                    RelativePath = normKey,
                    IsDirectory = true,
                    IsExpanded = isExpanded
                };

                parentNode.Children.Add(folderNode);
                folderMap[normKey] = folderNode;
                return folderNode;
            }

            foreach (var note in notes)
            {
                if (note == null) continue;
                var rel = note.RelativePath?.Replace('\\', '/').TrimStart('/') ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rel) && !string.IsNullOrWhiteSpace(note.FullPath))
                {
                    rel = Path.GetRelativePath(rootPath, note.FullPath).Replace('\\', '/').TrimStart('/');
                }

                var relDir = Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? string.Empty;
                var parentFolder = GetOrCreateFolder(relDir);

                var fileNode = new FolderNode
                {
                    Name = Path.GetFileNameWithoutExtension(note.FileName ?? note.FullPath ?? "note"),
                    Title = note.Title ?? string.Empty,
                    FullPath = note.FullPath ?? string.Empty,
                    RelativePath = rel,
                    IsDirectory = false,
                    Note = note
                };
                parentFolder.Children.Add(fileNode);
            }

            void SortNodeRecursive(FolderNode node)
            {
                if (node.Children.Count > 1)
                {
                    var sorted = node.Children
                        .OrderByDescending(c => c.IsDirectory)
                        .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    node.Children.Clear();
                    foreach (var c in sorted)
                        node.Children.Add(c);
                }
                foreach (var child in node.Children)
                {
                    if (child.IsDirectory)
                        SortNodeRecursive(child);
                }
            }

            SortNodeRecursive(rootNode);
            return rootNode;
        }

        public static string NormalizePathKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return path.Replace('\\', '/').Trim('/');
        }

        private void PopulateSubFolders(FolderNode parentNode, string rootPath, System.Collections.Generic.IDictionary<string, bool>? expandedStates = null)
        {
            try
            {
                var dirInfo = new DirectoryInfo(parentNode.FullPath);

                // Add subdirectories
                foreach (var dir in dirInfo.GetDirectories())
                {
                    if (dir.Name.StartsWith(".") || dir.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) || dir.Name.Equals("obj", StringComparison.OrdinalIgnoreCase) || dir.Name.Equals("assets", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var relPath = Path.GetRelativePath(rootPath, dir.FullName);
                    var key = NormalizePathKey(relPath);

                    bool isExpanded = true;
                    if (expandedStates != null && expandedStates.TryGetValue(key, out bool savedState))
                    {
                        isExpanded = savedState;
                    }

                    var folderNode = new FolderNode
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        RelativePath = relPath,
                        IsDirectory = true,
                        IsExpanded = isExpanded
                    };

                    PopulateSubFolders(folderNode, rootPath, expandedStates);
                    parentNode.Children.Add(folderNode);
                }

                // Add markdown files
                foreach (var file in dirInfo.GetFiles("*.md"))
                {
                    var fileNode = new FolderNode
                    {
                        Name = Path.GetFileNameWithoutExtension(file.Name),
                        FullPath = file.FullName,
                        RelativePath = Path.GetRelativePath(rootPath, file.FullName),
                        IsDirectory = false
                    };
                    parentNode.Children.Add(fileNode);
                }
            }
            catch (Exception)
            {
                // Ignore permission issues for specific directories
            }
        }

        public Task<string> CreateFolderAsync(string parentPath, string folderName)
        {
            var targetPath = Path.Combine(parentPath, folderName);
            int counter = 1;
            var baseName = folderName;
            while (Directory.Exists(targetPath))
            {
                targetPath = Path.Combine(parentPath, $"{baseName} ({counter++})");
            }

            Directory.CreateDirectory(targetPath);
            return Task.FromResult(targetPath);
        }

        public Task RenameFolderAsync(string folderPath, string newFolderName)
        {
            var cleanPath = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(cleanPath);
            if (string.IsNullOrEmpty(parent)) throw new InvalidOperationException("Cannot rename root folder");

            var newPath = Path.Combine(parent, newFolderName);
            if (string.Equals(cleanPath, newPath, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            if (string.Equals(cleanPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                var tempPath = cleanPath + "_tmp_" + Guid.NewGuid().ToString("N");
                Directory.Move(cleanPath, tempPath);
                Directory.Move(tempPath, newPath);
                return Task.CompletedTask;
            }

            if (Directory.Exists(newPath))
            {
                throw new InvalidOperationException($"Directory '{newFolderName}' already exists");
            }

            try
            {
                var di = new DirectoryInfo(cleanPath);
                if ((di.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    di.Attributes &= ~FileAttributes.ReadOnly;
                }
            }
            catch { }

            int retries = 5;
            while (true)
            {
                try
                {
                    Directory.Move(cleanPath, newPath);
                    break;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (--retries <= 0) throw;
                    System.Threading.Thread.Sleep(100);
                }
            }

            return Task.CompletedTask;
        }

        public Task DeleteFolderAsync(string folderPath)
        {
            if (Directory.Exists(folderPath))
            {
                int retries = 5;
                while (true)
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(folderPath);
                        NormalizeAttributesRecursively(dirInfo);
                        dirInfo.Delete(recursive: true);
                        break;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        if (--retries <= 0) throw;
                        System.Threading.Thread.Sleep(150);
                    }
                }
            }
            return Task.CompletedTask;
        }

        private static void NormalizeAttributesRecursively(DirectoryInfo dir)
        {
            try
            {
                foreach (var file in dir.GetFiles())
                {
                    try { file.Attributes = FileAttributes.Normal; } catch { }
                }
                foreach (var subDir in dir.GetDirectories())
                {
                    NormalizeAttributesRecursively(subDir);
                }
                dir.Attributes = FileAttributes.Normal;
            }
            catch { }
        }

        public Task<string> MoveItemAsync(string sourcePath, string targetDirectoryPath)
        {
            if (!Directory.Exists(targetDirectoryPath))
            {
                Directory.CreateDirectory(targetDirectoryPath);
            }

            if (File.Exists(sourcePath))
            {
                var fileName = Path.GetFileName(sourcePath);
                var destPath = Path.Combine(targetDirectoryPath, fileName);

                if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(sourcePath);
                }

                if (File.Exists(destPath))
                {
                    var baseName = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    int counter = 1;
                    while (File.Exists(destPath))
                    {
                        destPath = Path.Combine(targetDirectoryPath, $"{baseName} ({counter++}){ext}");
                    }
                }

                File.Move(sourcePath, destPath);
                return Task.FromResult(destPath);
            }
            else if (Directory.Exists(sourcePath))
            {
                // Prevent moving a folder into itself or its own subfolder
                var fullSource = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullTarget = Path.GetFullPath(targetDirectoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(fullSource, fullTarget, StringComparison.OrdinalIgnoreCase) ||
                    fullTarget.StartsWith(fullSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Cannot move a folder into itself or one of its subfolders.");
                }

                var dirName = Path.GetFileName(fullSource);
                var destPath = Path.Combine(targetDirectoryPath, dirName);

                if (string.Equals(fullSource, Path.GetFullPath(destPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(fullSource);
                }

                if (Directory.Exists(destPath))
                {
                    int counter = 1;
                    while (Directory.Exists(destPath))
                    {
                        destPath = Path.Combine(targetDirectoryPath, $"{dirName} ({counter++})");
                    }
                }

                Directory.Move(fullSource, destPath);
                return Task.FromResult(destPath);
            }

            throw new FileNotFoundException($"Source path '{sourcePath}' does not exist.");
        }

        public Task<string> CopyItemAsync(string sourcePath, string targetDirectoryPath)
        {
            if (!Directory.Exists(targetDirectoryPath))
            {
                Directory.CreateDirectory(targetDirectoryPath);
            }

            if (File.Exists(sourcePath))
            {
                var fileName = Path.GetFileName(sourcePath);
                var baseName = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);

                var isSameDir = string.Equals(
                    Path.GetDirectoryName(Path.GetFullPath(sourcePath)),
                    Path.GetFullPath(targetDirectoryPath),
                    StringComparison.OrdinalIgnoreCase);

                string destPath;
                if (isSameDir)
                {
                    destPath = Path.Combine(targetDirectoryPath, $"{baseName} - Copy{ext}");
                    int counter = 1;
                    while (File.Exists(destPath))
                    {
                        destPath = Path.Combine(targetDirectoryPath, $"{baseName} - Copy ({counter++}){ext}");
                    }
                }
                else
                {
                    destPath = Path.Combine(targetDirectoryPath, fileName);
                    int counter = 1;
                    while (File.Exists(destPath))
                    {
                        destPath = Path.Combine(targetDirectoryPath, $"{baseName} ({counter++}){ext}");
                    }
                }

                File.Copy(sourcePath, destPath);
                return Task.FromResult(destPath);
            }
            else if (Directory.Exists(sourcePath))
            {
                var fullSource = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var dirName = Path.GetFileName(fullSource);

                var isSameDir = string.Equals(
                    Path.GetDirectoryName(fullSource),
                    Path.GetFullPath(targetDirectoryPath),
                    StringComparison.OrdinalIgnoreCase);

                string destPath;
                if (isSameDir)
                {
                    destPath = Path.Combine(targetDirectoryPath, $"{dirName} - Copy");
                    int counter = 1;
                    while (Directory.Exists(destPath))
                    {
                        destPath = Path.Combine(targetDirectoryPath, $"{dirName} - Copy ({counter++})");
                    }
                }
                else
                {
                    destPath = Path.Combine(targetDirectoryPath, dirName);
                    int counter = 1;
                    while (Directory.Exists(destPath))
                    {
                        destPath = Path.Combine(targetDirectoryPath, $"{dirName} ({counter++})");
                    }
                }

                CopyDirectoryRecursively(fullSource, destPath);
                return Task.FromResult(destPath);
            }

            throw new FileNotFoundException($"Source path '{sourcePath}' does not exist.");
        }

        public Task<string> RenameItemAsync(string currentPath, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                throw new ArgumentException("Name cannot be empty.", nameof(newName));
            }

            if (File.Exists(currentPath))
            {
                var parent = Path.GetDirectoryName(currentPath)!;
                var ext = Path.GetExtension(currentPath);
                var finalName = newName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? newName : newName + ext;
                var destPath = Path.Combine(parent, finalName);

                if (string.Equals(currentPath, destPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(currentPath);
                }

                if (File.Exists(destPath))
                {
                    throw new InvalidOperationException($"A file named '{finalName}' already exists.");
                }

                File.Move(currentPath, destPath);
                return Task.FromResult(destPath);
            }
            else if (Directory.Exists(currentPath))
            {
                var parent = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrEmpty(parent)) throw new InvalidOperationException("Cannot rename root folder.");

                var destPath = Path.Combine(parent, newName);

                if (string.Equals(currentPath, destPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(currentPath);
                }

                if (Directory.Exists(destPath))
                {
                    throw new InvalidOperationException($"A folder named '{newName}' already exists.");
                }

                Directory.Move(currentPath, destPath);
                return Task.FromResult(destPath);
            }

            throw new FileNotFoundException($"Path '{currentPath}' does not exist.");
        }

        public Task DeleteItemAsync(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            else
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            return Task.CompletedTask;
        }

        private static void CopyDirectoryRecursively(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectoryRecursively(subDir, destSubDir);
            }
        }
    }
}
