using QuickNote.Core;
using QuickNote.Core.Markdown.Behaviors;
using Xunit;

namespace QuickNote.Tests.Markdown
{
    public class MarkdownBehaviorEngineTests
    {
        private readonly MarkdownBehaviorEngine _engine;

        public MarkdownBehaviorEngineTests()
        {
            _engine = new MarkdownBehaviorEngine(
                new CheckboxBehavior(),
                new OrderedListBehavior(),
                new BulletListBehavior(),
                new QuoteBehavior(),
                new IndentationBehavior());
        }

        [Theory]
        [InlineData("1. First item", "2. ")]
        [InlineData("99. Item", "100. ")]
        [InlineData("  1. Indented item", "  2. ")]
        public void HandleEnter_OrderedList_AutoIncrementsNumber(string input, string expectedNextLine)
        {
            string newLine = string.Empty;
            bool handled = false;

            _engine.HandleEnter(input, ref newLine, ref handled);

            Assert.True(handled);
            Assert.Equal(expectedNextLine, newLine);
        }

        [Fact]
        public void HandleEnter_EmptyOrderedList_TerminatesList()
        {
            string newLine = "temporary";
            bool handled = false;

            _engine.HandleEnter("1. ", ref newLine, ref handled);

            Assert.True(handled);
            Assert.Equal(string.Empty, newLine);
        }

        [Theory]
        [InlineData("- Bullet item", "- ")]
        [InlineData("* Star item", "* ")]
        [InlineData("  - Nested bullet", "  - ")]
        public void HandleEnter_BulletList_ContinuesBullet(string input, string expectedNextLine)
        {
            string newLine = string.Empty;
            bool handled = false;

            _engine.HandleEnter(input, ref newLine, ref handled);

            Assert.True(handled);
            Assert.Equal(expectedNextLine, newLine);
        }

        [Fact]
        public void HandleEnter_EmptyBullet_TerminatesList()
        {
            string newLine = "temporary";
            bool handled = false;

            _engine.HandleEnter("- ", ref newLine, ref handled);

            Assert.True(handled);
            Assert.Equal(string.Empty, newLine);
        }

        [Fact]
        public void HandleEnter_CheckboxItem_ContinuesUncheckedCheckbox()
        {
            string newLine = string.Empty;
            bool handled = false;

            _engine.HandleEnter("- [x] Done task", ref newLine, ref handled);

            Assert.True(handled);
            Assert.Equal("- [ ] ", newLine);
        }

        [Fact]
        public void HandleEnter_EmptyCheckbox_TerminatesList()
        {
            string newLine = "temporary";
            bool handled = false;

            _engine.HandleEnter("- [ ] ", ref newLine, ref handled);

            Assert.True(handled);
            Assert.Equal(string.Empty, newLine);
        }

        [Fact]
        public void HandleEnter_Quote_ContinuesQuote()
        {
            string newLine = string.Empty;
            bool handled = false;

            _engine.HandleEnter("> Important quote", ref newLine, ref handled);

            Assert.True(handled);
            Assert.Equal("> ", newLine);
        }

        [Fact]
        public void HandleEnter_EmptyQuote_TerminatesQuote()
        {
            string newLine = "temporary";
            bool handled = false;

            _engine.HandleEnter("> ", ref newLine, ref handled);

            Assert.True(handled);
            Assert.Equal(string.Empty, newLine);
        }

        [Fact]
        public void HandleTab_IndentsCurrentLine()
        {
            string newLine = string.Empty;
            bool handled = false;

            _engine.HandleTab("- List item", ref newLine, ref handled);

            Assert.True(handled);
            Assert.Equal("  - List item", newLine);
        }

        [Fact]
        public void HandleBackspace_AtMarkerStart_CleansMarker()
        {
            string currentLine = "- Item";
            string newLine = string.Empty;
            int newCaret = 0;
            bool handled = false;

            _engine.HandleBackspace(currentLine, 2, ref newLine, ref newCaret, ref handled);

            Assert.True(handled);
            Assert.Equal("Item", newLine);
            Assert.Equal(0, newCaret);
        }
    }
}
