using System;
using System.Linq;
using QuickNote.Core.Markdown;
using QuickNote.Core.Models;
using QuickNote.Core.Search;
using Xunit;

namespace QuickNote.Tests.Search
{
    public class SearchEngineTests
    {
        private readonly SearchEngine _engine = new SearchEngine();
        private readonly MarkdownParser _parser = new MarkdownParser();

        [Fact]
        public void Search_RanksExactH1HigherThanBody()
        {
            var doc1 = _parser.Parse("cortex.md", "# Cortex\n\nCortex assessment overview.");
            var doc2 = _parser.Parse("soc.md", "# SOC Operations\n\nMentions cortex in passing.");

            _engine.IndexDocument(doc1);
            _engine.IndexDocument(doc2);

            var results = _engine.Search("Cortex").ToList();

            Assert.True(results.Count >= 2);
            Assert.Equal("Cortex", results[0].NoteTitle); // doc1 should be first
            Assert.True(results[0].Score > results[1].Score);
        }

        [Fact]
        public void Search_FindsNoteByTag()
        {
            var doc = _parser.Parse("incident.md", "---\ntags: [security, incident]\n---\n\n# Incident Report\n\nResolved.");
            _engine.IndexDocument(doc);

            var results = _engine.Search("security").ToList();

            Assert.Single(results);
            Assert.Equal("Incident Report", results[0].NoteTitle);
            Assert.Equal(SearchMatchType.Tag, results[0].MatchType);
        }

        [Fact]
        public void Search_SupportsTagPrefix()
        {
            var doc1 = _parser.Parse("a.md", "---\ntags: [firewall]\n---\n# Rule A\nAllow");
            var doc2 = _parser.Parse("b.md", "# Rule B\nText mentions firewall but no tag");

            _engine.IndexDocument(doc1);
            _engine.IndexDocument(doc2);

            var results = _engine.Search("tag:firewall").ToList();

            Assert.Single(results);
            Assert.Equal("Rule A", results[0].NoteTitle);
        }

        [Fact]
        public void Search_MultiWordQuery_FindsNotesAcrossTitleTagsAndBody()
        {
            var doc1 = _parser.Parse("cmd.md", "---\ntags: [windows]\n---\n# Essentials CMD\nUseful windows command line tools.");
            var doc2 = _parser.Parse("linux.md", "# Linux Shell\nBash command line tools.");

            _engine.IndexDocument(doc1);
            _engine.IndexDocument(doc2);

            // Multi-word query: "windows cmd" matches doc1 (title has CMD, tag and body have windows)
            var results = _engine.Search("windows cmd").ToList();

            Assert.Single(results);
            Assert.Equal("Essentials CMD", results[0].NoteTitle);
        }

        [Fact]
        public void Search_MultiWordQuery_MatchesNonAdjacentWordsInContent()
        {
            var doc = _parser.Parse("logs.md", "# Broker VM\nLots of detailed network connectivity logs.");
            _engine.IndexDocument(doc);

            var results = _engine.Search("broker connectivity").ToList();

            Assert.Single(results);
            Assert.Equal("Broker VM", results[0].NoteTitle);
        }
    }
}
