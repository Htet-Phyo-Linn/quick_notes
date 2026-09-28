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
                var imagePath = src;
                if (!Path.IsPathRooted(imagePath) && !string.IsNullOrWhiteSpace(notesRoot))
                {
                    imagePath = Path.Combine(notesRoot, imagePath);
                }

                if (!File.Exists(imagePath) && !string.IsNullOrWhiteSpace(notesRoot))
                {
                    if (src.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) || src.StartsWith("assets\\", StringComparison.OrdinalIgnoreCase))
                    {
                        var altPath = Path.Combine(notesRoot, ".assets", src.Substring(7));
                        if (File.Exists(altPath)) imagePath = altPath;
                    }
                    else if (src.StartsWith(".assets/", StringComparison.OrdinalIgnoreCase) || src.StartsWith(".assets\\", StringComparison.OrdinalIgnoreCase))
                    {
                        var altPath = Path.Combine(notesRoot, "assets", src.Substring(8));
                        if (File.Exists(altPath)) imagePath = altPath;
                    }
                }

                BitmapImage? bitmap = null;
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
