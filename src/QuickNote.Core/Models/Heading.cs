using System.Collections.Generic;

namespace QuickNote.Core.Models
{
    public class Heading
    {
        public int Level { get; set; }
        public string Text { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public bool IsExpanded { get; set; } = true;
        public List<Heading> Children { get; set; } = new List<Heading>();

        public override string ToString() => $"{new string('#', Level)} {Text}";
    }
}
