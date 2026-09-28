using System;

namespace QuickNote.Core.Models
{
    public enum SearchMatchType
    {
        ExactH1,
        ExactH2,
        ExactH3,
        HeadingContains,
        Filename,
        Folder,
        Tag,
        Body
    }

    public class SearchResult
    {
        public Guid DocumentId { get; set; }
        public string NoteTitle { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public SearchMatchType MatchType { get; set; }
        public string MatchedText { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public int Score { get; set; }

        public override string ToString() => $"[{Score}] {NoteTitle} ({MatchType}: {MatchedText})";
    }
}
