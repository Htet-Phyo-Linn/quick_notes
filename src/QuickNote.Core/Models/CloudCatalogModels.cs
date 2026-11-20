using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QuickNote.Core.Models
{
    /// <summary>
    /// Central catalog JSON schema stored in Google Drive (quicknote_catalog.json).
    /// Enables 1-call delta sync between Windows and Android.
    /// </summary>
    public class CloudCatalog
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("catalog_revision")]
        public long CatalogRevision { get; set; } = 1;

        [JsonPropertyName("catalog_id")]
        public string CatalogId { get; set; } = Guid.NewGuid().ToString("D");

        [JsonPropertyName("last_updated_utc")]
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("updated_by_device")]
        public string UpdatedByDevice { get; set; } = Environment.MachineName;

        [JsonPropertyName("folders")]
        public Dictionary<string, string> Folders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("notes")]
        public Dictionary<string, CloudNoteEntry> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("tombstones")]
        public Dictionary<string, CloudTombstoneEntry> Tombstones { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class CloudNoteEntry
    {
        [JsonPropertyName("note_id")]
        public string NoteId { get; set; } = string.Empty;

        [JsonPropertyName("relative_path")]
        public string RelativePath { get; set; } = string.Empty;

        [JsonPropertyName("drive_file_id")]
        public string DriveFileId { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("content_hash")]
        public string ContentHash { get; set; } = string.Empty;

        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("last_modified_utc")]
        public DateTime LastModifiedUtc { get; set; }

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("revision")]
        public long Revision { get; set; } = 1;
    }

    public class CloudTombstoneEntry
    {
        [JsonPropertyName("relative_path")]
        public string RelativePath { get; set; } = string.Empty;

        [JsonPropertyName("deleted_utc")]
        public DateTime DeletedUtc { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("deleted_by_device")]
        public string DeletedByDevice { get; set; } = string.Empty;
    }
}
