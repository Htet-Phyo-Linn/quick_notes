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
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Error parsing web message: {ex.Message}");
            }
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
                Dispatcher.UIThread.Post(() => SendMarkdownToEditor(targetMarkdown),
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

        private async void SendMarkdownToEditor(string markdown)
        {
            var json = JsonSerializer.Serialize(markdown ?? string.Empty);
            System.Diagnostics.Debug.WriteLine($"[NotionEditorControl] Sending markdown len={json.Length}");
            try
            {
                var result = await WebViewControl.InvokeScript(
                    $"(function(){{if(window.setMarkdown){{window.setMarkdown({json});return 'ok';}}else{{window.__pendingMarkdown={json};return 'pending';}}}})()");
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
    }
}
