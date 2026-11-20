using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;

namespace QuickNote.Core.Search
{
    public class SearchEngine : ISearchEngine
    {
        private readonly ConcurrentDictionary<Guid, NoteDocument> _index = new ConcurrentDictionary<Guid, NoteDocument>();

        public void IndexDocument(NoteDocument document)
        {
            if (document == null) return;
            _index[document.Id] = document;
        }

        public void RemoveDocument(Guid documentId)
        {
            _index.TryRemove(documentId, out _);
        }

        public void Clear()
        {
            _index.Clear();
        }

        public IEnumerable<SearchResult> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Enumerable.Empty<SearchResult>();
            }

            var cleanQuery = query.Trim();
            var results = new List<SearchResult>();

            // Optional search syntax check: tag:, title:, folder:
            string? tagFilter = null;
            string? titleFilter = null;
            string? folderFilter = null;

            if (cleanQuery.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
            {
                tagFilter = cleanQuery.Substring(4).Trim();
            }
            else if (cleanQuery.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
            {
                titleFilter = cleanQuery.Substring(6).Trim();
            }
            else if (cleanQuery.StartsWith("folder:", StringComparison.OrdinalIgnoreCase))
            {
                folderFilter = cleanQuery.Substring(7).Trim();
            }

            foreach (var doc in _index.Values)
            {
                if (tagFilter != null)
                {
                    if (doc.Tags != null && doc.Tags.Any(t => t.Contains(tagFilter, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(new SearchResult
                        {
                            DocumentId = doc.Id,
                            NoteTitle = doc.Title,
                            FilePath = doc.FullPath,
                            RelativePath = doc.RelativePath,
                            MatchType = SearchMatchType.Tag,
                            MatchedText = tagFilter,
                            Snippet = $"Tag: #{tagFilter}",
                            Score = 100
                        });
                    }
                    continue;
                }

                if (titleFilter != null)
                {
                    if (doc.Title.Contains(titleFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new SearchResult
                        {
                            DocumentId = doc.Id,
                            NoteTitle = doc.Title,
                            FilePath = doc.FullPath,
                            RelativePath = doc.RelativePath,
                            MatchType = SearchMatchType.HeadingContains,
                            MatchedText = doc.Title,
                            Snippet = doc.Title,
                            Score = 100
                        });
                    }
                    continue;
                }

                if (folderFilter != null)
                {
                    if (doc.FolderPath.Contains(folderFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new SearchResult
                        {
                            DocumentId = doc.Id,
                            NoteTitle = doc.Title,
                            FilePath = doc.FullPath,
                            RelativePath = doc.RelativePath,
                            MatchType = SearchMatchType.Folder,
                            MatchedText = doc.FolderPath,
                            Snippet = doc.RelativePath,
                            Score = 100
                        });
                    }
                    continue;
                }

                // Standard search with weighted scoring
                int bestScore = 0;
                SearchResult? bestMatch = null;

                var terms = cleanQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (terms.Length > 1)
                {
                    // Multi-word search: check if all terms are found across document fields
                    var flatHeadings = doc.HeadingTree != null ? FlattenHeadings(doc.HeadingTree).ToList() : new List<Heading>();
                    var allHeadingsText = string.Join(" ", flatHeadings.Select(h => h.Text));
                    var allTagsText = doc.Tags != null ? string.Join(" ", doc.Tags) : string.Empty;
                    var docContent = doc.Content ?? string.Empty;

                    bool allTermsMatched = true;
                    int matchedTermsCount = 0;
                    int multiScore = 0;

                    foreach (var term in terms)
                    {
                        bool inTitle = doc.Title.Contains(term, StringComparison.OrdinalIgnoreCase) || doc.FileName.Contains(term, StringComparison.OrdinalIgnoreCase);
                        bool inHeadings = allHeadingsText.Contains(term, StringComparison.OrdinalIgnoreCase);
                        bool inTags = allTagsText.Contains(term, StringComparison.OrdinalIgnoreCase);
                        bool inBody = docContent.Contains(term, StringComparison.OrdinalIgnoreCase);

                        if (inTitle || inHeadings || inTags || inBody)
                        {
                            matchedTermsCount++;
                            if (inTitle) multiScore += 30;
                            if (inHeadings) multiScore += 25;
                            if (inTags) multiScore += 20;
                            if (inBody) multiScore += 10;
                        }
                        else
                        {
                            allTermsMatched = false;
                        }
                    }

                    if (allTermsMatched && matchedTermsCount == terms.Length)
                    {
                        // Check for exact phrase bonus
                        bool exactInTitle = doc.Title.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase);
                        bool exactInHeading = allHeadingsText.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase);
                        bool exactInBody = docContent.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase);

                        if (exactInTitle) multiScore += 50;
                        else if (exactInHeading) multiScore += 40;
                        else if (exactInBody) multiScore += 30;

                        if ((DateTime.Now - doc.LastModified).TotalDays < 7) multiScore += 5;

                        // Determine best matching location for snippet & navigation
                        var bestHeading = flatHeadings.FirstOrDefault(h => terms.All(t => h.Text.Contains(t, StringComparison.OrdinalIgnoreCase)))
                                          ?? flatHeadings.FirstOrDefault(h => terms.Any(t => h.Text.Contains(t, StringComparison.OrdinalIgnoreCase)));

                        if (bestHeading != null && terms.Any(t => bestHeading.Text.Contains(t, StringComparison.OrdinalIgnoreCase)))
                        {
                            bestMatch = new SearchResult
                            {
                                DocumentId = doc.Id,
                                NoteTitle = doc.Title,
                                FilePath = doc.FullPath,
                                RelativePath = doc.RelativePath,
                                MatchType = SearchMatchType.HeadingContains,
                                MatchedText = bestHeading.Text,
                                Snippet = $"{new string('#', bestHeading.Level)} {bestHeading.Text}",
                                LineNumber = bestHeading.LineNumber,
                                StartOffset = bestHeading.StartOffset,
                                EndOffset = bestHeading.EndOffset,
                                Score = multiScore
                            };
                        }
                        else if (exactInTitle || doc.Title.Contains(terms[0], StringComparison.OrdinalIgnoreCase))
                        {
                            bestMatch = new SearchResult
                            {
                                DocumentId = doc.Id,
                                NoteTitle = doc.Title,
                                FilePath = doc.FullPath,
                                RelativePath = doc.RelativePath,
                                MatchType = SearchMatchType.Filename,
                                MatchedText = doc.Title,
                                Snippet = doc.Title,
                                LineNumber = 1,
                                StartOffset = 0,
                                Score = multiScore
                            };
                        }
                        else
                        {
                            // Find snippet in body around first matched term
                            int firstTermIdx = docContent.IndexOf(terms[0], StringComparison.OrdinalIgnoreCase);
                            if (firstTermIdx < 0 && terms.Length > 1)
                            {
                                firstTermIdx = docContent.IndexOf(terms[1], StringComparison.OrdinalIgnoreCase);
                            }

                            int lineNum = 1;
                            if (firstTermIdx >= 0)
                            {
                                for (int i = 0; i < firstTermIdx; i++)
                                {
                                    if (docContent[i] == '\n') lineNum++;
                                }
                            }
                            else
                            {
                                firstTermIdx = 0;
                            }

                            int snippetStart = Math.Max(0, firstTermIdx - 30);
                            int snippetEnd = Math.Min(docContent.Length, firstTermIdx + cleanQuery.Length + 40);
                            string snippet = docContent.Length > 0 
                                ? docContent.Substring(snippetStart, snippetEnd - snippetStart).Replace("\r", " ").Replace("\n", " ") 
                                : doc.Title;

                            if (snippetStart > 0) snippet = "..." + snippet;
                            if (snippetEnd < docContent.Length) snippet += "...";

                            bestMatch = new SearchResult
                            {
                                DocumentId = doc.Id,
                                NoteTitle = doc.Title,
                                FilePath = doc.FullPath,
                                RelativePath = doc.RelativePath,
                                MatchType = SearchMatchType.Body,
                                MatchedText = cleanQuery,
                                Snippet = snippet,
                                LineNumber = lineNum,
                                StartOffset = firstTermIdx,
                                EndOffset = firstTermIdx + terms[0].Length,
                                Score = multiScore
                            };
                        }

                        results.Add(bestMatch);
                        continue;
                    }
                }

                // 1. Check headings
                if (doc.HeadingTree != null)
                {
                    foreach (var heading in FlattenHeadings(doc.HeadingTree))
                    {
                        int score = 0;
                        SearchMatchType matchType = SearchMatchType.HeadingContains;

                        if (string.Equals(heading.Text, cleanQuery, StringComparison.OrdinalIgnoreCase))
                        {
                            if (heading.Level == 1) { score = 100; matchType = SearchMatchType.ExactH1; }
                            else if (heading.Level == 2) { score = 90; matchType = SearchMatchType.ExactH2; }
                            else { score = 80; matchType = SearchMatchType.ExactH3; }
                        }
                        else if (heading.Text.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase))
                        {
                            score = 70;
                            matchType = SearchMatchType.HeadingContains;
                        }

                        if (score > 0)
                        {
                            if (heading.Text.StartsWith(cleanQuery, StringComparison.OrdinalIgnoreCase)) score += 10;
                            if ((DateTime.Now - doc.LastModified).TotalDays < 7) score += 5;

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestMatch = new SearchResult
                                {
                                    DocumentId = doc.Id,
                                    NoteTitle = doc.Title,
                                    FilePath = doc.FullPath,
                                    RelativePath = doc.RelativePath,
                                    MatchType = matchType,
                                    MatchedText = heading.Text,
                                    Snippet = $"{new string('#', heading.Level)} {heading.Text}",
                                    LineNumber = heading.LineNumber,
                                    StartOffset = heading.StartOffset,
                                    EndOffset = heading.EndOffset,
                                    Score = score
                                };
                            }
                        }
                    }
                }

