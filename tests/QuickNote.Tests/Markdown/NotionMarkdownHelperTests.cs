using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using QuickNote.App.Helpers;
using Xunit;

namespace QuickNote.Tests.Markdown
{
    public class NotionMarkdownHelperTests
    {
        private static void RunOnSta(Action action)
        {
            Exception? ex = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception e) { ex = e; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (ex != null) ExceptionDispatchInfo.Capture(ex).Throw();
        }

        [Theory]
        [InlineData("cs", "csharp")]
        [InlineData("c#", "csharp")]
        [InlineData("py", "python")]
        [InlineData("python", "python")]
        [InlineData("js", "javascript")]
        [InlineData("ts", "typescript")]
        [InlineData("ps1", "powershell")]
        [InlineData("powershell", "powershell")]
        [InlineData("sh", "bash")]
        [InlineData("bash", "bash")]
        [InlineData("sql", "sql")]
        [InlineData("json", "json")]
        [InlineData("unknown_lang", "unknown_lang")]
        public void SyntaxHighlighter_NormalizeLanguage_NormalizesAliases(string alias, string expected)
        {
            var result = SyntaxHighlighter.NormalizeLanguage(alias);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void SyntaxHighlighter_Highlight_ProducesColoredInlines()
        {
            RunOnSta(() =>
            {
                string code = "int count = 42;\nstring text = \"hello\";";
                var inlines = SyntaxHighlighter.Highlight(code, "csharp");

                Assert.NotEmpty(inlines);
                var runs = inlines.OfType<Run>().ToList();
                Assert.Contains(runs, r => r.Text == "int");
                Assert.Contains(runs, r => r.Text == "42");
                Assert.Contains(runs, r => r.Text == "\"hello\"");
                Assert.Contains(inlines, i => i is LineBreak);
            });
        }

        [Fact]
        public void GetBulletMarker_ReturnsJoplinStyleHierarchy()
        {
            Assert.Equal("• ", NotionMarkdownHelper.GetBulletMarker(0));
            Assert.Equal("◦ ", NotionMarkdownHelper.GetBulletMarker(1));
            Assert.Equal("▪ ", NotionMarkdownHelper.GetBulletMarker(2));
            Assert.Equal("▫ ", NotionMarkdownHelper.GetBulletMarker(3));
            Assert.Equal("▫ ", NotionMarkdownHelper.GetBulletMarker(4));
        }

        [Fact]
        public void OrderedList_Roundtrip_PreservesNumberedItems()
        {
            RunOnSta(() =>
            {
                string markdown = "1. First item\n2. Second item\n3. Third item";
                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(markdown);

                Assert.Equal(3, doc.Blocks.Count);
                foreach (var block in doc.Blocks)
                {
                    Assert.IsType<Paragraph>(block);
                    var p = (Paragraph)block;
                    Assert.Equal(NotionMarkdownHelper.TagOrderedList, p.Tag);
                }

                string serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);
                Assert.Contains("1. First item", serialized);
                Assert.Contains("2. Second item", serialized);
                Assert.Contains("3. Third item", serialized);
            });
        }

        [Fact]
        public void JoplinSubBullets_Roundtrip_PreservesIndentationAndMarkers()
        {
            RunOnSta(() =>
            {
                string markdown = "- Root item\n  - Level 1 item\n    - Level 2 item";
                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(markdown);

                Assert.Equal(3, doc.Blocks.Count);
                var p0 = (Paragraph)doc.Blocks.ElementAt(0);
                var p1 = (Paragraph)doc.Blocks.ElementAt(1);
                var p2 = (Paragraph)doc.Blocks.ElementAt(2);

                Assert.Equal(NotionMarkdownHelper.TagBullet, p0.Tag);
                Assert.Equal(NotionMarkdownHelper.TagBullet, p1.Tag);
                Assert.Equal(NotionMarkdownHelper.TagBullet, p2.Tag);

                // Margin checks (12 + level * 24)
                Assert.Equal(12, p0.Margin.Left);
                Assert.Equal(36, p1.Margin.Left);
                Assert.Equal(60, p2.Margin.Left);

                string serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);
                Assert.Contains("- Root item", serialized);
                Assert.Contains("  - Level 1 item", serialized);
                Assert.Contains("    - Level 2 item", serialized);
            });
        }

