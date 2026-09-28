using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace QuickNote.Infrastructure.FileSystem
{
    public class AtomicFileWriter
    {
        public async Task WriteAllTextAsync(string targetFilePath, string content)
        {
            var directory = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempFilePath = targetFilePath + $".{Guid.NewGuid():N}.tmp";

            try
            {
                // Write content to temporary file with flush to disk
                using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    await writer.WriteAsync(content);
                    await writer.FlushAsync();
                }

                // Atomically move/replace target file
                File.Move(tempFilePath, targetFilePath, overwrite: true);
            }
            catch
            {
                // Clean up temp file on failure
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
                throw;
            }
        }
    }
}