                // 2. Check filename / title
                if (doc.Title.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase) ||
                    doc.FileName.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase))
                {
                    int score = 60;
                    if (string.Equals(doc.Title, cleanQuery, StringComparison.OrdinalIgnoreCase)) score += 20;
                    if (doc.Title.StartsWith(cleanQuery, StringComparison.OrdinalIgnoreCase)) score += 10;
                    if ((DateTime.Now - doc.LastModified).TotalDays < 7) score += 5;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = new SearchResult
                        {
                            DocumentId = doc.Id,
                            NoteTitle = doc.Title,
                            FilePath = doc.FullPath,
                            RelativePath = doc.RelativePath,
                            MatchType = SearchMatchType.Filename,
                            MatchedText = doc.FileName,
                            Snippet = doc.Title,
                            LineNumber = 1,
                            StartOffset = 0,
                            Score = score
                        };
                    }
                }

                // 3. Check tags
                if (doc.Tags != null)
                {
                    var matchedTag = doc.Tags.FirstOrDefault(t => t.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase));
                    if (matchedTag != null)
                    {
                        int score = 50;
                        if (string.Equals(matchedTag, cleanQuery, StringComparison.OrdinalIgnoreCase)) score += 20;
                        if ((DateTime.Now - doc.LastModified).TotalDays < 7) score += 5;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestMatch = new SearchResult
                            {
                                DocumentId = doc.Id,
                                NoteTitle = doc.Title,
                                FilePath = doc.FullPath,
                                RelativePath = doc.RelativePath,
                                MatchType = SearchMatchType.Tag,
                                MatchedText = matchedTag,
                                Snippet = $"Tag: #{matchedTag}",
                                LineNumber = 1,
                                Score = score
                            };
                        }
                    }
                }

                // 4. Check body text
                if (!string.IsNullOrEmpty(doc.Content))
                {
                    int bodyIndex = doc.Content.IndexOf(cleanQuery, StringComparison.OrdinalIgnoreCase);
                    if (bodyIndex >= 0)
                    {
                        int score = 20;
                        if ((DateTime.Now - doc.LastModified).TotalDays < 7) score += 5;

                        if (score > bestScore || bestMatch == null)
                        {
                            // Calculate line number
                            int lineNum = 1;
                            for (int i = 0; i < bodyIndex; i++)
                            {
                                if (doc.Content[i] == '\n') lineNum++;
                            }

                            // Extract snippet
                            int snippetStart = Math.Max(0, bodyIndex - 30);
                            int snippetEnd = Math.Min(doc.Content.Length, bodyIndex + cleanQuery.Length + 40);
                            string snippet = doc.Content.Substring(snippetStart, snippetEnd - snippetStart)
                                .Replace("\r", " ").Replace("\n", " ");

                            if (snippetStart > 0) snippet = "..." + snippet;
                            if (snippetEnd < doc.Content.Length) snippet += "...";

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestMatch = new SearchResult
                                {
                                    DocumentId = doc.Id,
                                    NoteTitle = doc.Title,
                                    FilePath = doc.FullPath,
                                    RelativePath = doc.RelativePath,
                                    MatchType = SearchMatchType.Body,
                                    MatchedText = cleanQuery,
                                    Snippet = snippet,
                                    LineNumber = lineNum,
                                    StartOffset = bodyIndex,
                                    EndOffset = bodyIndex + cleanQuery.Length,
                                    Score = score
                                };
                            }
                        }
                    }
                }

                if (bestMatch != null)
                {
                    results.Add(bestMatch);
                }
            }

            return results
                .GroupBy(r => !string.IsNullOrWhiteSpace(r.FilePath) ? Path.GetFullPath(r.FilePath).ToLowerInvariant() : r.DocumentId.ToString())
                .Select(g => g.OrderByDescending(r => r.Score).First())
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.NoteTitle);
        }

        private IEnumerable<Heading> FlattenHeadings(IEnumerable<Heading> headings)
        {
            var list = new List<Heading>();
            foreach (var h in headings)
            {
                list.Add(h);
                if (h.Children != null && h.Children.Count > 0)
                {
                    list.AddRange(FlattenHeadings(h.Children));
                }
            }
            return list;
        }
    }
}
