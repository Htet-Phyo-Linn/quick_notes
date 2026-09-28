using QuickNote.Core.Models;

namespace QuickNote.Core.Interfaces
{
    public interface IMarkdownParser
    {
        NoteDocument Parse(string filePath, string content);
    }
}