using System;
using System.IO;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace QuickNote.Avalonia.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        public Application(IntPtr handle, JniHandleOwnership transfer)
            : base(handle, transfer)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();
            ExtractEditorAssets();
        }

        private void ExtractEditorAssets()
        {
            try
            {
                var personalDir = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                var targetDir = Path.Combine(personalDir, "Assets", "milkdown");
                Directory.CreateDirectory(targetDir);

                var assetManager = Assets;
                if (assetManager != null)
                {
                    // Check both "Assets/milkdown" and "milkdown" prefixes
                    string[] subDirs = new[] { "Assets/milkdown", "milkdown" };
                    foreach (var subDir in subDirs)
                    {
                        var files = assetManager.List(subDir);
                        if (files != null && files.Length > 0)
                        {
                            foreach (var file in files)
                            {
                                var assetPath = $"{subDir}/{file}";
                                var destPath = Path.Combine(targetDir, file);
                                try
                                {
                                    using var inStream = assetManager.Open(assetPath);
                                    using var outStream = File.Create(destPath);
                                    inStream.CopyTo(outStream);
                                }
                                catch (Exception fileEx)
                                {
                                    global::Android.Util.Log.Warn("QuickNote", $"Failed to copy asset {assetPath}: {fileEx.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("QuickNote", $"Error extracting editor assets: {ex.Message}");
            }
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
