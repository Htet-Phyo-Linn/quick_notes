using System;
using System.IO;
using System.Linq;
using System.Text;

namespace QuickNote.Core.Utilities
{
    public static class FileNameSanitizer
    {
        // Standard cross-platform invalid filename characters (covers Windows, Android/Linux, macOS, and FAT32/ext4/NTFS)
        private static readonly char[] CrossPlatformInvalidChars = new[]
        {
            '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0'
        };

        public static string Sanitize(string name, string fallback = "Untitled")
        {
            if (string.IsNullOrWhiteSpace(name))
                return fallback;

            var platformInvalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var ch in name)
            {
                if (char.IsControl(ch) || CrossPlatformInvalidChars.Contains(ch) || platformInvalid.Contains(ch))
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(ch);
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
