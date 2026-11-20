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

        [Fact]
        public void ComputeRenderText_StripsTitleAndCodeFences_ReturnsCleanBody()
        {
            var content = "# aaa\n\nppp";
            var render = MarkdownParser.ComputeRenderText(content, "aaa");
            Assert.Equal("ppp", render);
        }

        [Fact]
        public void ComputeRenderText_WithCodeBlocksAndHashtags_StripsFences()
        {
            var content = "# CyberArk Users AD Collect\n\n```PowerShell\nsysteminfo | findstr /B / C:\"Domain\"\n```\n#cyberark #ad";
            var render = MarkdownParser.ComputeRenderText(content, "CyberArk Users AD Collect");
            Assert.DoesNotContain("```PowerShell", render);
            Assert.DoesNotContain("```", render);
            Assert.Contains("systeminfo", render);
            Assert.Contains("findstr", render);
        }

        [Fact]
        public void ComputeRenderText_WithHtmlAndTable_StripsRawHtmlAndMarkdown()
        {
            var content = "---\ntags:\n  - cyberark\n---\n# License Renew\n\n<br />\n| Step | Description |\n| 1 | Download new license |";
            var render = MarkdownParser.ComputeRenderText(content, "License Renew");
            Assert.DoesNotContain("<br />", render);
            Assert.DoesNotContain("<br>", render);
            Assert.DoesNotContain("| Step |", render);
            Assert.Contains("Download new license", render);
        }

        [Fact]
        public void ComputeRenderText_EmptyContent_ReturnsEmptyNote()
        {
            Assert.Equal("Empty note", MarkdownParser.ComputeRenderText("", "Untitled"));
            Assert.Equal("Empty note", MarkdownParser.ComputeRenderText(null, "Untitled"));
            Assert.Equal("Empty note", MarkdownParser.ComputeRenderText("   \n\n  ", "Untitled"));
            Assert.Equal("Empty note", MarkdownParser.ComputeRenderText("# Just Title\n\n", "Just Title"));
        }

        [Fact]
        public void Parse_WithFrontMatterUuid_PreservesNoteIdAcrossPaths()
        {
            var expectedId = Guid.NewGuid();
            var content = $"---\nid: {expectedId:D}\ntags: [unit-test]\n---\n# My Note\nContent.";

            var doc1 = _parser.Parse("folder1/note.md", content);
            var doc2 = _parser.Parse("folder2/renamed_note.md", content);

            Assert.Equal(expectedId, doc1.Id);
            Assert.Equal(expectedId, doc2.Id);
            Assert.Equal(doc1.Id, doc2.Id);
        }

        [Fact]
        public void EnsureFrontmatterId_AddsIdWhenMissing_AndPreservesExistingId()
        {
            var targetId = Guid.NewGuid();
            var plainContent = "# Plain Note\nNo frontmatter here.";

            var withId = MarkdownParser.EnsureFrontmatterId(plainContent, targetId);
            Assert.Contains($"id: {targetId:D}", withId);

            var doc = _parser.Parse("plain.md", withId);
            Assert.Equal(targetId, doc.Id);

            // Re-running EnsureFrontmatterId should not duplicate or change the ID
            var anotherId = Guid.NewGuid();
            var unchanged = MarkdownParser.EnsureFrontmatterId(withId, anotherId);
            Assert.Equal(withId, unchanged);
        }
    }
}
