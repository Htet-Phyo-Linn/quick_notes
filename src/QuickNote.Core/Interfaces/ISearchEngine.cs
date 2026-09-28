using System;
using System.Collections.Generic;
using QuickNote.Core.Models;

namespace QuickNote.Core.Interfaces
{
    public interface ISearchEngine
    {
        void IndexDocument(NoteDocument document);
        void RemoveDocument(Guid documentId);
        void Clear();
        IEnumerable<SearchResult> Search(string query);
    }
}
