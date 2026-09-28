using System;
using System.Collections.Generic;
using System.Linq;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;

namespace QuickNote.Core.Services
{
    public class TagService : ITagService
    {
        public IEnumerable<Tag> GetAllTags(IEnumerable<NoteDocument> documents)
        {
            var dict = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase);

            foreach (var doc in documents)
            {
                if (doc.Tags == null) continue;

                foreach (var tagName in doc.Tags)
                {
                    if (string.IsNullOrWhiteSpace(tagName)) continue;
                    var cleanTag = tagName.Trim();

                    if (!dict.TryGetValue(cleanTag, out var tag))
                    {
                        tag = new Tag(cleanTag);
                        dict[cleanTag] = tag;
                    }
                    tag.NoteIds.Add(doc.Id);
                }
            }

            return dict.Values.OrderByDescending(t => t.Count).ThenBy(t => t.Name);
        }

        public IEnumerable<NoteDocument> FilterByTag(IEnumerable<NoteDocument> documents, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return documents;

            return documents.Where(doc =>
                doc.Tags != null && doc.Tags.Any(t => string.Equals(t.Trim(), tag.Trim(), StringComparison.OrdinalIgnoreCase)));
        }
    }
}
