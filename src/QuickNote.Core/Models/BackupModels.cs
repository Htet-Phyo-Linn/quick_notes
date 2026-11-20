using System;
using System.Collections.Generic;
using System.IO;

namespace QuickNote.Core.Models
{
    public class NoteBackupVersion
    {
        public string OriginalFilePath { get; set; } = string.Empty;
        public string BackupFilePath { get; set; } = string.Empty;
        public string NoteTitle { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public long FileSize { get; set; }
        public bool IsManual { get; set; }

        public string FormattedDate => Timestamp.ToString("MMM d, yyyy");
        public string FormattedTime => Timestamp.ToString("h:mm:ss tt");
        public string FormattedDateTime => $"{FormattedDate} at {FormattedTime}";
        public string TypeBadgeText => IsManual ? "Manual" : "Auto-Save";

        public string FormattedSize
        {
            get
            {
                if (FileSize < 1024) return $"{FileSize} B";
                if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
                return $"{FileSize / (1024.0 * 1024.0):F1} MB";
            }
        }

        public string RelativeTimeText
        {
            get
            {
                var diff = DateTime.Now - Timestamp;
                if (diff.TotalSeconds < 60) return "Just now";
                if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
                if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
                if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
                return FormattedDate;
            }
        }
    }

    public class NoteBackupGroup
    {
        public string OriginalFilePath { get; set; } = string.Empty;
        public string NoteTitle { get; set; } = string.Empty;
        public List<NoteBackupVersion> Versions { get; set; } = new();

        public int VersionCount => Versions.Count;
        public DateTime LatestTimestamp => Versions.Count > 0 ? Versions[0].Timestamp : DateTime.MinValue;
        public string LatestTimestampText => Versions.Count > 0 ? Versions[0].RelativeTimeText : string.Empty;
    }
}
