using System.Threading.Tasks;
using QuickNote.Core.Models;

namespace QuickNote.Core.Interfaces
{
    public interface IFolderService
    {
        Task<FolderNode> BuildFolderTreeAsync(string rootPath);
        Task<FolderNode> BuildFolderTreeAsync(string rootPath, System.Collections.Generic.IDictionary<string, bool>? expandedStates);
        FolderNode BuildFolderTreeFromNotes(string rootPath, System.Collections.Generic.IEnumerable<NoteDocument> notes, System.Collections.Generic.IDictionary<string, bool>? expandedStates = null);
        Task<string> CreateFolderAsync(string parentPath, string folderName);
        Task RenameFolderAsync(string folderPath, string newFolderName);
        Task DeleteFolderAsync(string folderPath);

        Task<string> MoveItemAsync(string sourcePath, string targetDirectoryPath);
        Task<string> CopyItemAsync(string sourcePath, string targetDirectoryPath);
        Task<string> RenameItemAsync(string currentPath, string newName);
        Task DeleteItemAsync(string path, bool isDirectory);
    }
}
