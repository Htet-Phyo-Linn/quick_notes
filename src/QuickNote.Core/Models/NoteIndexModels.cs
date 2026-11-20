using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using QuickNote.Core.Markdown;

namespace QuickNote.Core.Models
{
    public class NoteIndexEntry
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("relativePath")]
        public string RelativePath { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("snippet")]
        public string Snippet { get; set; } = string.Empty;

        [JsonPropertyName("renderText")]
        public string RenderText { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("tags")]
        public string[] Tags { get; set; } = Array.Empty<string>();

        [JsonPropertyName("headings")]
        public string[] Headings { get; set; } = Array.Empty<string>();

        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; }

        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }

        [JsonPropertyName("lastFileWriteUtc")]
        public DateTime LastFileWriteUtc { get; set; }

        [JsonPropertyName("contentHash")]
        public string ContentHash { get; set; } = string.Empty;

        [JsonPropertyName("isDiskSynced")]
        public bool IsDiskSynced { get; set; } = true;

        public NoteDocument ToNoteDocument(string notesRoot, bool loadContent = true)
        {
            var fullPath = Path.Combine(notesRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var cleanRender = !string.IsNullOrWhiteSpace(RenderText) && RenderText != "Empty note"
                ? RenderText
                : (!string.IsNullOrWhiteSpace(Snippet) && Snippet != "Empty note" ? Snippet : null);
            if (string.IsNullOrWhiteSpace(cleanRender) && !string.IsNullOrWhiteSpace(Content))
            {
                cleanRender = MarkdownParser.ComputeRenderText(Content, Title);
            }
            return new NoteDocument
            {
                Id = Id,
                FullPath = fullPath,
                RelativePath = RelativePath,
                FileName = Path.GetFileName(fullPath),
                FolderPath = Path.GetDirectoryName(fullPath) ?? notesRoot,
                Title = Title ?? string.Empty,
                Snippet = cleanRender ?? "Empty note",
                RenderText = cleanRender ?? "Empty note",
                Content = loadContent ? (Content ?? string.Empty) : string.Empty,
                Tags = Tags ?? Array.Empty<string>(),
                Headings = Headings ?? Array.Empty<string>(),
                LastModified = LastModified,
                FileSize = FileSize,
                ContentHash = ContentHash ?? string.Empty
            };
        }

        public static NoteIndexEntry FromNoteDocument(NoteDocument doc, long fileSize, DateTime lastFileWriteUtc, bool isDiskSynced = true)
        {
            var cleanRender = !string.IsNullOrWhiteSpace(doc.RenderText) && doc.RenderText != "Empty note"
                ? doc.RenderText
                : (!string.IsNullOrWhiteSpace(doc.Snippet) && doc.Snippet != "Empty note" ? doc.Snippet : null);
            if (string.IsNullOrWhiteSpace(cleanRender) && !string.IsNullOrWhiteSpace(doc.Content))
            {
                cleanRender = MarkdownParser.ComputeRenderText(doc.Content, doc.Title);
            }
            return new NoteIndexEntry
            {
                Id = doc.Id,
                RelativePath = doc.RelativePath?.Replace('\\', '/') ?? string.Empty,
                Title = doc.Title ?? string.Empty,
                Snippet = cleanRender ?? "Empty note",
                RenderText = cleanRender ?? "Empty note",
                Content = doc.Content ?? string.Empty,
                Tags = doc.Tags ?? Array.Empty<string>(),
                Headings = doc.Headings ?? Array.Empty<string>(),
                LastModified = doc.LastModified,
                FileSize = fileSize,
                LastFileWriteUtc = lastFileWriteUtc,
                ContentHash = doc.ContentHash ?? string.Empty,
                IsDiskSynced = isDiskSynced
            };
        }
    }

    public class NotesIndexCatalog
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("lastUpdatedUtc")]
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("notes")]
        public List<NoteIndexEntry> Notes { get; set; } = new();
    }
}
