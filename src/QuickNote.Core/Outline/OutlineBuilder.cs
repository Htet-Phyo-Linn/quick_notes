using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;

namespace QuickNote.Core.Outline
{
    public class OutlineBuilder : IOutlineService
    {
        private static readonly Regex HeadingRegex = new Regex(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);

        public List<Heading> BuildOutline(string markdownContent)
        {
            var result = new List<Heading>();
            if (string.IsNullOrEmpty(markdownContent)) return result;

            var flatHeadings = new List<Heading>();
            var lines = markdownContent.Split('\n');
            int currentOffset = 0;

            bool inCodeBlock = false;
            string codeFence = string.Empty;
            bool inFrontMatter = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var cleanLine = line.TrimEnd('\r');
                var trimmed = cleanLine.Trim();

                // Check frontmatter
                if (i == 0 && trimmed == "---")
                {
                    inFrontMatter = true;
                    currentOffset += line.Length + 1;
                    continue;
                }
                if (inFrontMatter)
                {
                    if (trimmed == "---" || trimmed == "...")
                    {
                        inFrontMatter = false;
                    }
                    currentOffset += line.Length + 1;
                    continue;
                }

                // Check code block fences: ``` or ~~~
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    var fenceType = trimmed.Substring(0, 3);
                    if (!inCodeBlock)
                    {
                        inCodeBlock = true;
                        codeFence = fenceType;
                    }
                    else if (trimmed.StartsWith(codeFence))
                    {
                        inCodeBlock = false;
                        codeFence = string.Empty;
                    }
                    currentOffset += line.Length + 1;
                    continue;
                }

                if (!inCodeBlock)
                {
                    var match = HeadingRegex.Match(cleanLine);
                    if (match.Success)
                    {
                        int level = match.Groups[1].Value.Length;
                        string text = match.Groups[2].Value.Trim();

                        flatHeadings.Add(new Heading
                        {
                            Level = level,
                            Text = text,
                            LineNumber = i + 1,
                            StartOffset = currentOffset,
                            EndOffset = currentOffset + cleanLine.Length
                        });
                    }
                }

                currentOffset += line.Length + 1; // +1 for newline character
            }

            // Build hierarchical tree
            var stack = new Stack<Heading>();

            foreach (var heading in flatHeadings)
            {
                while (stack.Count > 0 && stack.Peek().Level >= heading.Level)
                {
                    stack.Pop();
                }

                if (stack.Count == 0)
                {
                    result.Add(heading);
                }
                else
                {
                    stack.Peek().Children.Add(heading);
                }

                stack.Push(heading);
            }

            return result;
        }

        public Heading? FindActiveHeading(IEnumerable<Heading> outline, int cursorLineNumber)
        {
            var flat = new List<Heading>();
            FlattenOutline(outline, flat);

            Heading? active = null;
            foreach (var h in flat.OrderBy(x => x.LineNumber))
            {
                if (h.LineNumber <= cursorLineNumber)
                {
                    active = h;
                }
                else
                {
                    break;
                }
            }

            return active;
        }

        private void FlattenOutline(IEnumerable<Heading> headings, List<Heading> flat)
        {
            foreach (var h in headings)
            {
                flat.Add(h);
                if (h.Children.Count > 0)
                {
                    FlattenOutline(h.Children, flat);
                }
            }
        }
    }
}
