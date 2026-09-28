using System;
using QuickNote.Core.Markdown;
using Xunit;

namespace QuickNote.Tests.Markdown
{
    public class MarkdownParserTests
    {
        private readonly MarkdownParser _parser = new MarkdownParser();

        [Fact]
        public void Parse_WithFrontMatterAndCRLF_ExtractsTagsAndTitle()
        {
            var content = "---\r\ntitle: My Special Note\r\ntags:\r\n  - cybersecurity\r\n  - cortex\r\n---\r\n\r\n# Body Heading\r\n\r\nNote text here.";
            var doc = _parser.Parse("test.md", content);

            Assert.Equal("My Special Note", doc.Title);
            Assert.Contains("cybersecurity", doc.Tags);
            Assert.Contains("cortex", doc.Tags);
            Assert.Contains("Body Heading", doc.Headings);
        }

        [Fact]
        public void Parse_WithInlineTagArray_ExtractsAllTags()
        {
            var content = "---\ntitle: Network Overview\ntags: [ospf, bgp, cisco]\n---\n\nContent.";
            var doc = _parser.Parse("network.md", content);

            Assert.Equal("Network Overview", doc.Title);
            Assert.Equal(3, doc.Tags.Length);
            Assert.Contains("ospf", doc.Tags);
            Assert.Contains("bgp", doc.Tags);
            Assert.Contains("cisco", doc.Tags);
        }

        [Fact]
        public void Parse_WithoutFrontMatter_UsesFirstH1AsTitle()
        {
            var content = "# First H1 Title\n\nSome paragraph text\n\n## Sub heading\n";
            var doc = _parser.Parse("untitled.md", content);

            Assert.Equal("First H1 Title", doc.Title);
            Assert.Contains("First H1 Title", doc.Headings);
            Assert.Contains("Sub heading", doc.Headings);
        }

        [Fact]
        public void Parse_WithInlineHashtags_ExtractsTagsFromContent()
        {
            var content = "# Project Roadmap\n\nRemember to test #deployment and #performance_metrics!\nCheck #bug-fix later.\n";
            var doc = _parser.Parse("roadmap.md", content);

            Assert.Equal("Project Roadmap", doc.Title);
            Assert.Contains("deployment", doc.Tags);
            Assert.Contains("performance_metrics", doc.Tags);
            Assert.Contains("bug-fix", doc.Tags);
        }
    }
}
