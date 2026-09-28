using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickNote.App.Helpers
{
    public static partial class NotionMarkdownHelper
    {
        public static BlockUIContainer? CreateImageBlock(string src, string alt = "", string? notesRoot = null)
        {
            if (string.IsNullOrWhiteSpace(src)) return null;

            try
            {
                BitmapImage? bitmap = null;

                if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var headerEnd = src.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
                    if (headerEnd > 0)
                    {
                        var cleanB64 = src.Substring(headerEnd + 8);
                        var bytes = Convert.FromBase64String(cleanB64);
                        using var ms = new MemoryStream(bytes);
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }
                }
                else
                {
                    var imagePath = DocxExportHelper.ResolveImagePath(src, null, notesRoot);
                    if (string.IsNullOrWhiteSpace(imagePath)) imagePath = src;

                    if (Uri.TryCreate(imagePath, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        bitmap = new BitmapImage(uri);
                    }
                    else if (File.Exists(imagePath))
                    {
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }
                }

                if (bitmap != null)
                {
                    var img = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        StretchDirection = StretchDirection.Both,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 10, 0, 10),
                        SnapsToDevicePixels = true,
                        Tag = "ResponsiveImage"
                    };

                    if (!string.IsNullOrWhiteSpace(alt))
                    {
                        img.ToolTip = alt;
                    }

                    var container = new BlockUIContainer(img)
                    {
                        Tag = "ImageBlock:" + src,
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    return container;
                }
            }
            catch { }

            return null;
        }
    }
}
