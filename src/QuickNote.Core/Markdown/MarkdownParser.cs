using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Core.Outline;

namespace QuickNote.Core.Markdown
{
    public class MarkdownParser : IMarkdownParser
    {
        private readonly OutlineBuilder _outlineBuilder = new OutlineBuilder();

        private static readonly Regex FrontMatterIdRegex = new Regex(@"^(?:id|uuid)\s*:\s*[""']?([a-fA-F0-9\-]{36})[""']?", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static Guid? ExtractIdFromFrontMatter(string frontMatter)
        {
            if (string.IsNullOrWhiteSpace(frontMatter)) return null;
            var match = FrontMatterIdRegex.Match(frontMatter);
            if (match.Success && Guid.TryParse(match.Groups[1].Value, out var guid))
            {
                return guid;
            }
            return null;
        }

        public static string EnsureFrontmatterId(string content, Guid id)
        {
            content ??= string.Empty;
            var (frontMatter, _) = ParseFrontMatterInternal(content);
            if (!string.IsNullOrEmpty(frontMatter))
            {
                var existingId = ExtractIdFromFrontMatter(frontMatter);
                if (existingId.HasValue) return content;

                var normalized = content.Replace("\r\n", "\n");
                var endFmIndex = normalized.IndexOf("\n---\n", 4);
                if (endFmIndex > 0)
                {
                    var idLine = "id: " + id.ToString("D") + "\n";
                    var remainingFm = normalized.Substring(4, endFmIndex - 4);
                    var body = normalized.Substring(endFmIndex + 5);
                    return "---\n" + idLine + (string.IsNullOrWhiteSpace(remainingFm) ? "" : remainingFm.Trim() + "\n") + "---\n" + body;
                }
            }
            return $"---\nid: {id:D}\n---\n\n" + content;
        }

        public NoteDocument Parse(string filePath, string content)
        {
            content ??= string.Empty;

            var (frontMatter, markdownContent) = ParseFrontMatterInternal(content);

            var idFromFm = ExtractIdFromFrontMatter(frontMatter);
            var noteId = idFromFm ?? GenerateIdFromPath(filePath);

            var fmTags = ExtractTagsFromFrontMatter(frontMatter);
            var inlineTags = ExtractInlineTags(markdownContent);
            var tags = fmTags.Union(inlineTags, StringComparer.OrdinalIgnoreCase).ToArray();

            var title = ExtractTitleFromFrontMatter(frontMatter);
            if (string.IsNullOrEmpty(title))
            {
                title = ExtractFirstH1Heading(markdownContent);
            }

            if (string.IsNullOrEmpty(title))
            {
                title = Path.GetFileNameWithoutExtension(filePath);
            }

            var headingTree = _outlineBuilder.BuildOutline(markdownContent);
            var flatHeadings = ExtractFlatHeadings(headingTree);

            var fileInfo = File.Exists(filePath) ? new FileInfo(filePath) : null;

            return new NoteDocument
            {
                Id = noteId,
                FullPath = filePath,
                RelativePath = Path.GetFileName(filePath),
                FileName = Path.GetFileName(filePath),
                FolderPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                Title = title,
                Snippet = ComputeRenderText(content, title),
                RenderText = ComputeRenderText(content, title),
                Content = content,
                Headings = flatHeadings.ToArray(),
                HeadingTree = headingTree,
                Tags = tags,
                LastModified = fileInfo?.LastWriteTime ?? DateTime.Now,
                FileSize = fileInfo?.Length ?? Encoding.UTF8.GetByteCount(content),
                ContentHash = ComputeContentHash(content)
            };
        }

        private static readonly Regex HashtagRegex = new Regex(@"(?<=^|\s)#([a-zA-Z][a-zA-Z0-9_\-]*)", RegexOptions.Compiled);

        private string[] ExtractInlineTags(string markdownContent)
        {
            if (string.IsNullOrWhiteSpace(markdownContent)) return Array.Empty<string>();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = markdownContent.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("# ") || trimmed.StartsWith("## ") || trimmed.StartsWith("### "))
                {
                    continue;
                }
                var matches = HashtagRegex.Matches(line);
                foreach (Match match in matches)
                {
                    var tag = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(tag))
                    {
                        set.Add(tag);
                    }
                }
            }
            return set.ToArray();
        }

        public static (string frontMatter, string markdownContent) ParseFrontMatter(string content) => ParseFrontMatterInternal(content);

        private static (string frontMatter, string markdownContent) ParseFrontMatterInternal(string content)
        {
            // Normalize CRLF to LF for front matter parsing
            if (content.StartsWith("---\r\n") || content.StartsWith("---\n"))
            {
                var normalized = content.Replace("\r\n", "\n");
                var endIndex = normalized.IndexOf("\n---\n", 4);
                if (endIndex > 0)
                {
                    var frontMatter = normalized.Substring(4, endIndex - 4);
                    var markdownContent = normalized.Substring(endIndex + 5);
                    return (frontMatter, markdownContent);
                }
            }

            return (string.Empty, content);
        }

        private string[] ExtractTagsFromFrontMatter(string frontMatter)
        {
            if (string.IsNullOrWhiteSpace(frontMatter)) return Array.Empty<string>();

            var tags = new List<string>();
            var lines = frontMatter.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
                {
                    var remainder = line.Substring(5).Trim();
                    if (remainder.StartsWith("[") && remainder.EndsWith("]"))
                    {
                        var items = remainder.Substring(1, remainder.Length - 2).Split(',');
                        foreach (var it in items)
                        {
                            var clean = it.Trim().Trim('\'', '\"');
                            if (!string.IsNullOrEmpty(clean)) tags.Add(clean);
                        }
                    }
                    else if (!string.IsNullOrEmpty(remainder))
                    {
                        var clean = remainder.Trim().Trim('\'', '\"');
                        if (!string.IsNullOrEmpty(clean)) tags.Add(clean);
                    }

                    // Check following lines for YAML list items (- tag)
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        var nextLine = lines[j].Trim();
                        if (nextLine.StartsWith("-"))
                        {
                            var tagVal = nextLine.Substring(1).Trim().Trim('\'', '\"');
                            if (!string.IsNullOrEmpty(tagVal)) tags.Add(tagVal);
                        }
                        else if (!string.IsNullOrWhiteSpace(nextLine))
                        {
                            break;
                        }
                    }
                    break;
                }
            }

            return tags.ToArray();
        }

        private string? ExtractTitleFromFrontMatter(string frontMatter)
        {
            if (string.IsNullOrWhiteSpace(frontMatter)) return null;

            var lines = frontMatter.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(6).Trim().Trim('\'', '\"');
                }
            }

            return null;
        }

        private string? ExtractFirstH1Heading(string markdownContent)
        {
            var lines = markdownContent.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("# ") && !trimmed.StartsWith("##"))
                {
                    return trimmed.Substring(2).Trim();
                }
            }

            return null;
        }

        private List<string> ExtractFlatHeadings(IEnumerable<Heading> headings)
        {
            var list = new List<string>();
            foreach (var h in headings)
            {
                list.Add(h.Text);
                if (h.Children != null && h.Children.Count > 0)
                {
                    list.AddRange(ExtractFlatHeadings(h.Children));
                }
            }
            return list;
        }

        private Guid GenerateIdFromPath(string filePath)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()));
            return new Guid(hash.Take(16).ToArray());
        }

        private string ComputeContentHash(string content)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(hash);
        }

        private static readonly MarkdownPipeline PlainTextPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        public static string ComputeSnippet(string content)
        {
            return ComputeRenderText(content);
        }

        public static string ComputeRenderText(string? content, string? title = null, int maxLength = 180)
        {
            if (string.IsNullOrWhiteSpace(content)) return "Empty note";

            var text = content.Trim();
            // 1. Strip YAML FrontMatter
            if (text.StartsWith("---"))
            {
                var end = text.IndexOf("---", 3);
                if (end > 3)
                {
                    text = text.Substring(end + 3).TrimStart('\r', '\n', ' ');
                }
            }
            if (string.IsNullOrWhiteSpace(text)) return "Empty note";

            // 2. Strip title if it repeats on the first line
            if (!string.IsNullOrWhiteSpace(title))
            {
                var cleanTitle = title.Trim();
                using var lineReader = new StringReader(text);
                var firstLine = lineReader.ReadLine();
                if (firstLine != null)
                {
                    var trimmedFirstLine = firstLine.Trim().TrimStart('#', ' ').Trim();
                    if (string.Equals(trimmedFirstLine, cleanTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        text = text.Substring(firstLine.Length).TrimStart('\r', '\n', ' ');
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(text)) return "Empty note";

            // 3. Convert markdown to plain text via Markdig
            string plainText;
            try
            {
                plainText = Markdig.Markdown.ToPlainText(text, PlainTextPipeline);
            }
            catch
            {
                plainText = text;
            }

            // 4. Clean up any remaining artifacts (e.g. raw HTML tags, backticks, table dividers, empty lines)
            plainText = Regex.Replace(plainText, @"<[^>]+>", " "); // remove html tags
            plainText = Regex.Replace(plainText, @"^\s*\|?[-: ]+[-| :]+[-: ]*\|?\s*$", "", RegexOptions.Multiline); // remove table separator lines
            plainText = Regex.Replace(plainText, @"^\s*\||\|\s*$", "", RegexOptions.Multiline); // remove leading/trailing table pipes
            plainText = Regex.Replace(plainText, @"(?<=\S)\s*\|\s*(?=\S)", " · "); // replace table pipes with clean dot
            plainText = Regex.Replace(plainText, @"[`#*~_]", ""); // remove stray markdown markers
            plainText = Regex.Replace(plainText, @"[ \t]+", " "); // collapse multiple spaces/tabs
            plainText = Regex.Replace(plainText, @"\r?\n[ \t]*\r?\n", "\n"); // collapse multiple empty lines

            // 5. Build clean multi-line snippet (up to 4 lines, max maxLength chars)
            using var reader = new StringReader(plainText);
            var sb = new StringBuilder(maxLength);
            int lineCount = 0;
            string? line;
            while ((line = reader.ReadLine()) != null && lineCount < 4)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(trimmed);
                lineCount++;
                if (sb.Length >= maxLength) break;
            }

            if (sb.Length == 0) return "Empty note";
            if (sb.Length > maxLength)
            {
                return sb.ToString(0, maxLength).TrimEnd() + "...";
            }
            return sb.ToString().Trim();
        }
    }
}