using System;
using System.IO;
using System.Linq;
using System.Text;

namespace QuickNote.Core.Utilities
{
    public static class FileNameSanitizer
    {
        public static string Sanitize(string name, string fallback = "Untitled")
        {
            if (string.IsNullOrWhiteSpace(name))
                return fallback;

            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var ch in name)
            {
                if (!invalidChars.Contains(ch))
                {
                    sb.Append(ch);
                }
                else
                {
                    sb.Append(' ');
                }
            }

            var sanitized = sb.ToString().Trim();
            while (sanitized.Contains("  "))
            {
                sanitized = sanitized.Replace("  ", " ");
            }

            return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
        }
    }
}
