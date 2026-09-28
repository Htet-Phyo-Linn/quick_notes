using QuickNote.Core;

namespace QuickNote.Core.Markdown.Behaviors
{
    public interface IIndentationBehavior
    {
        bool HandleKeyDown(EditingKey key, string currentLine, ref string newLine);
        bool HandleEnter(string currentLine, ref string newLine);
        bool HandleTab(string currentLine, ref string newLine);
        bool HandleBackspace(string currentLine, int caretIndex, ref string newLine, ref int newCaretIndex);
    }
}