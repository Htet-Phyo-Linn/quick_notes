using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuickNote.Core.Interfaces
{
    public class TrashItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string OriginalPath { get; set; } = string.Empty;
        public string TrashPath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public DateTime DeletedAt { get; set; } = DateTime.Now;
    }

    public interface ITrashBinService
    {
        Task<TrashItem?> MoveToTrashAsync(string path);
        Task<IEnumerable<TrashItem>> GetTrashItemsAsync();
        Task<bool> RestoreItemAsync(TrashItem item);
        Task<bool> PermanentDeleteAsync(TrashItem item);
        Task EmptyTrashAsync();
        void UpdateNotesRoot(string notesRoot);
    }
}
