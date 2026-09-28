using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using QuickNote.Core.Models;

namespace QuickNote.App.Helpers
{
    public class ExportImageInfo
    {
        public int Id { get; set; }
        public string RelId => $"rIdImg{Id}";
        public string ZipPath => $"word/media/image_{Id}{Extension}";
        public string Extension { get; set; } = ".png";
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public long WidthEmu { get; set; }
        public long HeightEmu { get; set; }
        public string AltText { get; set; } = string.Empty;
    }

    public static class DocxExportHelper
    {
        public static async Task ExportToDocxAsync(NoteDocument note, string targetFilePath, string? notesRoot = null)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            if (string.IsNullOrWhiteSpace(targetFilePath)) throw new ArgumentException("Target file path cannot be empty.", nameof(targetFilePath));

            var content = note.Content ?? string.Empty;
            var images = new List<ExportImageInfo>();
            var documentXml = await BuildDocumentXmlAsync(note, content, images, notesRoot);

            // Delete target file if it already exists
            if (File.Exists(targetFilePath))
            {
                File.Delete(targetFilePath);
            }

            // Create .docx ZIP archive
            using var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, true);

            // 1. [Content_Types].xml
            var contentTypesEntry = archive.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(contentTypesEntry.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync(GetContentTypesXml(images));
            }

