using System;
using QuickNote.Core;

namespace QuickNote.Core.Markdown.Behaviors
{
    public class IndentationBehavior : IIndentationBehavior
    {
        private const string IndentUnit = "  "; // 2 spaces standard for Markdown

        public bool HandleKeyDown(EditingKey key, string currentLine, ref string newLine)
        {
            return false;
        }

        public bool HandleEnter(string currentLine, ref string newLine)
        {
            // Preserve indentation of the current line
            var trimmed = currentLine.TrimStart();
            var leadingWhitespace = currentLine.Substring(0, currentLine.Length - trimmed.Length);
            if (!string.IsNullOrEmpty(leadingWhitespace) && !string.IsNullOrEmpty(newLine))
            {
                newLine = leadingWhitespace + newLine;
                return true;
            }
            return false;
        }

        public bool HandleTab(string currentLine, ref string newLine)
        {
            // Indent the current line
            newLine = IndentUnit + currentLine;
            return true;
        }

        public bool HandleBackspace(string currentLine, int caretIndex, ref string newLine, ref int newCaretIndex)
        {
            // If the line starts with spaces and caret is within leading indentation
            var trimmed = currentLine.TrimStart();
            var leadingSpaces = currentLine.Length - trimmed.Length;

            if (leadingSpaces > 0 && caretIndex <= leadingSpaces)
            {
                var removeCount = Math.Min(IndentUnit.Length, leadingSpaces);
                newLine = currentLine.Substring(removeCount);
                newCaretIndex = Math.Max(0, caretIndex - removeCount);
                return true;
            }

            return false;
        }
    }
}
