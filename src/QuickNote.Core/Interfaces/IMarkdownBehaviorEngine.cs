using QuickNote.Core;

namespace QuickNote.Core.Interfaces
{
    public interface IMarkdownBehaviorEngine
    {
        void HandleKeyDown(EditingKey key, string currentLine, ref string newLine, ref bool handled);
        void HandleEnter(string currentLine, ref string newLine, ref bool handled);
        void HandleTab(string currentLine, ref string newLine, ref bool handled);
        void HandleBackspace(string currentLine, int caretIndex, ref string newLine, ref int newCaretIndex, ref bool handled);
    }
}