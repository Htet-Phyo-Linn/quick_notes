using System.Text.RegularExpressions;
using QuickNote.Core;

namespace QuickNote.Core.Markdown.Behaviors
{
    public class OrderedListBehavior : IOrderedListBehavior
    {
        public bool HandleKeyDown(EditingKey key, string currentLine, ref string newLine)
        {
            return false;
        }

        public bool HandleEnter(string currentLine, ref string newLine)
        {
            var trimmedLine = currentLine.TrimStart();
            var leadingWhitespace = currentLine.Substring(0, currentLine.Length - trimmedLine.Length);

            var match = Regex.Match(trimmedLine, @"^(\d+)\.\s+(.*)");
            if (match.Success)
            {
                var number = int.Parse(match.Groups[1].Value);
                var text = match.Groups[2].Value;

                // If the line is empty after the number, terminate list
                if (string.IsNullOrWhiteSpace(text))
                {
                    newLine = string.Empty;
                    return true;
                }

                // Continue the ordered list with next number
                newLine = $"{leadingWhitespace}{number + 1}. ";
                return true;
            }

            return false;
        }

        public bool HandleBackspace(string currentLine, int caretIndex, ref string newLine, ref int newCaretIndex)
        {
            var trimmedLine = currentLine.TrimStart();
            var leadingSpaces = currentLine.Length - trimmedLine.Length;

            var match = Regex.Match(trimmedLine, @"^(\d+)\.\s*");
            if (match.Success)
            {
                var markerLength = match.Length;
                if (caretIndex >= leadingSpaces && caretIndex <= leadingSpaces + markerLength)
                {
                    newLine = currentLine.Substring(leadingSpaces + markerLength);
                    newCaretIndex = leadingSpaces;
                    return true;
                }
            }

            return false;
        }
    }
}