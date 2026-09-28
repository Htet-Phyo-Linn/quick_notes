using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Core.Outline;

namespace QuickNote.Core.Markdown
{
    public class MarkdownParser : IMarkdownParser
    {
        private readonly OutlineBuilder _outlineBuilder = new OutlineBuilder();

        public NoteDocument Parse(string filePath, string content)
        {
            content ??= string.Empty;

            var (frontMatter, markdownContent) = ParseFrontMatter(content);

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
                Id = GenerateIdFromPath(filePath),
                FullPath = filePath,
                RelativePath = Path.GetFileName(filePath),
                FileName = Path.GetFileName(filePath),
                FolderPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                Title = title,
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

        private (string frontMatter, string markdownContent) ParseFrontMatter(string content)
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
    }
}