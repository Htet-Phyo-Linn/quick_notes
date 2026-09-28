using System;
using QuickNote.Core;
using QuickNote.Core.Interfaces;

namespace QuickNote.Core.Markdown.Behaviors
{
    public class MarkdownBehaviorEngine : IMarkdownBehaviorEngine
    {
        private readonly ICheckboxBehavior _checkboxBehavior;
        private readonly IOrderedListBehavior _orderedListBehavior;
        private readonly IBulletListBehavior _bulletListBehavior;
        private readonly IQuoteBehavior _quoteBehavior;
        private readonly IIndentationBehavior _indentationBehavior;

        public MarkdownBehaviorEngine(
            ICheckboxBehavior checkboxBehavior,
            IOrderedListBehavior orderedListBehavior,
            IBulletListBehavior bulletListBehavior,
            IQuoteBehavior quoteBehavior,
            IIndentationBehavior indentationBehavior)
        {
            _checkboxBehavior = checkboxBehavior ?? throw new ArgumentNullException(nameof(checkboxBehavior));
            _orderedListBehavior = orderedListBehavior ?? throw new ArgumentNullException(nameof(orderedListBehavior));
            _bulletListBehavior = bulletListBehavior ?? throw new ArgumentNullException(nameof(bulletListBehavior));
            _quoteBehavior = quoteBehavior ?? throw new ArgumentNullException(nameof(quoteBehavior));
            _indentationBehavior = indentationBehavior ?? throw new ArgumentNullException(nameof(indentationBehavior));
        }

        public void HandleKeyDown(EditingKey key, string currentLine, ref string newLine, ref bool handled)
        {
            handled = _checkboxBehavior.HandleKeyDown(key, currentLine, ref newLine) ||
                      _orderedListBehavior.HandleKeyDown(key, currentLine, ref newLine) ||
                      _bulletListBehavior.HandleKeyDown(key, currentLine, ref newLine) ||
                      _quoteBehavior.HandleKeyDown(key, currentLine, ref newLine) ||
                      _indentationBehavior.HandleKeyDown(key, currentLine, ref newLine);
        }

        public void HandleEnter(string currentLine, ref string newLine, ref bool handled)
        {
            handled = _checkboxBehavior.HandleEnter(currentLine, ref newLine) ||
                      _orderedListBehavior.HandleEnter(currentLine, ref newLine) ||
                      _bulletListBehavior.HandleEnter(currentLine, ref newLine) ||
                      _quoteBehavior.HandleEnter(currentLine, ref newLine) ||
                      _indentationBehavior.HandleEnter(currentLine, ref newLine);
        }

        public void HandleTab(string currentLine, ref string newLine, ref bool handled)
        {
            handled = _indentationBehavior.HandleTab(currentLine, ref newLine);
        }

        public void HandleBackspace(string currentLine, int caretIndex, ref string newLine, ref int newCaretIndex, ref bool handled)
        {
            handled = _checkboxBehavior.HandleBackspace(currentLine, caretIndex, ref newLine, ref newCaretIndex) ||
                      _orderedListBehavior.HandleBackspace(currentLine, caretIndex, ref newLine, ref newCaretIndex) ||
                      _bulletListBehavior.HandleBackspace(currentLine, caretIndex, ref newLine, ref newCaretIndex) ||
                      _quoteBehavior.HandleBackspace(currentLine, caretIndex, ref newLine, ref newCaretIndex) ||
                      _indentationBehavior.HandleBackspace(currentLine, caretIndex, ref newLine, ref newCaretIndex);
        }
    }
}