            // 2. _rels/.rels
            var relsEntry = archive.CreateEntry("_rels/.rels");
            using (var writer = new StreamWriter(relsEntry.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync(GetRelsXml());
            }

            // 3. word/document.xml
            var docEntry = archive.CreateEntry("word/document.xml");
            using (var writer = new StreamWriter(docEntry.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync(documentXml);
            }

            // 4. word/styles.xml (Word Heading 1-6, Title, Normal styles for Outline Navigation)
            var stylesEntry = archive.CreateEntry("word/styles.xml");
            using (var writer = new StreamWriter(stylesEntry.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync(GetStylesXml());
            }

            // 5. word/settings.xml (Modern Word mode, remove compatibility bar)
            var settingsEntry = archive.CreateEntry("word/settings.xml");
            using (var writer = new StreamWriter(settingsEntry.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync(GetSettingsXml());
            }

            // 6. word/_rels/document.xml.rels
            var docRelsEntry = archive.CreateEntry("word/_rels/document.xml.rels");
            using (var writer = new StreamWriter(docRelsEntry.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync(GetDocumentRelsXml(images));
            }

            // 7. word/media/image_{id}.ext (embedded image binaries)
            foreach (var img in images)
            {
                var mediaEntry = archive.CreateEntry(img.ZipPath);
                using var mediaStream = mediaEntry.Open();
                await mediaStream.WriteAsync(img.Data, 0, img.Data.Length);
            }
        }

        private static string GetContentTypesXml(List<ExportImageInfo> images)
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "png", "jpg", "jpeg", "gif", "bmp", "webp" };
            foreach (var img in images)
            {
                var ext = img.Extension.TrimStart('.').ToLowerInvariant();
                if (!string.IsNullOrEmpty(ext)) extensions.Add(ext);
            }

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n");
            sb.Append("  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n");
            sb.Append("  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n");
            foreach (var ext in extensions)
            {
                string cType = ext switch
                {
                    "png" => "image/png",
                    "jpg" or "jpeg" => "image/jpeg",
                    "gif" => "image/gif",
                    "bmp" => "image/bmp",
                    "webp" => "image/webp",
                    _ => "application/octet-stream"
                };
                sb.Append($"  <Default Extension=\"{ext}\" ContentType=\"{cType}\"/>\n");
            }
            sb.Append("  <Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>\n");
            sb.Append("  <Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>\n");
            sb.Append("  <Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>\n");
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string GetRelsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n" +
                   "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>\n" +
                   "</Relationships>";
        }

        private static string GetDocumentRelsXml(List<ExportImageInfo> images)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n");
            sb.Append("  <Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\n");
            sb.Append("  <Relationship Id=\"rIdSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>\n");
            foreach (var img in images)
            {
                sb.Append($"  <Relationship Id=\"{img.RelId}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image_{img.Id}{img.Extension}\"/>\n");
            }
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private static string GetSettingsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                   "<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\n" +
                   "  <w:compat>\n" +
                   "    <w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/>\n" +
                   "  </w:compat>\n" +
                   "</w:settings>";
        }

        private static string GetStylesXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                   "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\n" +
                   "  <w:docDefaults>\n" +
                   "    <w:rPrDefault>\n" +
                   "      <w:rPr>\n" +
                   "        <w:rFonts w:ascii=\"Segoe UI\" w:hAnsi=\"Segoe UI\" w:eastAsia=\"Segoe UI\" w:cs=\"Segoe UI\"/>\n" +
                   "        <w:sz w:val=\"24\"/>\n" +
                   "        <w:szCs w:val=\"24\"/>\n" +
                   "        <w:color w:val=\"111827\"/>\n" +
                   "        <w:lang w:val=\"en-US\"/>\n" +
                   "      </w:rPr>\n" +
                   "    </w:rPrDefault>\n" +
                   "    <w:pPrDefault>\n" +
                   "      <w:pPr>\n" +
                   "        <w:spacing w:after=\"120\" w:line=\"276\" w:lineRule=\"auto\"/>\n" +
                   "      </w:pPr>\n" +
                   "    </w:pPrDefault>\n" +
                   "  </w:docDefaults>\n" +
                   "  <w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\">\n" +
                   "    <w:name w:val=\"Normal\"/>\n" +
                   "    <w:qFormat/>\n" +
                   "  </w:style>\n" +
                   "  <w:style w:type=\"paragraph\" w:styleId=\"Title\">\n" +
                   "    <w:name w:val=\"Title\"/>\n" +
                   "    <w:basedOn w:val=\"Normal\"/>\n" +
                   "    <w:next w:val=\"Normal\"/>\n" +
                   "    <w:qFormat/>\n" +
                   "    <w:pPr>\n" +
                   "      <w:spacing w:before=\"240\" w:after=\"160\"/>\n" +
                   "    </w:pPr>\n" +
                   "    <w:rPr>\n" +
                   "      <w:b/>\n" +
                   "      <w:sz w:val=\"52\"/>\n" +
                   "      <w:szCs w:val=\"52\"/>\n" +
                   "      <w:color w:val=\"111827\"/>\n" +
                   "    </w:rPr>\n" +
                   "  </w:style>\n" +
                   "  <w:style w:type=\"paragraph\" w:styleId=\"Heading1\">\n" +
                   "    <w:name w:val=\"heading 1\"/>\n" +
                   "    <w:basedOn w:val=\"Normal\"/>\n" +
                   "    <w:next w:val=\"Normal\"/>\n" +
                   "    <w:uiPriority w:val=\"9\"/>\n" +
                   "    <w:qFormat/>\n" +
                   "    <w:pPr>\n" +
                   "      <w:keepNext/>\n" +
                   "      <w:spacing w:before=\"360\" w:after=\"120\"/>\n" +
                   "      <w:outlineLvl w:val=\"0\"/>\n" +
                   "    </w:pPr>\n" +
                   "    <w:rPr>\n" +
                   "      <w:b/>\n" +
                   "      <w:sz w:val=\"40\"/>\n" +
                   "      <w:szCs w:val=\"40\"/>\n" +
                   "      <w:color w:val=\"1F2937\"/>\n" +
                   "    </w:rPr>\n" +
                   "  </w:style>\n" +
                   "  <w:style w:type=\"paragraph\" w:styleId=\"Heading2\">\n" +
                   "    <w:name w:val=\"heading 2\"/>\n" +
                   "    <w:basedOn w:val=\"Normal\"/>\n" +
                   "    <w:next w:val=\"Normal\"/>\n" +
                   "    <w:uiPriority w:val=\"9\"/>\n" +
                   "    <w:qFormat/>\n" +
                   "    <w:pPr>\n" +
                   "      <w:keepNext/>\n" +
                   "      <w:spacing w:before=\"280\" w:after=\"100\"/>\n" +
                   "      <w:outlineLvl w:val=\"1\"/>\n" +
                   "    </w:pPr>\n" +
                   "    <w:rPr>\n" +
                   "      <w:b/>\n" +
                   "      <w:sz w:val=\"32\"/>\n" +
                   "      <w:szCs w:val=\"32\"/>\n" +
                   "      <w:color w:val=\"374151\"/>\n" +
                   "    </w:rPr>\n" +
                   "  </w:style>\n" +
                   "  <w:style w:type=\"paragraph\" w:styleId=\"Heading3\">\n" +
                   "    <w:name w:val=\"heading 3\"/>\n" +
                   "    <w:basedOn w:val=\"Normal\"/>\n" +
                   "    <w:next w:val=\"Normal\"/>\n" +
                   "    <w:uiPriority w:val=\"9\"/>\n" +
                   "    <w:qFormat/>\n" +
                   "    <w:pPr>\n" +
                   "      <w:keepNext/>\n" +
                   "      <w:spacing w:before=\"220\" w:after=\"80\"/>\n" +
                   "      <w:outlineLvl w:val=\"2\"/>\n" +
                   "    </w:pPr>\n" +
                   "    <w:rPr>\n" +
                   "      <w:b/>\n" +
                   "      <w:sz w:val=\"28\"/>\n" +
                   "      <w:szCs w:val=\"28\"/>\n" +
                   "      <w:color w:val=\"4B5563\"/>\n" +
                   "    </w:rPr>\n" +
                   "  </w:style>\n" +
                   "  <w:style w:type=\"paragraph\" w:styleId=\"Heading4\">\n" +
                   "    <w:name w:val=\"heading 4\"/>\n" +
                   "    <w:basedOn w:val=\"Normal\"/>\n" +
                   "    <w:next w:val=\"Normal\"/>\n" +
                   "    <w:uiPriority w:val=\"9\"/>\n" +
                   "    <w:qFormat/>\n" +
                   "    <w:pPr>\n" +
                   "      <w:keepNext/>\n" +
                   "      <w:spacing w:before=\"180\" w:after=\"60\"/>\n" +
                   "      <w:outlineLvl w:val=\"3\"/>\n" +
                   "    </w:pPr>\n" +
                   "    <w:rPr>\n" +
                   "      <w:b/>\n" +
                   "      <w:sz w:val=\"26\"/>\n" +
                   "      <w:szCs w:val=\"26\"/>\n" +
                   "      <w:color w:val=\"4B5563\"/>\n" +
                   "    </w:rPr>\n" +
                   "  </w:style>\n" +
                   "  <w:style w:type=\"paragraph\" w:styleId=\"Heading5\">\n" +
                   "    <w:name w:val=\"heading 5\"/>\n" +
                   "    <w:basedOn w:val=\"Normal\"/>\n" +
                   "    <w:next w:val=\"Normal\"/>\n" +
                   "    <w:uiPriority w:val=\"9\"/>\n" +
                   "    <w:qFormat/>\n" +
                   "    <w:pPr>\n" +
                   "      <w:keepNext/>\n" +
                   "      <w:spacing w:before=\"140\" w:after=\"40\"/>\n" +
                   "      <w:outlineLvl w:val=\"4\"/>\n" +
                   "    </w:pPr>\n" +
                   "    <w:rPr>\n" +
                   "      <w:b/>\n" +
                   "      <w:sz w:val=\"24\"/>\n" +
                   "      <w:szCs w:val=\"24\"/>\n" +
                   "      <w:color w:val=\"6B7280\"/>\n" +
                   "    </w:rPr>\n" +
                   "  </w:style>\n" +
                   "  <w:style w:type=\"paragraph\" w:styleId=\"Heading6\">\n" +
                   "    <w:name w:val=\"heading 6\"/>\n" +
                   "    <w:basedOn w:val=\"Normal\"/>\n" +
                   "    <w:next w:val=\"Normal\"/>\n" +
                   "    <w:uiPriority w:val=\"9\"/>\n" +
                   "    <w:qFormat/>\n" +
                   "    <w:pPr>\n" +
                   "      <w:keepNext/>\n" +
                   "      <w:spacing w:before=\"120\" w:after=\"40\"/>\n" +
                   "      <w:outlineLvl w:val=\"5\"/>\n" +
                   "    </w:pPr>\n" +
                   "    <w:rPr>\n" +
                   "      <w:b/>\n" +
                   "      <w:sz w:val=\"24\"/>\n" +
                   "      <w:szCs w:val=\"24\"/>\n" +
                   "      <w:color w:val=\"9CA3AF\"/>\n" +
                   "    </w:rPr>\n" +
                   "  </w:style>\n" +
                   "</w:styles>";
        }

