using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace QuickNote.Avalonia.Notion
{
    public partial class NotionEditorControl : UserControl
    {
        private bool _isEditorReady;
        private string? _pendingMarkdown;
        private string _currentMarkdown = string.Empty;

        public event Action<string>? MarkdownChanged;
        public event Action<string>? ShortcutTriggered;
        public event Action? EditorFocused;
        public event Action<string>? WordIgnored;

        public bool IsSpellCheckEnabled { get; set; } = false;

        public NotionEditorControl()
        {
            InitializeComponent();

            WebViewControl.EnvironmentRequested += (s, e) =>
            {
                if (e is global::Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs winArgs)
                {
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    winArgs.UserDataFolder = Path.Combine(localAppData, "QuickNote", "WebView2");
                }
            };
            WebViewControl.WebMessageReceived += OnWebMessageReceived;
            WebViewControl.NavigationCompleted += OnNavigationCompleted;
            GotFocus += (s, e) => EditorFocused?.Invoke();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            InitializeEditor();
        }

        private void InitializeEditor()
        {
            var htmlPath = ResolveEditorHtmlPath();
            if (File.Exists(htmlPath))
            {
                var fileUri = new Uri(htmlPath);
                WebViewControl.Navigate(fileUri);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] milkdown index.html not found at: {htmlPath}");
            }
        }

        private static string ResolveEditorHtmlPath()
        {
            // 1. Check in output directory (Assets/milkdown/index.html)
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var path1 = Path.Combine(basePath, "Assets", "milkdown", "index.html");
            if (File.Exists(path1)) return path1;

            // 2. Check development relative path from bin/Debug/net10.0
            var path2 = Path.GetFullPath(Path.Combine(basePath, "..", "..", "..", "..", "QuickNote.Avalonia", "Assets", "milkdown", "index.html"));
            if (File.Exists(path2)) return path2;

            // 3. Check directly in src tree if running from root
            var path3 = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src", "QuickNote.Avalonia", "QuickNote.Avalonia", "Assets", "milkdown", "index.html"));
            if (File.Exists(path3)) return path3;

            return path1;
        }

        private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                System.Diagnostics.Debug.WriteLine("[NotionEditorControl] WebView navigation completed successfully.");
            }
        }

        public string? NotesRoot { get; set; }
        public string? CurrentNoteFolderPath { get; set; }
        public string? CurrentNotePath { get; set; }

        private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Body)) return;

            try
            {
                using var doc = JsonDocument.Parse(e.Body);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var typeProp))
                {
                    var msgType = typeProp.GetString();
                    if (msgType == "ready")
                    {
                        _isEditorReady = true;
                        _ = SetSpellCheckAsync(IsSpellCheckEnabled);
                        if (IgnoredWords != null)
                        {
                            _ = SetIgnoredWordsAsync(IgnoredWords);
                        }
                        // Capture before async dispatch
                        var contentToRestore = _pendingMarkdown ?? _currentMarkdown;
                        _pendingMarkdown = null;
                        if (!string.IsNullOrEmpty(contentToRestore))
                        {
                            // InvokeScript MUST run on UI thread
                            Dispatcher.UIThread.Post(() => SendMarkdownToEditor(contentToRestore),
                                DispatcherPriority.Normal);
                        }
                    }
                    else if (msgType == "contentChanged")
                    {
                        if (root.TryGetProperty("markdown", out var mdProp))
                        {
                            var markdown = mdProp.GetString() ?? string.Empty;
                            // Guard: ignore empty or whitespace-only markdown notification if host has non-empty content
                            if (string.IsNullOrWhiteSpace(markdown) && !string.IsNullOrWhiteSpace(_currentMarkdown))
                            {
                                return;
                            }
                            _currentMarkdown = markdown;
                            Dispatcher.UIThread.Post(() => MarkdownChanged?.Invoke(markdown),
                                DispatcherPriority.Normal);
                        }
                    }
                    else if (msgType == "shortcut")
                    {
                        if (root.TryGetProperty("action", out var actionProp))
                        {
                            var action = actionProp.GetString();
                            if (!string.IsNullOrEmpty(action))
                            {
                                Dispatcher.UIThread.Post(() => ShortcutTriggered?.Invoke(action));
                            }
                        }
                    }
                    else if (msgType == "editorFocused")
                    {
                        Dispatcher.UIThread.Post(() => EditorFocused?.Invoke());
                    }
                    else if (msgType == "openUrl")
                    {
                        if (root.TryGetProperty("url", out var urlProp))
                        {
                            var url = urlProp.GetString();
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                Dispatcher.UIThread.Post(() => OpenUrlInDefaultBrowser(url));
                            }
                        }
                    }
                    else if (msgType == "saveImage")
                    {
                        var reqId = root.TryGetProperty("reqId", out var rProp) ? rProp.GetString() ?? string.Empty : string.Empty;
                        var base64 = root.TryGetProperty("base64", out var bProp) ? bProp.GetString() ?? string.Empty : string.Empty;
                        var suggestedName = root.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                        var altText = root.TryGetProperty("alt", out var aProp) ? aProp.GetString() : "image";
                        Dispatcher.UIThread.Post(() => SaveAndRespondImage(reqId, base64, suggestedName, altText));
                    }
                    else if (msgType == "loadImage")
                    {
                        if (root.TryGetProperty("src", out var srcProp))
                        {
                            var src = srcProp.GetString() ?? string.Empty;
                            Dispatcher.UIThread.Post(() => LoadAndRespondImage(src));
                        }
                    }
                    else if (msgType == "ignoreWord")
                    {
                        if (root.TryGetProperty("word", out var wordProp))
                        {
                            var word = wordProp.GetString();
                            if (!string.IsNullOrWhiteSpace(word))
                            {
                                Dispatcher.UIThread.Post(() => WordIgnored?.Invoke(word.Trim()));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Error parsing web message: {ex.Message}");
            }
        }

        private async void SaveAndRespondImage(string reqId, string base64Data, string? suggestedName, string? altText)
        {
            if (string.IsNullOrWhiteSpace(base64Data)) return;

            try
            {
                var rootDir = NotesRoot;
                if (string.IsNullOrWhiteSpace(rootDir))
                {
                    rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QuickNote");
                }

                var assetsDir = Path.Combine(rootDir, ".assets");
                if (!Directory.Exists(assetsDir))
                {
                    Directory.CreateDirectory(assetsDir);
                }

                string ext = ".png";
                string cleanBase64 = base64Data;
                if (base64Data.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var headerEnd = base64Data.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
                    if (headerEnd > 0)
                    {
                        var mime = base64Data.Substring(11, headerEnd - 11).ToLowerInvariant();
                        ext = mime switch
                        {
                            "jpeg" or "jpg" => ".jpg",
                            "gif" => ".gif",
                            "webp" => ".webp",
                            "svg+xml" => ".svg",
                            _ => ".png"
                        };
                        cleanBase64 = base64Data.Substring(headerEnd + 8);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(suggestedName))
                {
                    var sExt = Path.GetExtension(suggestedName);
                    if (!string.IsNullOrWhiteSpace(sExt)) ext = sExt.ToLowerInvariant();
                }

                var imageBytes = Convert.FromBase64String(cleanBase64);
                var shortId = Guid.NewGuid().ToString("N").Substring(0, 6);
                var fileName = $"pasted_image_{DateTime.Now:yyyyMMdd_HHmmss}_{shortId}{ext}";
                var fullPath = Path.Combine(assetsDir, fileName);
                await File.WriteAllBytesAsync(fullPath, imageBytes);

                var relPath = ".assets/" + fileName;
                var jsonReqId = JsonSerializer.Serialize(reqId);
                var jsonRelPath = JsonSerializer.Serialize(relPath);
                var jsonBase64 = JsonSerializer.Serialize(base64Data);
                var jsonAlt = JsonSerializer.Serialize(altText ?? "image");

                await WebViewControl.InvokeScript(
                    $"if(window.onImageSaved){{ window.onImageSaved({jsonReqId}, {jsonRelPath}, {jsonBase64}, {jsonAlt}); }}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Failed to save image: {ex.Message}");
            }
        }

        private async void LoadAndRespondImage(string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return;

            try
            {
                string? resolvedPath = ResolveImageFilePath(src, CurrentNoteFolderPath, NotesRoot);

                if (resolvedPath != null && File.Exists(resolvedPath))
                {
                    var bytes = await File.ReadAllBytesAsync(resolvedPath);
                    var ext = Path.GetExtension(resolvedPath).TrimStart('.').ToLowerInvariant();
                    var mime = ext switch
                    {
                        "jpg" or "jpeg" => "image/jpeg",
                        "gif" => "image/gif",
                        "webp" => "image/webp",
                        "svg" => "image/svg+xml",
                        _ => "image/png"
                    };
                    var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                    var jsonSrc = JsonSerializer.Serialize(src);
                    var jsonDataUrl = JsonSerializer.Serialize(dataUrl);

                    await WebViewControl.InvokeScript(
                        $"if(window.onImageLoaded){{ window.onImageLoaded({jsonSrc}, {jsonDataUrl}); }}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Failed to load image {src}: {ex.Message}");
            }
        }

        public static string? ResolveImageFilePath(string src, string? noteFolderPath, string? notesRoot)
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

            // 1. Check in note folder and parent hierarchy
            if (!string.IsNullOrWhiteSpace(noteFolderPath))
            {
                var p = Path.Combine(noteFolderPath, cleanSrc);
                if (File.Exists(p)) return p;

                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var pAssets = Path.Combine(noteFolderPath, ".assets", fileName);
                    if (File.Exists(pAssets)) return pAssets;

                    var pAssets2 = Path.Combine(noteFolderPath, "assets", fileName);
                    if (File.Exists(pAssets2)) return pAssets2;
                }

                // Walk up directories from noteFolderPath to notesRoot or drive root
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

            // 2. Check in notesRoot
            if (!string.IsNullOrWhiteSpace(notesRoot))
            {
                var p = Path.Combine(notesRoot, cleanSrc);
                if (File.Exists(p)) return p;

                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var pDotAssets = Path.Combine(notesRoot, ".assets", fileName);
                    if (File.Exists(pDotAssets)) return pDotAssets;

                    var pAssets = Path.Combine(notesRoot, "assets", fileName);
                    if (File.Exists(pAssets)) return pAssets;
                }
            }

            // 3. Fallback to standard OneDrive / MyDocuments locations
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


        public static void OpenUrlInDefaultBrowser(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                url = url.Trim();
                if (!url.Contains("://", StringComparison.Ordinal) && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
                        url.Contains(".com", StringComparison.OrdinalIgnoreCase) ||
                        url.Contains(".org", StringComparison.OrdinalIgnoreCase) ||
                        url.Contains(".net", StringComparison.OrdinalIgnoreCase) ||
                        url.Contains(".io", StringComparison.OrdinalIgnoreCase) ||
                        url.Contains(".dev", StringComparison.OrdinalIgnoreCase))
                    {
                        url = "https://" + url;
                    }
                }

                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = uri.AbsoluteUri,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                else if (Uri.TryCreate("https://" + url, UriKind.Absolute, out var fallbackUri))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fallbackUri.AbsoluteUri,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Failed to open URL '{url}': {ex.Message}");
            }
        }

        public void LoadMarkdown(string markdown, bool force = false)
        {
            markdown ??= string.Empty;
            bool contentChanged = !string.Equals(_currentMarkdown, markdown, StringComparison.Ordinal);
            if (!force && !contentChanged) return;
            _currentMarkdown = markdown;
            if (_isEditorReady)
            {
                _pendingMarkdown = null;
                var targetMarkdown = _currentMarkdown;
                // Always dispatch to UI thread — InvokeScript requires it
                Dispatcher.UIThread.Post(() => SendMarkdownToEditor(targetMarkdown, force),
                    DispatcherPriority.Normal);
            }
            else
            {
                _pendingMarkdown = markdown;
            }
        }

        public string GetMarkdown()
        {
            return _currentMarkdown;
        }

        public async Task FlushPendingChangesAsync()
        {
            if (WebViewControl != null && _isEditorReady)
            {
                try
                {
                    var md = await WebViewControl.InvokeScript(
                        "(function(){ if(window.flushContentChanged){ window.flushContentChanged(); } return window.getMarkdown ? window.getMarkdown() : ''; })()");
                    if (!string.IsNullOrEmpty(md))
                    {
                        var unquoted = md;
                        if (unquoted.StartsWith("\"") && unquoted.EndsWith("\"") && unquoted.Length >= 2)
                        {
                            try { unquoted = JsonSerializer.Deserialize<string>(unquoted) ?? unquoted; } catch { }
                        }
                        _currentMarkdown = unquoted;
                        MarkdownChanged?.Invoke(unquoted);
                    }
                }
                catch { }
            }
        }

        private async void SendMarkdownToEditor(string markdown, bool force = false)
        {
            var json = JsonSerializer.Serialize(markdown ?? string.Empty);
            var forceArg = force ? "true" : "false";
            System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Sending markdown len={json.Length} force={force}");
            try
            {
                var result = await WebViewControl.InvokeScript(
                    $"(function(){{if(window.setMarkdown){{window.setMarkdown({json}, {forceArg});return 'ok';}}else{{window.__pendingMarkdown={json};return 'pending';}}}})()");
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] InvokeScript result={result}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] InvokeScript failed: {ex.Message}");
            }
        }

        public async Task ScrollToHeadingAsync(string headingText, int level = 0)
        {
            if (string.IsNullOrWhiteSpace(headingText)) return;
            var jsonText = JsonSerializer.Serialize(headingText);
            try
            {
                await WebViewControl.InvokeScript($"if (window.scrollToHeading) {{ window.scrollToHeading({jsonText}, {level}); }}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Failed to scroll to heading: {ex.Message}");
            }
        }

        public async Task<int> FindInPageAsync(string query, bool backwards = false)
        {
            if (WebViewControl == null || !_isEditorReady) return 0;
            var jsonQuery = JsonSerializer.Serialize(query ?? string.Empty);
            var backwardsArg = backwards ? "true" : "false";
            try
            {
                var resultJson = await WebViewControl.InvokeScript(
                    $"(function(){{ if(window.findInPage) {{ return JSON.stringify(window.findInPage({jsonQuery}, {backwardsArg})); }} return JSON.stringify({{count:0, found:false}}); }})()");
                if (!string.IsNullOrEmpty(resultJson))
                {
                    using var doc = JsonDocument.Parse(resultJson);
                    if (doc.RootElement.TryGetProperty("count", out var countProp))
                    {
                        return countProp.GetInt32();
                    }
                }
            }
            catch { }
            return 0;
        }

        public async Task ClearFindHighlightAsync()
        {
            if (WebViewControl == null || !_isEditorReady) return;
            try
            {
                await WebViewControl.InvokeScript("if(window.clearFindHighlight){ window.clearFindHighlight(); }");
            }
            catch { }
        }

        public async Task OpenFindWidgetAsync(string? initialQuery = null)
        {
            if (WebViewControl == null || !_isEditorReady) return;
            try
            {
                var queryParam = initialQuery != null ? JsonSerializer.Serialize(initialQuery) : "null";
                await WebViewControl.InvokeScript($"if (window.openFindWidget) {{ window.openFindWidget({queryParam}); }}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Failed to open find widget: {ex.Message}");
            }
        }

        public async Task CloseFindWidgetAsync()
        {
            if (WebViewControl == null || !_isEditorReady) return;
            try
            {
                await WebViewControl.InvokeScript("if (window.closeFindWidget) { window.closeFindWidget(); }");
            }
            catch { }
        }

        public async Task PrintOrExportPdfAsync()
        {
            try
            {
                await WebViewControl.InvokeScript("window.print();");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Failed to invoke window.print(): {ex.Message}");
            }
        }

        public async Task SetSpellCheckAsync(bool enabled)
        {
            IsSpellCheckEnabled = enabled;
            try
            {
                await WebViewControl.InvokeScript($"if (window.setSpellCheck) {{ window.setSpellCheck({(enabled ? "true" : "false")}); }}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Failed to set spellcheck: {ex.Message}");
            }
        }

        public System.Collections.Generic.IEnumerable<string>? IgnoredWords { get; set; }

        public async Task SetIgnoredWordsAsync(System.Collections.Generic.IEnumerable<string>? words)
        {
            IgnoredWords = words;
            if (!_isEditorReady || words == null) return;
            try
            {
                var json = JsonSerializer.Serialize(words);
                await WebViewControl.InvokeScript($"if (window.setIgnoredWords) {{ window.setIgnoredWords({json}); }}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Failed to set ignored words: {ex.Message}");
            }
        }
    }
}
