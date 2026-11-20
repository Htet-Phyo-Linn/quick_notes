using System;
using System.Collections.Generic;

namespace QuickNote.Core.Models
{
    public class NoteDocument
    {
        public Guid Id { get; set; }
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public string RenderText { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string[] Headings { get; set; } = Array.Empty<string>();
        public List<Heading> HeadingTree { get; set; } = new List<Heading>();
        public string[] Tags { get; set; } = Array.Empty<string>();
        public DateTime LastModified { get; set; }
        public long FileSize { get; set; }
        public string ContentHash { get; set; } = string.Empty;

        public int WordCount
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Content)) return 0;
                var count = 0;
                var inWord = false;
                for (int i = 0; i < Content.Length; i++)
                {
                    if (char.IsWhiteSpace(Content[i]))
                    {
                        inWord = false;
                    }
                    else if (!inWord)
                    {
                        inWord = true;
                        count++;
                    }
                }
                return count;
            }
        }

        public int LineCount
        {
            get
            {
                if (string.IsNullOrEmpty(Content)) return 0;
                var count = 1;
                for (int i = 0; i < Content.Length; i++)
                {
                    if (Content[i] == '\n') count++;
                }
                return count;
            }
        }
    }
}