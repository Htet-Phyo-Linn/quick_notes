using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuickNote.Core.Interfaces
{
    public class RecoveryItem
    {
        public string OriginalFilePath { get; set; } = string.Empty;
        public string RecoveryFilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime SavedAt { get; set; }
    }

    public interface IRecoveryService
    {
        Task SaveDraftAsync(string originalFilePath, string content);
        Task ClearDraftAsync(string originalFilePath);
        Task<IEnumerable<RecoveryItem>> CheckForRecoverableItemsAsync();
    }
}
