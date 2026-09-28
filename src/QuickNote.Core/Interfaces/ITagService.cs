using System.Collections.Generic;
using QuickNote.Core.Models;

namespace QuickNote.Core.Interfaces
{
    public interface ITagService
    {
        IEnumerable<Tag> GetAllTags(IEnumerable<NoteDocument> documents);
        IEnumerable<NoteDocument> FilterByTag(IEnumerable<NoteDocument> documents, string tag);
    }
}
