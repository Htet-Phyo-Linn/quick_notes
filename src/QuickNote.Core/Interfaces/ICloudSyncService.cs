using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuickNote.Core.Interfaces
{
    public class OneDriveAccountInfo
    {
        public string AccountName { get; set; } = string.Empty;
        public string UserFolder { get; set; } = string.Empty;
        public bool IsBusiness { get; set; }
        public string DefaultNotesPath => System.IO.Path.Combine(UserFolder, "QuickNote");
    }

    public enum CloudSyncState
    {
        Disabled,
        Syncing,
        Synced,
        Warning,
        Error
    }

    public class CloudSyncStatus
    {
        public CloudSyncState State { get; set; } = CloudSyncState.Disabled;
        public string Message { get; set; } = "Sync disabled";
        public string? ActiveNotesPath { get; set; }
        public string? AccountName { get; set; }
        public DateTime? LastSyncTime { get; set; }
    }

    public interface ICloudSyncService
    {
        List<OneDriveAccountInfo> DetectOneDriveAccounts();
        Task<bool> EnableOneDriveSyncAsync(OneDriveAccountInfo account, bool migrateExistingNotes, string currentNotesRoot);
        Task<bool> DisableOneDriveSyncAsync(string localDocumentsNotesRoot);
        CloudSyncStatus GetSyncStatus();
        event EventHandler<CloudSyncStatus>? SyncStatusChanged;
    }
}
