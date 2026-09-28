using System.IO;
using QuickNote.Core.Utilities;
using Xunit;

namespace QuickNote.Tests.Utilities
{
    public class FileNameSanitizerTests
    {
        [Theory]
        [InlineData("", "Untitled")]
        [InlineData("   ", "Untitled")]
        [InlineData(null, "Untitled")]
        [InlineData("My Great Note", "My Great Note")]
        [InlineData("Note: Subtitle", "Note Subtitle")]
        [InlineData("Project / Plan \\ 2026", "Project Plan 2026")]
        [InlineData("What is <this>?", "What is this")]
        [InlineData("Quote \"Hello\" *star* |pipe|", "Quote Hello star pipe")]
        public void Sanitize_ValidAndInvalidInput_ProducesCleanFileName(string? input, string expected)
        {
            var result = FileNameSanitizer.Sanitize(input!);
            Assert.Equal(expected, result);

            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var ch in result)
            {
                Assert.DoesNotContain(ch, invalidChars);
            }
        }
    }
}
