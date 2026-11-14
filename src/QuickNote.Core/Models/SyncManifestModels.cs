using System;
using System.Collections.Generic;

namespace QuickNote.Core.Models
{
    public enum SyncStatus
    {
        Synced,
        PendingUpload,
        PendingDownload,
        Conflict,
        PendingDelete
    }

    public class SyncFileEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public string LocalHash { get; set; } = string.Empty;
        public DateTime LocalLastModified { get; set; }
        public string RemoteId { get; set; } = string.Empty;
        public string RemoteETag { get; set; } = string.Empty;
        public DateTime RemoteLastModified { get; set; }
        public SyncStatus Status { get; set; } = SyncStatus.Synced;
    }

    public class SyncManifest
    {
        public int Version { get; set; } = 1;
        public string? QuickNoteFolderId { get; set; }
        public DateTime LastSyncTime { get; set; }
        public Dictionary<string, SyncFileEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public int UploadedCount { get; set; }
        public int DownloadedCount { get; set; }
        public int ConflictCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Error { get; set; }

        public static SyncResult Ok(int uploaded, int downloaded, int conflicts, string message = "Sync completed") =>
            new() { Success = true, UploadedCount = uploaded, DownloadedCount = downloaded, ConflictCount = conflicts, Message = message };

        public static SyncResult Fail(string message, Exception? ex = null) =>
            new() { Success = false, Message = message, Error = ex };
    }
}