        [Fact]
        public void MultilineCodeBlock_Roundtrip_PreservesLanguageAndContent()
        {
            RunOnSta(() =>
            {
                string codeBlock = "```python\ndef greet(name):\n    print(f\"Hello {name}\")\n```";
                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(codeBlock);

                Assert.NotEmpty(doc.Blocks);
                var container = doc.Blocks.FirstOrDefault(b => b.Tag?.ToString()?.StartsWith("CodeBlock") == true);
                Assert.NotNull(container);
                Assert.Equal("CodeBlock:python", container.Tag);

                // Extract code directly
                string extractedCode = NotionMarkdownHelper.ExtractCodeFromBlock(container);
                Assert.Contains("def greet(name):", extractedCode);
                Assert.Contains("print(f\"Hello {name}\")", extractedCode);

                string serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);
                Assert.StartsWith("```python", serialized);
                Assert.Contains("def greet(name):", serialized);
                Assert.EndsWith("```", serialized.TrimEnd());
            });
        }
        [Fact]
        public void InlineMarkdown_BoldAndInlineCode_Roundtrip_PreservesFormatting()
        {
            RunOnSta(() =>
            {
                string markdown = "Your **network and SSH handshake are completely successful**. Convert the `.ppk` key.";
                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(markdown);

                Assert.NotEmpty(doc.Blocks);
                var p = doc.Blocks.OfType<Paragraph>().FirstOrDefault();
                Assert.NotNull(p);

                // Assert bold and inline code exist in Inlines
                var bold = p.Inlines.OfType<Bold>().FirstOrDefault();
                Assert.NotNull(bold);
                var boldRun = bold.Inlines.OfType<Run>().FirstOrDefault();
                Assert.NotNull(boldRun);
                Assert.Equal("network and SSH handshake are completely successful", boldRun.Text);

                var span = p.Inlines.OfType<Span>().FirstOrDefault(s => s.Tag as string == "InlineCode");
                Assert.NotNull(span);
                var codeRun = span.Inlines.OfType<Run>().FirstOrDefault();
                Assert.NotNull(codeRun);
                Assert.Equal(".ppk", codeRun.Text);

                // Round-trip back to markdown
                string serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);
                Assert.Contains("**network and SSH handshake are completely successful**", serialized);
                Assert.Contains("`.ppk`", serialized);
            });
        }

        [Fact]
        public void Heading4To6_ParsedAsHeadings()
        {
            RunOnSta(() =>
            {
                string markdown = "#### Option A: Via the Cortex XDR / XSIAM Web Console\n##### Deep Option";
                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(markdown);

                Assert.Equal(2, doc.Blocks.Count);
                var p0 = (Paragraph)doc.Blocks.ElementAt(0);
                var p1 = (Paragraph)doc.Blocks.ElementAt(1);

                Assert.Equal(NotionMarkdownHelper.TagH3, p0.Tag);
                Assert.Equal(NotionMarkdownHelper.TagH3, p1.Tag);

                string serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);
                Assert.Contains("### Option A: Via the Cortex XDR / XSIAM Web Console", serialized);
                Assert.Contains("### Deep Option", serialized);
            });
        }

        [Fact]
        public void AIChatPaste_MultiBlock_ParsesAllBlocksAndInlinesImmediately()
        {
            RunOnSta(() =>
            {
                string aiChatText = "Now the debug output confirms the exact problem.\n\n" +
                                   "Your **network and SSH handshake are completely successful**. The failure occurs only when Windows OpenSSH tries to read the private key.\n\n" +
                                   "The key evidence is:\n\n" +
                                   "```text\n" +
                                   "debug1: Trying private key: .\\broker_ssh\\private_ssh.ppk\n" +
                                   "Load key \".\\broker_ssh\\private_ssh.ppk\": invalid format\n" +
                                   "```\n\n" +
                                   "### What's happening\n\n" +
                                   "Your connection sequence is:\n" +
                                   "1. Go to **Settings** → **Broker VMs**.\n" +
                                   "2. Select your Broker VM (`192.168.51.120`).";

                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(aiChatText);

                // Check that code block is parsed
                var codeBlock = doc.Blocks.FirstOrDefault(b => b.Tag?.ToString()?.StartsWith("CodeBlock") == true);
                Assert.NotNull(codeBlock);

                // Check that H3 is parsed
                var h3 = doc.Blocks.OfType<Paragraph>().FirstOrDefault(p => p.Tag as string == NotionMarkdownHelper.TagH3);
                Assert.NotNull(h3);

                // Check that bold is parsed in body
                var bodyWithBold = doc.Blocks.OfType<Paragraph>().FirstOrDefault(p => p.Inlines.OfType<Bold>().Any());
                Assert.NotNull(bodyWithBold);

                // Check that ordered list with bold is parsed
                var listWithBold = doc.Blocks.OfType<Paragraph>().FirstOrDefault(p => (p.Tag as string == NotionMarkdownHelper.TagOrderedList) && p.Inlines.OfType<Bold>().Any());
                Assert.NotNull(listWithBold);

                // Check round-trip
                string serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);
                Assert.Contains("**network and SSH handshake are completely successful**", serialized);
                Assert.Contains("`192.168.51.120`", serialized);
                Assert.Contains("### What's happening", serialized);
                Assert.Contains("```text", serialized);
            });
        }

        [Fact]
        public void TryTransformBlock_PreservesHeading_WhenTextContainsNumberedPrefix()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagH2, "1. Set System Timezone");
                bool transformed = NotionMarkdownHelper.TryTransformBlock(p);

                Assert.False(transformed);
                Assert.Equal(NotionMarkdownHelper.TagH2, p.Tag);
                Assert.Equal("1. Set System Timezone", NotionMarkdownHelper.InlinesToMarkdown(p.Inlines, NotionMarkdownHelper.TagH2));
            });
        }

        [Fact]
        public void TryTransformBlock_TransformsBodyToOrderedList_WhenTextStartsWithNumber()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, "1. First step");
                bool transformed = NotionMarkdownHelper.TryTransformBlock(p);

                Assert.True(transformed);
                Assert.Equal(NotionMarkdownHelper.TagOrderedList, p.Tag);
            });
        }

        [Fact]
        public void TryTransformBlock_TransformsListItemToHeading_WhenHeadingMarkerFollowsNumber()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagOrderedList, "1. # hello");
                bool transformed = NotionMarkdownHelper.TryTransformBlock(p);

                Assert.True(transformed);
                Assert.Equal(NotionMarkdownHelper.TagH1, p.Tag);
                Assert.Equal("hello", NotionMarkdownHelper.InlinesToMarkdown(p.Inlines, NotionMarkdownHelper.TagH1));
            });
        }

        [Fact]
        public void TryTransformBlock_TransformsListItemToH2_WhenH2MarkerFollowsNumber()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagOrderedList, "1. ## Set System Timezone");
                bool transformed = NotionMarkdownHelper.TryTransformBlock(p);

                Assert.True(transformed);
                Assert.Equal(NotionMarkdownHelper.TagH2, p.Tag);
                Assert.Equal("Set System Timezone", NotionMarkdownHelper.InlinesToMarkdown(p.Inlines, NotionMarkdownHelper.TagH2));
            });
        }

        [Fact]
        public void TryTransformBlock_TransformsBodyToH2_WhenH2MarkerPrecedesNumber()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, "## 1. Set System Timezone");
                bool transformed = NotionMarkdownHelper.TryTransformBlock(p);

                Assert.True(transformed);
                Assert.Equal(NotionMarkdownHelper.TagH2, p.Tag);
                Assert.Equal("1. Set System Timezone", NotionMarkdownHelper.InlinesToMarkdown(p.Inlines, NotionMarkdownHelper.TagH2));
            });
        }

        [Fact]
        public void TryTransformBlock_ReturnsFalse_WhenEditingExistingOrderedList()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateOrderedListParagraph(1, "Set System Timezone", 12);
                bool transformed = NotionMarkdownHelper.TryTransformBlock(p);

                Assert.False(transformed);
                Assert.Equal(NotionMarkdownHelper.TagOrderedList, p.Tag);
            });
        }

        [Fact]
        public void MarkdownToFlowDocument_ParsesListWithHeadingMarker_AsHeading()
        {
            RunOnSta(() =>
            {
                string markdown = "1. # helllo";
                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(markdown);

                Assert.Single(doc.Blocks);
                var p = doc.Blocks.First() as Paragraph;
                Assert.NotNull(p);
                Assert.Equal(NotionMarkdownHelper.TagH1, p.Tag);
                Assert.Equal("helllo", NotionMarkdownHelper.InlinesToMarkdown(p.Inlines, NotionMarkdownHelper.TagH1));
            });
        }

        [Fact]
        public void BlockToMarkdown_ConvertsHeadingBlocksAccurately()
        {
            RunOnSta(() =>
            {
                var h1 = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagH1, "Windows");
                var h2 = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagH2, "OpenSSL Certificate");
                var h3 = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagH3, "Sub Section");

                Assert.Equal("# Windows", NotionMarkdownHelper.BlockToMarkdown(h1));
                Assert.Equal("## OpenSSL Certificate", NotionMarkdownHelper.BlockToMarkdown(h2));
                Assert.Equal("### Sub Section", NotionMarkdownHelper.BlockToMarkdown(h3));
            });
        }

        [Fact]
        public void BlockToMarkdown_ConvertsQuoteBlockAccurately()
        {
            RunOnSta(() =>
            {
                var quote = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagQuote, "WSL Linux CMD");
                Assert.Equal("> WSL Linux CMD", NotionMarkdownHelper.BlockToMarkdown(quote));
            });
        }

        [Fact]
        public void BlockToMarkdown_ConvertsCodeBlockAccurately()
        {
            RunOnSta(() =>
            {
                string code = "openssl req -x509 -nodes -newkey rsa:2048";
                var codeBlock = NotionMarkdownHelper.CreateCodeBlock(code, "bash");
                string md = NotionMarkdownHelper.BlockToMarkdown(codeBlock);

                Assert.Contains("```bash", md);
                Assert.Contains("openssl req -x509 -nodes -newkey rsa:2048", md);
                Assert.EndsWith("```", md.TrimEnd());
            });
        }

        [Fact]
        public void SetCodeBlockLanguage_UpdatesTagAndSerialization()
        {
            RunOnSta(() =>
            {
                string code = "print('hello world')";
                var codeBlock = NotionMarkdownHelper.CreateCodeBlock(code, "bash");
                Assert.Equal("CodeBlock:bash", codeBlock.Tag);

                NotionMarkdownHelper.SetCodeBlockLanguage(codeBlock, "python");
                Assert.Equal("CodeBlock:python", codeBlock.Tag);

                string md = NotionMarkdownHelper.BlockToMarkdown(codeBlock);
                Assert.StartsWith("```python", md);
                Assert.Contains("print('hello world')", md);
            });
        }

        [Theory]
        [InlineData("c++", "cpp")]
        [InlineData("docker", "dockerfile")]
        [InlineData("plaintext", "text")]
        [InlineData("cjs", "javascript")]
        public void SyntaxHighlighter_NormalizeLanguage_SupportsExtendedAliases(string alias, string expected)
        {
            var result = SyntaxHighlighter.NormalizeLanguage(alias);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ObsidianCodeBlock_NormalAndActiveModes_RenderAndSerializeCorrectly()
        {
            RunOnSta(() =>
            {
                string code = "Write-Host 'Antigravity'";
                var block = NotionMarkdownHelper.CreateCodeBlock(code, "powershell");

                Assert.IsType<Paragraph>(block);
                var p = (Paragraph)block;

                // In Normal mode: rendered without ``` fences in raw text, has code
                string normalText = new TextRange(p.ContentStart, p.ContentEnd).Text.Replace("\r\n", "\n");
                Assert.DoesNotContain("```powershell", normalText);
                Assert.Contains("Write-Host 'Antigravity'", normalText);

                // Serializes to valid markdown with fences
                string mdNormal = NotionMarkdownHelper.BlockToMarkdown(p);
                Assert.StartsWith("```powershell", mdNormal);
                Assert.Contains("Write-Host 'Antigravity'", mdNormal);
                Assert.EndsWith("```", mdNormal.TrimEnd());

                // In Active mode: expanded with ``` fences
                NotionMarkdownHelper.PopulateActiveCodeBlockParagraph(p, code, "powershell");
                string activeText = new TextRange(p.ContentStart, p.ContentEnd).Text.Replace("\r\n", "\n");
                Assert.StartsWith("```powershell", activeText);
                Assert.Contains("Write-Host 'Antigravity'", activeText);
                Assert.EndsWith("```", activeText.TrimEnd());

                // Serializes to valid markdown with fences
                string mdActive = NotionMarkdownHelper.BlockToMarkdown(p);
                Assert.StartsWith("```powershell", mdActive);
                Assert.Contains("Write-Host 'Antigravity'", mdActive);
                Assert.EndsWith("```", mdActive.TrimEnd());

                // Extract code
                string extracted = NotionMarkdownHelper.ExtractCodeFromParagraph(p);
                Assert.Equal("Write-Host 'Antigravity'", extracted);
            });
        }

        [Fact]
        public void ObsidianCodeBlock_CanBeDeletedAndRecreatedNatively()
        {
            RunOnSta(() =>
            {
                var doc = new FlowDocument();
                var codeBlock = NotionMarkdownHelper.CreateCodeBlock("Get-ChildItem", "powershell");
                doc.Blocks.Add(codeBlock);

                Assert.Single(doc.Blocks);

                // Native deletion without any crash
                doc.Blocks.Remove(codeBlock);
                Assert.Empty(doc.Blocks);

                // Add regular paragraph
                var p = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, "Hello");
                doc.Blocks.Add(p);
                Assert.Single(doc.Blocks);
            });
        }

        [Fact]
        public void MarkdownToFlowDocument_ConsecutiveBlocks_DoesNotCreateExtraEmptyParagraphs()
        {
            RunOnSta(() =>
            {
                // The exact scenario reported by user: H2, Quote, CodeBlock, H1, Body separated by \n\n
                string markdown = "## OpenSSL Certificate\n\n" +
                                  "> WSL Linux CMD\n\n" +
                                  "```bash\n" +
                                  "openssl req -x509 -nodes\n" +
                                  "```\n\n" +
                                  "# Windows\n\n" +
                                  "Folder copy";

                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(markdown);

                // Must have exactly 5 blocks with ZERO extra empty paragraphs in between
                Assert.Equal(5, doc.Blocks.Count);

                var blocks = doc.Blocks.ToList();
                Assert.Equal(NotionMarkdownHelper.TagH2, (blocks[0] as Paragraph)?.Tag);
                Assert.Equal(NotionMarkdownHelper.TagQuote, (blocks[1] as Paragraph)?.Tag);
                Assert.StartsWith("CodeBlock:bash", (blocks[2] as Paragraph)?.Tag as string);
                Assert.Equal(NotionMarkdownHelper.TagH1, (blocks[3] as Paragraph)?.Tag);
                Assert.Equal(NotionMarkdownHelper.TagBody, (blocks[4] as Paragraph)?.Tag);

                // Round-trip must also maintain 5 blocks
                string serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);
                var doc2 = NotionMarkdownHelper.MarkdownToFlowDocument(serialized);
                Assert.Equal(5, doc2.Blocks.Count);
            });
        }

        [Fact]
        public void MarkdownToFlowDocument_IntentionalEmptyLines_PreservedAccurately()
        {
            RunOnSta(() =>
            {
                // 2 blank lines between Heading and Body means 1 intentional empty line
                string markdown = "# Windows\n\n\nFolder copy";

                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(markdown);

                Assert.Equal(3, doc.Blocks.Count);
                var blocks = doc.Blocks.ToList();
                Assert.Equal(NotionMarkdownHelper.TagH1, (blocks[0] as Paragraph)?.Tag);
                Assert.Equal(NotionMarkdownHelper.TagBody, (blocks[1] as Paragraph)?.Tag);
                Assert.Equal(string.Empty, new TextRange((blocks[1] as Paragraph)!.ContentStart, (blocks[1] as Paragraph)!.ContentEnd).Text.Trim());
                Assert.Equal(NotionMarkdownHelper.TagBody, (blocks[2] as Paragraph)?.Tag);
                Assert.Equal("Folder copy", new TextRange((blocks[2] as Paragraph)!.ContentStart, (blocks[2] as Paragraph)!.ContentEnd).Text.Trim());

                // Round-trip preserves the intentional empty line without accumulating more
                string serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);
                var doc2 = NotionMarkdownHelper.MarkdownToFlowDocument(serialized);
                Assert.Equal(3, doc2.Blocks.Count);
            });
        }

        [Fact]
        public void ObsidianCodeBlock_WhenEmptied_CanRevertToBodyParagraphWithoutZombieBox()
        {
            RunOnSta(() =>
            {
                var block = NotionMarkdownHelper.CreateCodeBlock("Get-Process", "powershell");
                var p = (Paragraph)block;

                Assert.StartsWith("CodeBlock:", p.Tag as string);
                Assert.Equal(new Thickness(1), p.BorderThickness);

                // When user backspaces or deletes all content in code block, reverting to TagBody
                p.Inlines.Clear();
                p.DataContext = null;
                p.Resources.Remove("IsActiveCodeBlock");
                NotionMarkdownHelper.ApplyStyle(p, NotionMarkdownHelper.TagBody);

                // Tag is now TagBody, border is 0, background is transparent, padding is 0
                Assert.Equal(NotionMarkdownHelper.TagBody, p.Tag as string);
                Assert.Equal(new Thickness(0), p.BorderThickness);
                Assert.Equal(new Thickness(0), p.Padding);
                Assert.True(p.Background == null || p.Background == Brushes.Transparent || (p.Background is SolidColorBrush scb && scb.Color.A == 0));
            });
        }

        [Fact]
        public void ObsidianCodeBlock_HasConsistentCardStyling_InNormalAndActiveModes()
        {
            RunOnSta(() =>
            {
                var block = NotionMarkdownHelper.CreateCodeBlock("Get-ChildItem", "powershell");
                var p = (Paragraph)block;

                // Verify styling in Normal mode
                Assert.Equal(new Thickness(1), p.BorderThickness);
                Assert.Equal(new Thickness(14, 8, 14, 10), p.Padding);
                Assert.NotNull(p.Background);
                Assert.NotNull(p.BorderBrush);
                Assert.Contains("Consolas", p.FontFamily.Source);

                // Switch to Active mode
                NotionMarkdownHelper.PopulateActiveCodeBlockParagraph(p, "Get-ChildItem", "powershell");
                Assert.Equal(new Thickness(1), p.BorderThickness);
                Assert.Equal(new Thickness(14, 8, 14, 10), p.Padding);
                Assert.NotNull(p.Background);
                Assert.NotNull(p.BorderBrush);

                // Switch back to Normal mode
                NotionMarkdownHelper.PopulateNormalCodeBlockParagraph(p, "Get-ChildItem", "powershell");
                Assert.Equal(new Thickness(1), p.BorderThickness);
                Assert.Equal(new Thickness(14, 8, 14, 10), p.Padding);
                Assert.NotNull(p.Background);
            });
        }

        [Fact]
        public void MarkdownToFlowDocument_CodeBlocksRoundTrip_PreservesAllCodeBlocksAndStyles()
        {
            RunOnSta(() =>
            {
                // The exact scenario from the user's screenshot
                string markdown = "Folder copy\n\n" +
                                  "```powershell\n" +
                                  "Get-ChildItem -Path \"D:\\BIM_Learning_Course\" -Directory | Where-Object { $_.Name -like \"* *\" } | Copy-Item -Destination \"C:\\Users\\PC\\Downloads\" -Recurse\n" +
                                  "```\n\n" +
                                  "Powershell bypass\n\n" +
                                  "```powershell\n" +
                                  "Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force\n" +
                                  "```\n\n" +
                                  "SCP\n\n" +
                                  "```scp\n" +
                                  "scp -r \"C:\\Users\\BIM-ATS-0556\\Desktop\\web_server\" rootuser@10.1.40.62:/home/rootuser/CPM/\n" +
                                  "```\n\n" +
                                  "Check Network Connectivity\n\n" +
                                  "```powershell\n" +
                                  "Resolve-DnsName time.google.com\n" +
                                  "w32tm /stripchart /computer:time.google.com /dataonly /samples:5\n" +
                                  "w32tm /stripchart /computer:pool.ntp.org /dataonly /samples:5\n" +
                                  "```";

                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(markdown);

                // Check blocks
                var blocks = doc.Blocks.ToList();
                Assert.Equal(8, blocks.Count);

                Assert.Equal(NotionMarkdownHelper.TagBody, (blocks[0] as Paragraph)?.Tag);
                Assert.Equal("CodeBlock:powershell", (blocks[1] as Paragraph)?.Tag);
                Assert.Equal(NotionMarkdownHelper.TagBody, (blocks[2] as Paragraph)?.Tag);
                Assert.Equal("CodeBlock:powershell", (blocks[3] as Paragraph)?.Tag);
                Assert.Equal(NotionMarkdownHelper.TagBody, (blocks[4] as Paragraph)?.Tag);
                Assert.Equal("CodeBlock:scp", (blocks[5] as Paragraph)?.Tag);
                Assert.Equal(NotionMarkdownHelper.TagBody, (blocks[6] as Paragraph)?.Tag);
                Assert.Equal("CodeBlock:powershell", (blocks[7] as Paragraph)?.Tag);

                // Round-trip must preserve all 4 code blocks and language tags
                string serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);
                Assert.Contains("```powershell", serialized);
                Assert.Contains("```scp", serialized);
                Assert.Contains("Get-ChildItem", serialized);
                Assert.Contains("Set-ExecutionPolicy", serialized);
                Assert.Contains("scp -r", serialized);
                Assert.Contains("Resolve-DnsName", serialized);

                var doc2 = NotionMarkdownHelper.MarkdownToFlowDocument(serialized);
                Assert.Equal(8, doc2.Blocks.Count);
                for (int i = 0; i < 8; i++)
                {
                    Assert.Equal((blocks[i] as Paragraph)?.Tag, (doc2.Blocks.ToList()[i] as Paragraph)?.Tag);
                }
            });
        }

        [Fact]
        public void GetContainingParagraph_TraversesInlineAndParentHierarchies()
        {
            RunOnSta(() =>
            {
                var doc = new FlowDocument();
                var p = NotionMarkdownHelper.CreateCodeBlock("Get-Process # active process\nStop-Process -Id 123", "powershell");
                doc.Blocks.Add(p);

                // Pointer from ContentStart
                var pFound1 = NotionMarkdownHelper.GetContainingParagraph(p.ContentStart);
                Assert.Same(p, pFound1);

                // Pointer from inner Run
                var run = p.Inlines.OfType<Run>().FirstOrDefault();
                Assert.NotNull(run);
                var pFound2 = NotionMarkdownHelper.GetContainingParagraph(run.ContentStart);
                Assert.Same(p, pFound2);
            });
        }

        [Fact]
        public void ObsidianCodeBlock_ActiveMode_AllowsNaturalEditingAndPreservesContent()
        {
            RunOnSta(() =>
            {
                var block = NotionMarkdownHelper.CreateCodeBlock("# Initial comment\nGet-Service", "powershell");
                var p = (Paragraph)block;

                // Expand to Active Mode
                NotionMarkdownHelper.PopulateActiveCodeBlockParagraph(p, "# Initial comment\nGet-Service", "powershell");
                Assert.True(p.Resources["IsActiveCodeBlock"] as bool? ?? false);

                // Extract code from active block
                var extracted = NotionMarkdownHelper.ExtractCodeFromParagraph(p);
                Assert.Equal("# Initial comment\nGet-Service", extracted);

                // Markdown conversion
                var md = NotionMarkdownHelper.BlockToMarkdown(p);
                Assert.StartsWith("```powershell", md);
                Assert.Contains("# Initial comment", md);
                Assert.Contains("Get-Service", md);
                Assert.EndsWith("```", md.TrimEnd());
            });
        }

        [Fact]
        public void CodeBlock_ActiveMode_TypingSpacesAndNewlines_PreservesCode()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateCodeBlock(string.Empty, "powershell");
                Assert.True(NotionMarkdownHelper.IsCodeBlock(p));

                // Populate active mode
                NotionMarkdownHelper.PopulateActiveCodeBlockParagraph(p, string.Empty, "powershell");
                Assert.True(NotionMarkdownHelper.IsCodeBlock(p));

                // Simulate typing "hello world" on line 2
                NotionMarkdownHelper.PopulateActiveCodeBlockParagraph(p, "hello world", "powershell");
                Assert.True(NotionMarkdownHelper.IsCodeBlock(p));

                var extracted = NotionMarkdownHelper.ExtractCodeFromParagraph(p);
                Assert.Equal("hello world", extracted);

                var md = NotionMarkdownHelper.BlockToMarkdown(p);
                Assert.Equal("```powershell\nhello world\n```", md.TrimEnd());
            });
        }

        [Fact]
        public void CodeBlock_DocumentWithCodeBlock_FullSelection_SerializesProperly()
        {
            RunOnSta(() =>
            {
                var markdown = "# Title Note\n\nSome introductory text\n\n```powershell\nGet-Process | Where-Object { $_.CPU -gt 10 }\n```\n\nConcluding text";
                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(markdown);

                Assert.Equal(4, doc.Blocks.Count);
                Assert.True(NotionMarkdownHelper.IsCodeBlock(doc.Blocks.ElementAt(2)));

                var serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);
                Assert.Contains("# Title Note", serialized);
                Assert.Contains("Some introductory text", serialized);
                Assert.Contains("```powershell", serialized);
                Assert.Contains("Get-Process", serialized);
                Assert.Contains("Concluding text", serialized);
            });
        }

        [Fact]
        public void ConsecutiveBodyParagraphs_SerializeWithSingleNewline_NoExtraBlankLines()
        {
            RunOnSta(() =>
            {
                var doc = new System.Windows.Documents.FlowDocument();
                doc.Blocks.Add(NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, "hello world"));
                doc.Blocks.Add(NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, "hello world"));
                doc.Blocks.Add(NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, "hello world"));

                var serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);
                Assert.Equal("hello world\nhello world\nhello world", serialized);
            });
        }

        [Fact]
        public void LargePastedText_DoesNotCrash()
        {
            RunOnSta(() =>
            {
                string text = @"**TL;DR:** Below is the complete **Full Architectural Design** followed by the self-contained **PowerShell Deployment Script** (`Setup-GlobalSDLC-Complete.ps1`). It deploys the updated `@sdlc` skill (`$env:USERPROFILE\.antigravity\skills\sdlc.md`) and the verified AST/brace-counting indexer (`$env:USERPROFILE\.antigravity\tools\universal_index.py`), fully configured for zero-permission execution, token-efficient surgical patching, NASA JPL 12-rule quality, and automated architectural escalation.

---

## 1. Full Architectural Design

The `@sdlc` Autonomous Software Factory operates as a closed-loop, deterministic execution engine inside the Antigravity workspace.

```text
                               +------------------------------------------------+
                               |              USER SDLC PROMPT                  |
                               +-----------------------+------------------------+
                                                       |
                                                       v
+---------------------------------------------------------------------------------------------------------------+
| LAYER 1: PRELUDE (Inception & Context Anchoring)                                                              |
| - Read PROJECT_SPEC.md & .factory/issues.json (Purpose Anchor to avoid goal decay)                            |
| - Query .factory/code_index.json (Locate target symbol & exact start-end line offsets)                        |
| - Zero LLM context dump: Inspect ONLY the target line range (e.g. sed -n '45,78p' or Get-Content slice)       |
+------------------------------------------------------+--------------------------------------------------------+
                                                       |
                                                       v
+---------------------------------------------------------------------------------------------------------------+
| LAYER 2: PRIMA (NASA JPL 12-Rule Code Quality Engine)                                                         |
| - Rule 1: Simple Flow (Nesting <= 2, No recursion)   | Rule 7: Tightly Scoped State (Minimal globals)         |
| - Rule 2: Bounded Loops (Hard timeout / Max counter) | Rule 8: Explicit Side Effects (Decouple I/O from logic)|
| - Rule 3: Deterministic Cleanup (finally / using)    | Rule 9: Minimal Abstraction (Indirection <= 1 layer)   |
| - Rule 4: Single Responsibility (40-60 lines / unit) | Rule 10: Zero Warnings & Strict Typing                 |
| - Rule 5: Explicit Assertions & Defensive Checks     | Rule 11: Line-by-Line QA Review                        |
| - Rule 6: Zero Swallowed Errors (No except: pass)    | Rule 12: Test-Driven Specs (TDD validations)           |
+------------------------------------------------------+--------------------------------------------------------+
                                                       |
                                                       v
+---------------------------------------------------------------------------------------------------------------+
| LAYER 3: HELIX (Multi-Agent State Machine & Escalation Engine)                                                |
| - Architect: Interface contract verification. Halts blind patching on deadlocks; creates Architectural RFC.  |
| - Coder: Emits unified search/replace diff blocks under NASA JPL standards.                                    |
| - QA: Edge-case validation, boundary review, failure-mode regression testing.                                 |
+------------------------------------------------------+--------------------------------------------------------+
                                                       |
                                                       v
+---------------------------------------------------------------------------------------------------------------+
| LAYER 4: ACME PROJECTS (Task Lifecycle Tracking)                                                              |
| - Synchronize task boards in .factory/project_board.json                                                      |
| - Autonomous status transitions: [Todo] -> [In-Progress] -> [Done]                                            |
+------------------------------------------------------+--------------------------------------------------------+
                                                       |
                                                       v
+---------------------------------------------------------------------------------------------------------------+
| LAYER 5: ACME ISSUES (Surgical Patching, Testing & Reindexing)                                                |
| - Emit Search/Replace block (no full-file rewrites):                                                          |
|       <<<<<<< SEARCH                                                                                          |
|       [exact original lines]                                                                                  |
|       =======                                                                                                 |
|       [updated code]                                                                                          |
|       >>>>>>> REPLACE                                                                                         |
| - Execute tests with silent truncation: pytest -q --tb=short                                                  |
| - Post-Write Reindexing: Run tools/universal_index.py to sync new line boundaries for future prompts          |
| - Close task in .factory/issues.json and render output summary table                                          |
+---------------------------------------------------------------------------------------------------------------+

```

---

## 2. Complete PowerShell Setup Script (`Setup-GlobalSDLC-Complete.ps1`)

Run this script in Windows PowerShell to deploy the global skill definition and the indexer script.

```powershell
<#
.SYNOPSIS
    Self-Contained Global Software Factory Skill Setup for Antigravity (@sdlc)
    Deploys:
    - Global Markdown Directives (NASA JPL, Unified Patching, Purpose Anchoring, ADR Escalation)
    - Robust AST & Brace-Counting Universal Indexer (tools/universal_index.py)
#>

$ErrorActionPreference = ""Stop""

Write-Host ""=================================================================="" -ForegroundColor Cyan
Write-Host ""  Deploying Complete @sdlc Global Engine & Indexer Tools         "" -ForegroundColor Cyan
Write-Host ""=================================================================="" -ForegroundColor Cyan

$GlobalSkillsFolder = ""$env:USERPROFILE\.antigravity\skills""
$GlobalToolsFolder  = ""$env:USERPROFILE\.antigravity\tools""

foreach ($dir in @($GlobalSkillsFolder,$GlobalToolsFolder)) {
    if (-not (Test-Path -Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host ""[+] Created Directory: $dir"" -ForegroundColor Green
    }
}

# -----------------------------------------------------------------------------
# 1. DEPLOY ROBUST UNIVERSAL INDEXER (tools/universal_index.py)
# -----------------------------------------------------------------------------
Write-Host ""[*] Deploying Universal AST & Brace-Counting Indexer..."" -ForegroundColor Yellow

$UniversalIndexPythonScript = @'
#!/usr/bin/env python3
""""""
Robust Multi-Language Symbol Indexer
Zero third-party dependencies.
Calculates exact start and end line ranges for targeted surgical reads.
""""""
import os
import ast
import re
import json
from pathlib import Path

WORKSPACE_ROOT = Path(""."")
OUTPUT_INDEX = WORKSPACE_ROOT / "".factory"" / ""code_index.json""
EXCLUDE_DIRS = {
    "".git"", "".factory"", ""__pycache__"", ""node_modules"",
    ""venv"", "".venv"", ""target"", ""dist"", ""bin"", ""obj"", "".idea"", "".vscode""
}

def parse_python(filepath):
    symbols = []
    try:
        with open(filepath, ""r"", encoding=""utf-8"", errors=""ignore"") as f:
            tree = ast.parse(f.read(), filename=str(filepath))
        
        for node in tree.body:
            if isinstance(node, ast.ClassDef):
                symbols.append({
                    ""name"": node.name,
                    ""type"": ""class"",
                    ""lines"": f""{node.lineno}-{node.end_lineno}""
                })
                for item in node.body:
                    if isinstance(item, (ast.FunctionDef, ast.AsyncFunctionDef)):
                        symbols.append({
                            ""name"": f""{node.name}.{item.name}"",
                            ""type"": ""method"",
                            ""lines"": f""{item.lineno}-{item.end_lineno}""
                        })
            elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                symbols.append({
                    ""name"": node.name,
                    ""type"": ""function"",
                    ""lines"": f""{node.lineno}-{node.end_lineno}""
                })
    except Exception:
        pass
    return symbols

def parse_brace_language(filepath, pattern):
    symbols = []
    try:
        with open(filepath, ""r"", encoding=""utf-8"", errors=""ignore"") as f:
            lines = f.readlines()

        total = len(lines)
        i = 0
        while i < total:
            line = lines[i]
            clean_line = re.sub(r""//.*$"", """", line).strip()
            match = pattern.search(clean_line)
            if match:
                name = next(filter(None, match.groups()), None)
                start_line = i + 1
                
                brace_count = 0
                found_open = False
                end_line = start_line
                
                for j in range(i, min(i + 300, total)):
                    cur_line = re.sub(r""//.*$"", """", lines[j])
                    open_b = cur_line.count(""{"")
                    close_b = cur_line.count(""}"")
                    
                    if open_b > 0:
                        found_open = True
                    
                    brace_count += (open_b - close_b)
                    
                    if found_open and brace_count <= 0:
                        end_line = j + 1
                        i = j
                        break
                        
                if name:
                    symbols.append({
                        ""name"": name,
                        ""type"": ""symbol"",
                        ""lines"": f""{start_line}-{end_line}""
                    })
            i += 1
    except Exception:
        pass
    return symbols

BRACE_PATTERN = re.compile(
    r""(?:func|fn|function|class|struct|interface)\s+([A-Za-z0-9_]+)|(?:const|let)\s+([A-Za-z0-9_]+)\s*=\s*(?:async\s*)?\(""
)

def run_indexer():
    OUTPUT_INDEX.parent.mkdir(parents=True, exist_ok=True)
    index_data = {""version"": ""2.1"", ""modules"": []}

    for root, dirs, files in os.walk(WORKSPACE_ROOT):
        dirs[:] = [d for d in dirs if d not in EXCLUDE_DIRS]
        for file in files:
            path = Path(root) / file
            rel_path = str(path.relative_to(WORKSPACE_ROOT))
            
            if file.endswith("".py""):
                symbols = parse_python(path)
            elif any(file.endswith(ext) for ext in ("".go"", "".rs"", "".ts"", "".js"", "".cs"", "".cpp"", "".c"", "".java"")):
                symbols = parse_brace_language(path, BRACE_PATTERN)
            else:
                continue

            if symbols:
                index_data[""modules""].append({
                    ""file"": rel_path,
                    ""symbols"": symbols
                })

    with open(OUTPUT_INDEX, ""w"", encoding=""utf-8"") as f:
        json.dump(index_data, f, indent=2)

if __name__ == ""__main__"":
    run_indexer()
'@

$GlobalToolPath = Join-Path -Path$GlobalToolsFolder -ChildPath ""universal_index.py""
[System.IO.File]::WriteAllText($GlobalToolPath,$UniversalIndexPythonScript, [System.Text.Encoding]::UTF8)
Write-Host ""[+] Indexer Deployed: $GlobalToolPath"" -ForegroundColor Green

# -----------------------------------------------------------------------------
# 2. DEPLOY FULL GLOBAL SKILL DIRECTIVES (sdlc.md)
# -----------------------------------------------------------------------------
Write-Host ""[*] Deploying Global @sdlc Skill Directives..."" -ForegroundColor Yellow

$GlobalSkillMarkdown = @'
---
name: sdlc
trigger: ""@sdlc""
description: ""Autonomous Global Software Factory Engine powered by NASA JPL 12-Rule Guardrails, Symbol Indexing, Purpose Anchoring, and Unified Patching.""
---

# SYSTEM ROLE: AUTONOMOUS SOFTWARE FACTORY ORCHESTRATOR

When triggered with `@sdlc [Requirement/Task/Fix]`, you operate as an end-to-end **Autonomous Software Factory Engine**. 
You must simulate and execute the 5-layer architecture designed by Ei Maung (`eimg`) combined with NASA JPL safety-critical code rules **entirely within your internal reasoning loop**.

---

## 1. NON-INTERACTIVE & ZERO-PERMISSION DIRECTIVES

* **FULL EXECUTION AUTHORIZATION:** You are granted full authorization to create, edit, patch, and write files/directories in this workspace.
* **ZERO USER INTERRUPTIONS:** Do NOT pause to ask ""Can I create this file?"" or ""Do you want to proceed?"". Run continuously from inception to validation.
* **TERMINAL READ AUTHORIZATION:** Auto-approve file saves, directory creation, and non-destructive terminal reads (`cat`, `ls`, `grep`, `sed`, `head`, `tail`).
* **SILENT COMMAND EXECUTION:** Pipe test and build commands to output only failures or short summaries (e.g., `pytest -q --tb=short`). Never dump complete passing logs into context.

---

## 2. SURGICAL CODE MODIFICATION DIRECTIVE (DIFF & UNIFIED PATCHING)

When modifying or refactoring existing code:
* **NEVER Rewrite Unchanged Files:** Do NOT output the entire file from line 1 to end if only an isolated function or block needs changing.
* **Unified Search/Replace Format:** Emit modifications strictly using precise search-and-replace blocks:
  ```text
  <<<<<<< SEARCH
  [exact lines from original file to replace]
  =======
  [new updated lines adhering to NASA JPL rules]
  >>>>>>> REPLACE

```

* **Preserve File Boundaries:** Modify only the target lines identified via `.factory/code_index.json`. Surrounding imports, helpers, and definitions must remain untouched.
* **Full-File Exemption:** Complete file writes are permitted ONLY when initializing a brand-new file or executing an approved full architectural migration.

---

## 3. UNIVERSAL TOKEN CONSERVATION & SYMBOL ENGINE

1. **Mandatory Index-First Query:**
* Before reading or fixing existing files, ALWAYS check `.factory/code_index.json`.
* If `.factory/code_index.json` does not exist in the workspace, copy `tools/universal_index.py` from the global template (`%USERPROFILE%/.antigravity/tools/universal_index.py`) into the workspace `tools/` directory and execute it.


2. **Surgical Line-Range Inspection:**
* Never load whole files into context. Locate the target symbol's exact line boundary (e.g., lines `45-78`) and inspect only those lines (e.g., via `sed -n '45,78p' <file>`).


3. **Post-Write Reindexing:**
* Always refresh `.factory/code_index.json` after completing file edits by executing `python3 tools/universal_index.py` non-interactively.



---

## 4. PRE-EXECUTION PURPOSE ANCHORING & ARCHITECTURAL ESCALATION

Before writing code for any modification, bug fix, or feature addition:

1. **Purpose Anchoring & Intent Verification:**
* Always review `PROJECT_SPEC.md` and `.factory/issues.json` before touching code to eliminate context drift and goal decay over long sessions.
* Verify that the requested modification aligns with core system invariants established in previous turns.


2. **Consolidation Analysis:**
* Never stack temporary band-aids on top of bugs. Consolidate bug fixes and incoming feature requests into a single clean refactor.
* Address root causes directly rather than suppressing exceptions (violating NASA Rule 6).


3. **Architectural Escalation Protocol (Halt on Deadlock):**
* If the current architecture, data schema, or technology cannot support requirements without violating NASA JPL rules (e.g., forcing unbounded loops, deep nesting, or memory leaks):
* **HALT blind patching immediately.**
* Present an **Architectural Decision Record (ADR / RFC)**: State the root cause of the current structural failure, propose a revised pattern/contract, and detail the migration impact.
* Automatically update `PROJECT_SPEC.md` with the new contracts and re-baseline `.factory/project_board.json`.
* Execute the migration cleanly in a single coordinated pass.





---

## 5. PRIMA PHASE: NASA JPL 12-RULE CODE QUALITY GUARDRAILS

All code written or modified in this workspace MUST strictly adhere to these 12 Safety-Critical Rules:

1. **Simple Control Flow:** Maximum 2 levels of nested logic. No deep conditionals. No recursion.
2. **Bounded Loops:** Every loop must have an explicit fixed upper-bound limit or hard timeout. No infinite polling.
3. **Resource Lifecycle:** All file handles, sockets, and memory handles must be released in `finally` or `using` blocks across ALL exit paths.
4. **Single Responsibility:** Functions must be under 40–60 lines and execute exactly ONE distinct responsibility.
5. **Explicit Assertions:** Include runtime checks for preconditions, parameter validations, and non-null states.
6. **Explicit Error Handling:** Zero empty `catch` blocks or swallowed exceptions (`except: pass`). Always log, propagate, or return explicit errors.
7. **Tightly Scoped State:** Keep variables strictly local to their block. Minimize global and class-level shared state.
8. **Explicit Side Effects:** Separate pure computational logic from I/O, database writes, and external network requests.
9. **Minimal Abstraction:** Limit indirection to 1 layer. Avoid unnecessary factory patterns, deep wrappers, or inheritance hierarchies.
10. **Zero Warnings & Strict Typing:** Code must pass static analysis and linting with zero warnings and complete type annotations.
11. **Line-by-Line Review:** Conduct an internal QA pass for boundary conditions, buffer overflows, and null checks before emitting code.
12. **Test-Driven Specs:** Auto-generate unit test specs or failure scenario validations alongside every implementation module.

---

## 6. INTERNAL 5-LAYER FACTORY LIFECYCLE

* **Layer 1 (Prelude):** Parse specs, build directory scaffolding, verify/generate `PROJECT_SPEC.md`, and run the universal indexer.
* **Layer 2 (Prima):** Apply NASA JPL 12-Rule constraints and strict language typings.
* **Layer 3 (Helix):** Coordinate simulated roles: **Architect** (schemas/contracts), **Coder** (diffs), and **QA** (edge cases/invariants).
* **Layer 4 (Acme Projects):** Track tasks in `.factory/project_board.json` (`Todo` -> `In-Progress` -> `Done`).
* **Layer 5 (Acme Issues):** Track issues in `.factory/issues.json`, execute search/replace patches, run tests, refresh index, and close tasks.

---

## 7. OUTPUT SUMMARY FORMATTING

Upon completing execution, emit a concise executive summary report:

### Architecture & Goal Anchor

* Status of `PROJECT_SPEC.md` alignment (or ADR summary if architecture was escalated).

### Acme Execution Tracking Summary

| Issue ID | Task Title | Target Path / Symbol | Acme Status | NASA QA Check |
| --- | --- | --- | --- | --- |
| '@ |  |  |  |  |

$GlobalSkillPath = Join-Path -Path$GlobalSkillsFolder -ChildPath ""sdlc.md""
[System.IO.File]::WriteAllText($GlobalSkillPath,$GlobalSkillMarkdown, [System.Text.Encoding]::UTF8)
Write-Host ""[+] Skill Deployed: $GlobalSkillPath"" -ForegroundColor Green

Write-Host ""=="" -ForegroundColor Cyan
Write-Host ""  GLOBAL SDLC SETUP COMPLETE!                                     "" -ForegroundColor Green
Write-Host ""  Skill File:   $GlobalSkillPath"" -ForegroundColor White
Write-Host ""  Indexer Tool: $GlobalToolPath"" -ForegroundColor White
Write-Host ""=="" -ForegroundColor Cyan

```

---

### Verification

After running the script, verify that both the skill and the indexer tool are in place:

```powershell
# 1. Verify Skill Directives
Get-Content ""$env:USERPROFILE\.antigravity\skills\sdlc.md"" | Select-String -Pattern ""SURGICAL CODE MODIFICATION|ARCHITECTURAL ESCALATION|UNIVERSAL TOKEN CONSERVATION""

# 2. Verify Indexer Script Syntax
python ""$env:USERPROFILE\.antigravity\tools\universal_index.py"" --help

```";

                var rtb = new RichTextBox();
                var editorDoc = new FlowDocument();
                var currentP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, "");
                editorDoc.Blocks.Add(currentP);
                rtb.Document = editorDoc;
                rtb.CaretPosition = currentP.ContentStart;

                var tempDoc = NotionMarkdownHelper.MarkdownToFlowDocument(text);
                var blocksToInsert = tempDoc.Blocks.ToList();
                tempDoc.Blocks.Clear();

                if (blocksToInsert.Count > 1 &&
                    blocksToInsert.Last() is Paragraph lastPara &&
                    (lastPara.Tag as string == NotionMarkdownHelper.TagBody) &&
                    string.IsNullOrWhiteSpace(new TextRange(lastPara.ContentStart, lastPara.ContentEnd).Text))
                {
                    blocksToInsert.RemoveAt(blocksToInsert.Count - 1);
                }

                Block lastBlock = currentP;
                foreach (var block in blocksToInsert)
                {
                    rtb.Document.Blocks.InsertAfter(lastBlock, block);
                    lastBlock = block;
                }
                rtb.Document.Blocks.Remove(currentP);

                if (lastBlock is Table t)
                {
                    var trailingP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, "");
                    rtb.Document.Blocks.InsertAfter(t, trailingP);
                    rtb.CaretPosition = trailingP.ContentStart;
                }
                else if (lastBlock is BlockUIContainer)
                {
                    var trailingP = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBody, "");
                    rtb.Document.Blocks.InsertAfter(lastBlock, trailingP);
                    rtb.CaretPosition = trailingP.ContentStart;
                }
                else
                {
                    rtb.CaretPosition = lastBlock.ContentEnd;
                }

                var serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(rtb.Document);
                Assert.NotNull(serialized);
            });
        }

        [Fact]
        public void ToggleHeadingFold_CollapsesAndExpands_PreservesHierarchy()
        {
            RunOnSta(() =>
            {
                NotionMarkdownHelper.FoldedHeadingSections.Clear();
                string md = "# Section 1\nLine under 1\n## Section 1.1\nLine under 1.1\n# Section 2\nLine under 2";
                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(md);
                var rtb = new RichTextBox { Document = doc };

                // Initially 6 blocks
                Assert.Equal(6, doc.Blocks.Count);
                var h1 = doc.Blocks.First() as Paragraph;
                Assert.NotNull(h1);
                Assert.Equal(NotionMarkdownHelper.TagH1, h1.Tag);

                // Collapse Section 1
                NotionMarkdownHelper.ToggleHeadingFold(h1, rtb);

                // Blocks under Section 1 (Line under 1, Section 1.1, Line under 1.1) should be hidden!
                // doc.Blocks should now only have Section 1, Section 2, and Line under 2 (3 blocks)
                Assert.Equal(3, doc.Blocks.Count);
                Assert.True(NotionMarkdownHelper.FoldedHeadingSections.ContainsKey(h1));
                Assert.Equal(3, NotionMarkdownHelper.FoldedHeadingSections[h1].Count);

                // Ellipsis should be present in folded heading
                Assert.Contains(h1.Inlines, inl => (string?)inl.Tag == NotionMarkdownHelper.TagFoldEllipsis);

                // Expand Section 1
                NotionMarkdownHelper.ToggleHeadingFold(h1, rtb);

                // All 6 blocks should be restored in order
                Assert.Equal(6, doc.Blocks.Count);
                Assert.False(NotionMarkdownHelper.FoldedHeadingSections.ContainsKey(h1));
                Assert.DoesNotContain(h1.Inlines, inl => (string?)inl.Tag == NotionMarkdownHelper.TagFoldEllipsis);

                NotionMarkdownHelper.FoldedHeadingSections.Clear();
            });
        }

        [Fact]
        public void FlowDocumentToMarkdown_PreservesFoldedContent_LossFree()
        {
            RunOnSta(() =>
            {
                NotionMarkdownHelper.FoldedHeadingSections.Clear();
                string originalMd = "# Section 1\nLine under 1\n## Section 1.1\nLine under 1.1\n# Section 2\nLine under 2";
                var doc = NotionMarkdownHelper.MarkdownToFlowDocument(originalMd);
                var rtb = new RichTextBox { Document = doc };

                var h1 = doc.Blocks.First() as Paragraph;
                Assert.NotNull(h1);

                // Collapse Section 1
                NotionMarkdownHelper.ToggleHeadingFold(h1, rtb);
                Assert.Equal(3, doc.Blocks.Count);

                // Serialize while collapsed
                string serialized = NotionMarkdownHelper.FlowDocumentToMarkdown(doc);

                // The serialized markdown MUST contain all original content loss-free!
                Assert.Contains("# Section 1", serialized);
                Assert.Contains("Line under 1", serialized);
                Assert.Contains("## Section 1.1", serialized);
                Assert.Contains("Line under 1.1", serialized);
                Assert.Contains("# Section 2", serialized);
                Assert.Contains("Line under 2", serialized);

                // And fold chevron / ellipsis symbols must NOT leak into the saved markdown
                Assert.DoesNotContain("˅", serialized);
                Assert.DoesNotContain("›", serialized);

                NotionMarkdownHelper.FoldedHeadingSections.Clear();
            });
        }

        [Fact]
        public void ConvertListItemToNormalText_BulletList_PreservesTextAndConvertsToBody()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBullet, "- asdfdf");
                Assert.Equal(NotionMarkdownHelper.TagBullet, p.Tag);

                NotionMarkdownHelper.ConvertListItemToNormalText(p);

                Assert.Equal(NotionMarkdownHelper.TagBody, p.Tag);
                Assert.Equal(new Thickness(0, 3, 0, 3), p.Margin);
                var text = NotionMarkdownHelper.InlinesToMarkdown(p.Inlines, NotionMarkdownHelper.TagBody).Trim();
                Assert.Equal("asdfdf", text);
            });
        }

        [Fact]
        public void ConvertListItemToNormalText_BulletListWithRichInlines_PreservesInlines()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBullet, "- **bold text** and `code`");
                Assert.Equal(NotionMarkdownHelper.TagBullet, p.Tag);

                NotionMarkdownHelper.ConvertListItemToNormalText(p);

                Assert.Equal(NotionMarkdownHelper.TagBody, p.Tag);
                var text = NotionMarkdownHelper.InlinesToMarkdown(p.Inlines, NotionMarkdownHelper.TagBody).Trim();
                Assert.Equal("**bold text** and `code`", text);
            });
        }

        [Fact]
        public void ConvertListItemToNormalText_OrderedList_PreservesTextAndConvertsToBody()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateOrderedListParagraph(1, "1. Hello world");
                Assert.Equal(NotionMarkdownHelper.TagOrderedList, p.Tag);

                NotionMarkdownHelper.ConvertListItemToNormalText(p);

                Assert.Equal(NotionMarkdownHelper.TagBody, p.Tag);
                var text = NotionMarkdownHelper.InlinesToMarkdown(p.Inlines, NotionMarkdownHelper.TagBody).Trim();
                Assert.Equal("Hello world", text);
            });
        }

        [Fact]
        public void ConvertListItemToNormalText_TodoItem_PreservesTextAndConvertsToBody()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagTodo, "Buy groceries");
                Assert.Equal(NotionMarkdownHelper.TagTodo, p.Tag);

                NotionMarkdownHelper.ConvertListItemToNormalText(p);

                Assert.Equal(NotionMarkdownHelper.TagBody, p.Tag);
                var text = NotionMarkdownHelper.InlinesToMarkdown(p.Inlines, NotionMarkdownHelper.TagBody).Trim();
                Assert.Equal("Buy groceries", text);
            });
        }

        [Fact]
        public void ConvertListItemToNormalText_CompletedTodo_ClearsStrikethroughAndPreservesText()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagTodoDone, "Done task");
                Assert.Equal(NotionMarkdownHelper.TagTodoDone, p.Tag);
                Assert.NotNull(p.TextDecorations);

                NotionMarkdownHelper.ConvertListItemToNormalText(p);

                Assert.Equal(NotionMarkdownHelper.TagBody, p.Tag);
                Assert.Null(p.TextDecorations);
                var text = NotionMarkdownHelper.InlinesToMarkdown(p.Inlines, NotionMarkdownHelper.TagBody).Trim();
                Assert.Equal("Done task", text);
            });
        }

        [Fact]
        public void ConvertListItemToNormalText_EmptyBulletItem_ConvertsToEmptyBody()
        {
            RunOnSta(() =>
            {
                var p = NotionMarkdownHelper.CreateParagraph(NotionMarkdownHelper.TagBullet, "");
                NotionMarkdownHelper.ConvertListItemToNormalText(p);

                Assert.Equal(NotionMarkdownHelper.TagBody, p.Tag);
                var text = NotionMarkdownHelper.InlinesToMarkdown(p.Inlines, NotionMarkdownHelper.TagBody).Trim();
                Assert.Equal("", text);
            });
        }
    }
}