        private static async Task<string> BuildDocumentXmlAsync(NoteDocument note, string markdownContent, List<ExportImageInfo> images, string? notesRoot)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
            sb.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" ");
            sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" ");
            sb.Append("xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" ");
            sb.Append("xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" ");
            sb.Append("xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" ");
            sb.Append("xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">\n");
            sb.Append("  <w:body>\n");

            // 1. Document Title
            if (!string.IsNullOrWhiteSpace(note.Title))
            {
                sb.Append("    <w:p>\n");
                sb.Append("      <w:pPr><w:spacing w:before=\"240\" w:after=\"160\"/></w:pPr>\n");
                sb.Append("      <w:r>\n");
                sb.Append("        <w:rPr><w:b/><w:sz w:val=\"52\"/><w:rFonts w:ascii=\"Segoe UI\" w:hAnsi=\"Segoe UI\"/><w:color w:val=\"111827\"/></w:rPr>\n");
                sb.Append($"        <w:t>{XmlEscape(note.Title)}</w:t>\n");
                sb.Append("      </w:r>\n");
                sb.Append("    </w:p>\n");
            }

            // Strip YAML front matter if present
            var normalized = markdownContent.Replace("\r\n", "\n");
            if (normalized.StartsWith("---\n"))
            {
                var endIndex = normalized.IndexOf("\n---\n", 4);
                if (endIndex > 0)
                {
                    normalized = normalized.Substring(endIndex + 5);
                }
            }

            var rawLines = normalized.Split('\n');

            // Skip first line if it's identical to the note title: "# Title"
            int startIdx = 0;
            if (rawLines.Length > 0 && !string.IsNullOrWhiteSpace(note.Title))
            {
                var firstTrimmed = rawLines[0].Trim();
                if (firstTrimmed.StartsWith("# ") && firstTrimmed.Substring(2).Trim().Equals(note.Title.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    startIdx = 1;
                }
            }

            for (int i = startIdx; i < rawLines.Length; i++)
            {
                var line = rawLines[i].TrimEnd();
                var trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    sb.Append("    <w:p><w:pPr><w:spacing w:before=\"40\" w:after=\"40\"/></w:pPr></w:p>\n");
                    continue;
                }

                // 2. Table parsing check: | col | col |
                if (trimmed.StartsWith("|") && trimmed.EndsWith("|") && trimmed.Length > 2)
                {
                    var tableLines = new List<string>();
                    while (i < rawLines.Length)
                    {
                        var tLine = rawLines[i].Trim();
                        if (tLine.StartsWith("|") && tLine.EndsWith("|") && tLine.Length > 2)
                        {
                            tableLines.Add(rawLines[i]);
                            i++;
                        }
                        else
                        {
                            i--;
                            break;
                        }
                    }

                    var tableXml = BuildTableXml(tableLines);
                    if (!string.IsNullOrEmpty(tableXml))
                    {
                        sb.Append(tableXml);
                        continue;
                    }
                }

                // 3. Horizontal Rule Divider check: ---, ***, ___
                if (NotionMarkdownHelper.IsHorizontalRule(trimmed))
                {
                    sb.Append(CreateDividerXml());
                    continue;
                }

                // 4. Image tag check: ![alt](src) or ![](src) or ![alt](<src>)
                var imgMatch = Regex.Match(trimmed, @"^!\[(?<alt>.*?)\]\((?:<(?<src>[^>]+)>|(?<src>.*?))\)");
                if (imgMatch.Success)
                {
                    var alt = imgMatch.Groups["alt"].Value;
                    var src = imgMatch.Groups["src"].Value.Trim();
                    if (src.StartsWith("<") && src.EndsWith(">") && src.Length >= 2)
                    {
                        src = src.Substring(1, src.Length - 2).Trim();
                    }
                    var spaceIdx = src.IndexOf(' ');
                    if (spaceIdx > 0 && (src.EndsWith("\"") || src.EndsWith("'")))
                    {
                        src = src.Substring(0, spaceIdx).Trim();
                    }
                    var imgInfo = await LoadImageInfoAsync(src, alt, note.FolderPath, notesRoot, images.Count + 1);
                    if (imgInfo != null)
                    {
                        images.Add(imgInfo);
                        sb.Append(CreateDrawingXml(imgInfo));
                        continue;
                    }
                }

                // 5. Headings
                if (line.StartsWith("# ") && !line.StartsWith("##"))
                {
                    var text = line.Substring(2).Trim();
                    sb.Append(CreateParagraphXml(text, fontSizeHalfPts: 38, isBold: true, spaceBefore: 240, spaceAfter: 80, colorHex: "1F2937"));
                }
                else if (line.StartsWith("## ") && !line.StartsWith("###"))
                {
                    var text = line.Substring(3).Trim();
                    sb.Append(CreateParagraphXml(text, fontSizeHalfPts: 30, isBold: true, spaceBefore: 200, spaceAfter: 60, colorHex: "374151"));
                }
                else if (line.StartsWith("### "))
                {
                    var text = line.Substring(4).Trim();
                    sb.Append(CreateParagraphXml(text, fontSizeHalfPts: 26, isBold: true, spaceBefore: 160, spaceAfter: 40, colorHex: "4B5563"));
                }
                // 6. Checkboxes
                else if (line.StartsWith("- [ ] ") || line.StartsWith("* [ ] "))
                {
                    var text = line.Substring(6);
                    sb.Append(CreateCheckboxXml(text, isChecked: false));
                }
                else if (line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("* [x] ", StringComparison.OrdinalIgnoreCase))
                {
                    var text = line.Substring(6);
                    sb.Append(CreateCheckboxXml(text, isChecked: true));
                }
                // 7. Bullets
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    var text = line.Substring(2);
                    sb.Append(CreateBulletXml(text));
                }
                // 8. Blockquotes
                else if (line.StartsWith("> "))
                {
                    var text = line.Substring(2);
                    sb.Append(CreateParagraphXml(text, fontSizeHalfPts: 24, isBold: false, isItalic: true, leftIndentTwips: 360, spaceBefore: 80, spaceAfter: 80, colorHex: "6B7280", hasLeftBorder: true));
                }
                else if (line == ">")
                {
                    sb.Append(CreateParagraphXml(string.Empty, fontSizeHalfPts: 24, leftIndentTwips: 360, hasLeftBorder: true));
                }
                // 9. Normal body paragraph
                else
                {
                    sb.Append(CreateParagraphXml(line, fontSizeHalfPts: 24, isBold: false, spaceBefore: 40, spaceAfter: 40, colorHex: "37352F"));
                }
            }

            sb.Append("  </w:body>\n");
            sb.Append("</w:document>");
            return sb.ToString();
        }

        private static string BuildTableXml(List<string> lines)
        {
            var parsedRows = new List<List<string>>();
            int maxCols = 0;
            int separatorIndex = -1;
            var alignments = new List<string>();

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("|")) line = line.Substring(1);
                if (line.EndsWith("|")) line = line.Substring(0, line.Length - 1);

                var cells = line.Split('|').Select(c => c.Trim()).ToList();

                // Check if this is a separator row like: | --- | --- | --- |
                bool isSep = cells.Count > 0 && cells.All(c => c.Length > 0 && c.All(ch => ch == '-' || ch == ':' || ch == ' '));
                if (isSep && separatorIndex == -1)
                {
                    separatorIndex = i;
                    foreach (var cell in cells)
                    {
                        if (cell.StartsWith(":") && cell.EndsWith(":")) alignments.Add("center");
                        else if (cell.EndsWith(":")) alignments.Add("right");
                        else alignments.Add("left");
                    }
                    continue;
                }

                if (cells.Count > maxCols) maxCols = cells.Count;
                parsedRows.Add(cells);
            }

            if (parsedRows.Count == 0 || maxCols == 0) return string.Empty;

            // Normalize column counts
            foreach (var row in parsedRows)
            {
                while (row.Count < maxCols) row.Add(string.Empty);
            }
            while (alignments.Count < maxCols) alignments.Add("left");

            var sb = new StringBuilder();
            sb.Append("    <w:tbl>\n");
            sb.Append("      <w:tblPr>\n");
            sb.Append("        <w:tblW w:w=\"0\" w:type=\"auto\"/>\n");
            sb.Append("        <w:tblBorders>\n");
            sb.Append("          <w:top w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"D4D4D0\"/>\n");
            sb.Append("          <w:left w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"D4D4D0\"/>\n");
            sb.Append("          <w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"D4D4D0\"/>\n");
            sb.Append("          <w:right w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"D4D4D0\"/>\n");
            sb.Append("          <w:insideH w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"D4D4D0\"/>\n");
            sb.Append("          <w:insideV w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"D4D4D0\"/>\n");
            sb.Append("        </w:tblBorders>\n");
            sb.Append("        <w:tblCellMar>\n");
            sb.Append("          <w:top w:w=\"120\" w:type=\"dxa\"/>\n");
            sb.Append("          <w:bottom w:w=\"120\" w:type=\"dxa\"/>\n");
            sb.Append("          <w:left w:w=\"160\" w:type=\"dxa\"/>\n");
            sb.Append("          <w:right w:w=\"160\" w:type=\"dxa\"/>\n");
            sb.Append("        </w:tblCellMar>\n");
            sb.Append("      </w:tblPr>\n");

            bool hasHeader = separatorIndex > 0;
            for (int r = 0; r < parsedRows.Count; r++)
            {
                var row = parsedRows[r];
                bool isHeaderRow = (r == 0 && hasHeader);

                sb.Append("      <w:tr>\n");
                if (isHeaderRow)
                {
                    sb.Append("        <w:trPr><w:tblHeader/></w:trPr>\n");
                }

                for (int c = 0; c < row.Count; c++)
                {
                    var cellText = row[c];
                    var align = alignments[c];

                    sb.Append("        <w:tc>\n");
                    sb.Append("          <w:tcPr>\n");
                    if (isHeaderRow)
                    {
                        sb.Append("            <w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F3F4F6\"/>\n");
                    }
                    sb.Append("          </w:tcPr>\n");

                    // OpenXML requires at least one <w:p> inside <w:tc>
                    sb.Append("          <w:p>\n");
                    sb.Append("            <w:pPr>\n");
                    if (align != "left")
                    {
                        sb.Append($"              <w:jc w:val=\"{align}\"/>\n");
                    }
                    sb.Append("              <w:spacing w:before=\"60\" w:after=\"60\"/>\n");
                    sb.Append("            </w:pPr>\n");

                    if (!string.IsNullOrWhiteSpace(cellText))
                    {
                        AppendFormattedRuns(sb, cellText, defaultBold: isHeaderRow, fontSizeHalfPts: isHeaderRow ? 24 : 22, defaultColorHex: isHeaderRow ? "111827" : "37352F");
                    }
                    else
                    {
                        sb.Append("            <w:r><w:rPr><w:sz w:val=\"22\"/></w:rPr><w:t></w:t></w:r>\n");
                    }

                    sb.Append("          </w:p>\n");
                    sb.Append("        </w:tc>\n");
                }

                sb.Append("      </w:tr>\n");
            }

            sb.Append("    </w:tbl>\n");
            // Trailing paragraph after table for smooth cursor placement
            sb.Append("    <w:p><w:pPr><w:spacing w:before=\"60\" w:after=\"60\"/></w:pPr></w:p>\n");

            return sb.ToString();
        }

        private static string CreateDividerXml()
        {
            var sb = new StringBuilder();
            sb.Append("    <w:p>\n");
            sb.Append("      <w:pPr>\n");
            sb.Append("        <w:pBdr>\n");
            sb.Append("          <w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"1\" w:color=\"D4D4D0\"/>\n");
            sb.Append("        </w:pBdr>\n");
            sb.Append("        <w:spacing w:before=\"160\" w:after=\"160\"/>\n");
            sb.Append("      </w:pPr>\n");
            sb.Append("    </w:p>\n");
            return sb.ToString();
        }

        private static async Task<ExportImageInfo?> LoadImageInfoAsync(string src, string alt, string? noteFolderPath, string? notesRoot, int id)
        {
            if (string.IsNullOrWhiteSpace(src)) return null;

            byte[]? data = null;
            string ext = ".png";

            if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var headerEnd = src.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
                    if (headerEnd > 0)
                    {
                        var mime = src.Substring(11, headerEnd - 11).ToLowerInvariant();
                        ext = mime switch
                        {
                            "jpeg" or "jpg" => ".jpg",
                            "gif" => ".gif",
                            "webp" => ".webp",
                            "bmp" => ".bmp",
                            _ => ".png"
                        };
                        var cleanB64 = src.Substring(headerEnd + 8);
                        data = Convert.FromBase64String(cleanB64);
                    }
                }
                catch { }
            }
            else if (Uri.TryCreate(src, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    data = await client.GetByteArrayAsync(uri);
                    ext = Path.GetExtension(uri.AbsolutePath);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                }
                catch { }
            }
            else
            {
                string? resolvedPath = ResolveImagePath(src, noteFolderPath, notesRoot);
                if (resolvedPath != null && File.Exists(resolvedPath))
                {
                    try
                    {
                        data = await File.ReadAllBytesAsync(resolvedPath);
                        ext = Path.GetExtension(resolvedPath);
                        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                    }
                    catch { }
                }
            }

            if (data == null || data.Length == 0) return null;

            ext = ext.ToLowerInvariant();
            if (ext == ".jpeg") ext = ".jpg";

            int pixelWidth = 600;
            int pixelHeight = 400;

            try
            {
                using var ms = new MemoryStream(data);
                var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count > 0)
                {
                    pixelWidth = decoder.Frames[0].PixelWidth;
                    pixelHeight = decoder.Frames[0].PixelHeight;
                }
            }
            catch { }

            if (pixelWidth <= 0) pixelWidth = 600;
            if (pixelHeight <= 0) pixelHeight = 400;

            long widthEmu = (long)pixelWidth * 9525;
            long heightEmu = (long)pixelHeight * 9525;

            const long maxEmuWidth = 5200000;
            const long maxEmuHeight = 7000000;

            if (widthEmu > maxEmuWidth)
            {
                double scale = (double)maxEmuWidth / widthEmu;
                widthEmu = maxEmuWidth;
                heightEmu = (long)(heightEmu * scale);
            }

            if (heightEmu > maxEmuHeight)
            {
                double scale = (double)maxEmuHeight / heightEmu;
                heightEmu = maxEmuHeight;
                widthEmu = (long)(widthEmu * scale);
            }

            return new ExportImageInfo
            {
                Id = id,
                Extension = ext,
                Data = data,
                WidthEmu = widthEmu,
                HeightEmu = heightEmu,
                AltText = alt
            };
        }

        public static string? ResolveImagePath(string src, string? noteFolderPath, string? notesRoot)
        {
            if (string.IsNullOrWhiteSpace(src)) return null;

            var cleanSrc = src.Trim();
            if (cleanSrc.StartsWith("<") && cleanSrc.EndsWith(">") && cleanSrc.Length >= 2)
            {
                cleanSrc = cleanSrc.Substring(1, cleanSrc.Length - 2).Trim();
            }
            if (cleanSrc.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            {
                cleanSrc = cleanSrc.Substring(8);
            }
            else if (cleanSrc.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                cleanSrc = cleanSrc.Substring(7);
            }

            cleanSrc = Uri.UnescapeDataString(cleanSrc).Replace('/', Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(cleanSrc) && File.Exists(cleanSrc))
            {
                return cleanSrc;
            }

            var fileName = Path.GetFileName(cleanSrc);

            if (!string.IsNullOrWhiteSpace(noteFolderPath))
            {
                var localToNote = Path.Combine(noteFolderPath, cleanSrc);
                if (File.Exists(localToNote)) return localToNote;

                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var pAssets = Path.Combine(noteFolderPath, ".assets", fileName);
                    if (File.Exists(pAssets)) return pAssets;

                    var pAssets2 = Path.Combine(noteFolderPath, "assets", fileName);
                    if (File.Exists(pAssets2)) return pAssets2;
                }

                try
                {
                    var currentDir = new DirectoryInfo(noteFolderPath);
                    while (currentDir != null && currentDir.Parent != null)
                    {
                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            var parentAssets = Path.Combine(currentDir.FullName, ".assets", fileName);
                            if (File.Exists(parentAssets)) return parentAssets;

                            var parentAssets2 = Path.Combine(currentDir.FullName, "assets", fileName);
                            if (File.Exists(parentAssets2)) return parentAssets2;
                        }
                        if (!string.IsNullOrWhiteSpace(notesRoot) && string.Equals(currentDir.FullName, notesRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                        currentDir = currentDir.Parent;
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(notesRoot))
            {
                var localToRoot = Path.Combine(notesRoot, cleanSrc);
                if (File.Exists(localToRoot)) return localToRoot;

                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var inDotAssets = Path.Combine(notesRoot, ".assets", fileName);
                    if (File.Exists(inDotAssets)) return inDotAssets;

                    var inAssets = Path.Combine(notesRoot, "assets", fileName);
                    if (File.Exists(inAssets)) return inAssets;
                }
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var standardLocations = new[]
            {
                Path.Combine(userProfile, "OneDrive - BIM ADVANCED TECHNOLOGY SERVICES PTE. LTD", "QuickNote"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QuickNote"),
                Path.Combine(userProfile, "OneDrive", "QuickNote")
            };

            foreach (var loc in standardLocations)
            {
                if (Directory.Exists(loc))
                {
                    var p = Path.Combine(loc, cleanSrc);
                    if (File.Exists(p)) return p;

                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        var pDotAssets = Path.Combine(loc, ".assets", fileName);
                        if (File.Exists(pDotAssets)) return pDotAssets;

                        var pAssets = Path.Combine(loc, "assets", fileName);
                        if (File.Exists(pAssets)) return pAssets;
                    }
                }
            }

            return null;
        }

        private static string CreateDrawingXml(ExportImageInfo img)
        {
            var sb = new StringBuilder();
            sb.Append("    <w:p>\n");
            sb.Append("      <w:pPr>\n");
            sb.Append("        <w:spacing w:before=\"120\" w:after=\"120\"/>\n");
            sb.Append("      </w:pPr>\n");
            sb.Append("      <w:r>\n");
            sb.Append("        <w:drawing>\n");
            sb.Append("          <wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">\n");
            sb.Append($"            <wp:extent cx=\"{img.WidthEmu}\" cy=\"{img.HeightEmu}\"/>\n");
            sb.Append("            <wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>\n");
            sb.Append($"            <wp:docPr id=\"{img.Id}\" name=\"Picture {img.Id}\" descr=\"{XmlEscape(img.AltText)}\"/>\n");
            sb.Append("            <wp:cNvGraphicFramePr>\n");
            sb.Append("              <a:graphicFrameLocks xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" noChangeAspect=\"1\"/>\n");
            sb.Append("            </wp:cNvGraphicFramePr>\n");
            sb.Append("            <a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">\n");
            sb.Append("              <a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">\n");
            sb.Append("                <pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">\n");
            sb.Append("                  <pic:nvPicPr>\n");
            sb.Append($"                    <pic:cNvPr id=\"{img.Id}\" name=\"image_{img.Id}\"/>\n");
            sb.Append("                    <pic:cNvPicPr/>\n");
            sb.Append("                  </pic:nvPicPr>\n");
            sb.Append("                  <pic:blipFill>\n");
            sb.Append($"                    <a:blip r:embed=\"{img.RelId}\"/>\n");
            sb.Append("                    <a:stretch><a:fillRect/></a:stretch>\n");
            sb.Append("                  </pic:blipFill>\n");
            sb.Append("                  <pic:spPr>\n");
            sb.Append("                    <a:xfrm>\n");
            sb.Append("                      <a:off x=\"0\" y=\"0\"/>\n");
            sb.Append($"                      <a:ext cx=\"{img.WidthEmu}\" cy=\"{img.HeightEmu}\"/>\n");
            sb.Append("                    </a:xfrm>\n");
            sb.Append("                    <a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>\n");
            sb.Append("                  </pic:spPr>\n");
            sb.Append("                </pic:pic>\n");
            sb.Append("              </a:graphicData>\n");
            sb.Append("            </a:graphic>\n");
            sb.Append("          </wp:inline>\n");
            sb.Append("        </w:drawing>\n");
            sb.Append("      </w:r>\n");
            sb.Append("    </w:p>\n");
            return sb.ToString();
        }

        private static string CreateCheckboxXml(string text, bool isChecked)
        {
            var sb = new StringBuilder();
            sb.Append("    <w:p>\n");
            sb.Append("      <w:pPr>\n");
            sb.Append("        <w:ind w:left=\"360\"/>\n");
            sb.Append("        <w:spacing w:before=\"40\" w:after=\"40\"/>\n");
            sb.Append("      </w:pPr>\n");
            sb.Append("      <w:r>\n");
            sb.Append("        <w:rPr>\n");
            sb.Append("          <w:rFonts w:ascii=\"Segoe UI Symbol\" w:hAnsi=\"Segoe UI Symbol\"/>\n");
            sb.Append("          <w:sz w:val=\"24\"/>\n");
            sb.Append($"          <w:color w:val=\"{(isChecked ? "10B981" : "6B7280")}\"/>\n");
            sb.Append("        </w:rPr>\n");
            sb.Append($"        <w:t xml:space=\"preserve\">{(isChecked ? "☑ " : "☐ ")}</w:t>\n");
            sb.Append("      </w:r>\n");

            AppendFormattedRuns(sb, text, defaultStrike: isChecked, fontSizeHalfPts: 24, defaultColorHex: isChecked ? "9CA3AF" : "37352F");

            sb.Append("    </w:p>\n");
            return sb.ToString();
        }

        private static string CreateBulletXml(string text)
        {
            var sb = new StringBuilder();
            sb.Append("    <w:p>\n");
            sb.Append("      <w:pPr>\n");
            sb.Append("        <w:ind w:left=\"360\"/>\n");
            sb.Append("        <w:spacing w:before=\"40\" w:after=\"40\"/>\n");
            sb.Append("      </w:pPr>\n");
            sb.Append("      <w:r>\n");
            sb.Append("        <w:rPr>\n");
            sb.Append("          <w:rFonts w:ascii=\"Segoe UI\" w:hAnsi=\"Segoe UI\"/>\n");
            sb.Append("          <w:sz w:val=\"24\"/>\n");
            sb.Append("          <w:color w:val=\"37352F\"/>\n");
            sb.Append("        </w:rPr>\n");
            sb.Append("        <w:t xml:space=\"preserve\">• </w:t>\n");
            sb.Append("      </w:r>\n");

            AppendFormattedRuns(sb, text, fontSizeHalfPts: 24, defaultColorHex: "37352F");

            sb.Append("    </w:p>\n");
            return sb.ToString();
        }

        private static string CreateParagraphXml(
            string text, 
            int fontSizeHalfPts = 24, 
            bool isBold = false, 
            bool isItalic = false, 
            bool isStrikethrough = false, 
            int leftIndentTwips = 0, 
            int spaceBefore = 40, 
            int spaceAfter = 40,
            string colorHex = "37352F",
            bool hasLeftBorder = false)
        {
            var sb = new StringBuilder();
            sb.Append("    <w:p>\n");
            sb.Append("      <w:pPr>\n");
            sb.Append($"        <w:spacing w:before=\"{spaceBefore}\" w:after=\"{spaceAfter}\"/>\n");
            if (leftIndentTwips > 0)
            {
                sb.Append($"        <w:ind w:left=\"{leftIndentTwips}\"/>\n");
            }
            if (hasLeftBorder)
            {
                sb.Append("        <w:pBdr>\n");
                sb.Append("          <w:left w:val=\"single\" w:sz=\"18\" w:space=\"12\" w:color=\"D4D4D0\"/>\n");
                sb.Append("        </w:pBdr>\n");
            }
            sb.Append("      </w:pPr>\n");

            AppendFormattedRuns(sb, text, defaultBold: isBold, defaultItalic: isItalic, defaultStrike: isStrikethrough, fontSizeHalfPts: fontSizeHalfPts, defaultColorHex: colorHex);

            sb.Append("    </w:p>\n");
            return sb.ToString();
        }

        private static void AppendFormattedRuns(
            StringBuilder sb, 
            string text, 
            bool defaultBold = false, 
            bool defaultItalic = false, 
            bool defaultStrike = false, 
            int fontSizeHalfPts = 24, 
            string defaultColorHex = "37352F")
        {
            if (string.IsNullOrEmpty(text))
            {
                sb.Append($"      <w:r><w:rPr><w:sz w:val=\"{fontSizeHalfPts}\"/><w:color w:val=\"{defaultColorHex}\"/></w:rPr><w:t></w:t></w:r>\n");
                return;
            }

            // Parse inline tokens: `code`, **bold**, *italic*, ~~strike~~
            var pattern = @"(`(?<code>[^`]+)`)|(\*\*(?<bold>[^*]+)\*\*)|(__([^_]+)__)|(\*(?<italic>[^*]+)\*)|(_([^_]+)_)|(~~(?<strike>[^~]+)~~)";
            var matches = Regex.Matches(text, pattern);

            int lastIndex = 0;
            foreach (Match m in matches)
            {
                if (m.Index > lastIndex)
                {
                    var plain = text.Substring(lastIndex, m.Index - lastIndex);
                    WriteRun(sb, plain, defaultBold, defaultItalic, defaultStrike, false, fontSizeHalfPts, defaultColorHex);
                }

                if (m.Groups["code"].Success)
                {
                    WriteRun(sb, m.Groups["code"].Value, isBold: false, isItalic: false, isStrike: false, isCode: true, fontSizeHalfPts: fontSizeHalfPts, colorHex: "E03E3E");
                }
                else if (m.Groups["bold"].Success || m.Groups[4].Success)
                {
                    var bText = m.Groups["bold"].Success ? m.Groups["bold"].Value : m.Groups[4].Value;
                    WriteRun(sb, bText, isBold: true, isItalic: defaultItalic, isStrike: defaultStrike, isCode: false, fontSizeHalfPts: fontSizeHalfPts, colorHex: defaultColorHex);
                }
                else if (m.Groups["italic"].Success || m.Groups[6].Success)
                {
                    var iText = m.Groups["italic"].Success ? m.Groups["italic"].Value : m.Groups[6].Value;
                    WriteRun(sb, iText, isBold: defaultBold, isItalic: true, isStrike: defaultStrike, isCode: false, fontSizeHalfPts: fontSizeHalfPts, colorHex: defaultColorHex);
                }
                else if (m.Groups["strike"].Success)
                {
                    WriteRun(sb, m.Groups["strike"].Value, isBold: defaultBold, isItalic: defaultItalic, isStrike: true, isCode: false, fontSizeHalfPts: fontSizeHalfPts, colorHex: "9CA3AF");
                }

                lastIndex = m.Index + m.Length;
            }

            if (lastIndex < text.Length)
            {
                var remaining = text.Substring(lastIndex);
                WriteRun(sb, remaining, defaultBold, defaultItalic, defaultStrike, false, fontSizeHalfPts, defaultColorHex);
            }
        }

        private static void WriteRun(
            StringBuilder sb, 
            string runText, 
            bool isBold, 
            bool isItalic, 
            bool isStrike, 
            bool isCode, 
            int fontSizeHalfPts, 
            string colorHex)
        {
            if (string.IsNullOrEmpty(runText)) return;

            sb.Append("      <w:r>\n");
            sb.Append("        <w:rPr>\n");
            if (isCode)
            {
                sb.Append("          <w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\"/>\n");
                sb.Append("          <w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F3F4F6\"/>\n");
            }
            else
            {
                sb.Append("          <w:rFonts w:ascii=\"Segoe UI\" w:hAnsi=\"Segoe UI\"/>\n");
            }

            sb.Append($"          <w:sz w:val=\"{fontSizeHalfPts}\"/>\n");
            sb.Append($"          <w:color w:val=\"{colorHex}\"/>\n");
            if (isBold) sb.Append("          <w:b/>\n");
            if (isItalic) sb.Append("          <w:i/>\n");
            if (isStrike) sb.Append("          <w:strike/>\n");
            sb.Append("        </w:rPr>\n");
            sb.Append($"        <w:t xml:space=\"preserve\">{XmlEscape(runText)}</w:t>\n");
            sb.Append("      </w:r>\n");
        }

        private static string XmlEscape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("&", "&amp;")
                       .Replace("<", "&lt;")
                       .Replace(">", "&gt;")
                       .Replace("\"", "&quot;")
                       .Replace("'", "&apos;");
        }
    }
}
