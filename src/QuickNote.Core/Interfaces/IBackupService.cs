using System.Collections.Generic;
using System.Threading.Tasks;
using QuickNote.Core.Models;

namespace QuickNote.Core.Interfaces
{
    public interface IBackupService
    {
        string BackupDirectory { get; }
        Task<List<NoteBackupGroup>> GetBackupGroupsAsync();
        Task<string> ReadBackupContentAsync(string backupFilePath);
        Task<bool> RestoreBackupAsync(string targetNotePath, string backupFilePath);
        Task<string?> CreateManualBackupAsync(string notePath);
        Task<int> PruneBackupsAsync(int maxPerNote = 20, int maxAgeDays = 30);
        void UpdateNotesRoot(string newNotesRoot);
    }
}
