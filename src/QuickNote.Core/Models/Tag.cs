using System;
using System.Collections.Generic;

namespace QuickNote.Core.Models
{
    public class Tag
    {
        public string Name { get; set; } = string.Empty;
        public int Count => NoteIds.Count;
        public HashSet<Guid> NoteIds { get; } = new HashSet<Guid>();

        public Tag(string name)
        {
            Name = name;
        }

        public override string ToString() => $"#{Name} ({Count})";
    }
}
