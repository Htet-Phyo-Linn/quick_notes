using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Core.Interfaces;

namespace QuickNote.Avalonia.Views
{
    public partial class GoogleDriveLoginWindow : Window
    {
        private readonly ICloudSyncService? _cloudSyncService;
        private readonly MainViewModel? _viewModel;
        private string _codeVerifier = string.Empty;
        private HttpListener? _httpListener;
        private bool _isCompleted = false;

        public GoogleDriveLoginWindow()
        {
            InitializeComponent();
        }

        public GoogleDriveLoginWindow(ICloudSyncService? cloudSyncService, MainViewModel? viewModel = null) : this()
        {
            _cloudSyncService = cloudSyncService;
            _viewModel = viewModel;
            Closed += (s, e) => StopListener();
        }

        private void OpenBrowser_Click(object? sender, RoutedEventArgs e)
        {
            if (_cloudSyncService == null) return;
            try
            {
                var url = _cloudSyncService.GetAuthorizationUrl(out _codeVerifier);
                if (string.IsNullOrWhiteSpace(url))
                {
                    StatusTextBlock.Text = "Failed to generate authorization URL.";
                    StatusTextBlock.Foreground = Brush.Parse("#ef4444");
                    return;
                }

                StartLoopbackListener();

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                StatusTextBlock.Text = "Browser opened. Sign in and grant permissions...";
                StatusTextBlock.Foreground = Brush.Parse("#38bdf8");
                BtnOpenBrowser.IsEnabled = false;
                BtnOpenBrowser.Opacity = 0.7;

                // Re-enable after 3 seconds in case user needs to reopen
                Task.Delay(3000).ContinueWith(_ =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!_isCompleted)
                        {
                            BtnOpenBrowser.IsEnabled = true;
                            BtnOpenBrowser.Opacity = 1.0;
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error opening browser: {ex.Message}";
                StatusTextBlock.Foreground = Brush.Parse("#ef4444");
            }
        }

        private void StartLoopbackListener()
        {
            try
            {
                StopListener();
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add("http://localhost:80/");
                _httpListener.Start();

                Task.Run(async () =>
                {
                    try
                    {
                        if (_httpListener == null || !_httpListener.IsListening) return;
                        var context = await _httpListener.GetContextAsync();
                        var reqUrl = context.Request.Url?.ToString() ?? string.Empty;
                        var code = ExtractCode(reqUrl);

                        var html = @"<!DOCTYPE html><html><head><meta charset='utf-8'><title>QuickNote Auth</title></head><body style='font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,sans-serif;text-align:center;padding:60px;background:#18181e;color:#f8fafc;'>
                            <h2 style='color:#34d399;'>Sign-In Successful!</h2>
                            <p style='color:#94a3b8;font-size:15px;'>QuickNote has received authorization from Google Drive.</p>
                            <p style='color:#64748b;font-size:13px;'>You may now close this browser tab and return to the QuickNote app.</p>
                        </body></html>";
                        var buffer = Encoding.UTF8.GetBytes(html);
                        context.Response.ContentLength64 = buffer.Length;
                        context.Response.ContentType = "text/html; charset=utf-8";
                        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        context.Response.OutputStream.Close();

                        if (!string.IsNullOrEmpty(code))
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                AuthCodeInputBox.Text = code;
                                _ = CompleteSignInWithCodeAsync(code);
                            });
                        }
                    }
                    catch { }
                    finally
                    {
                        StopListener();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GoogleDriveLoginWindow] Loopback listener unavailable: {ex.Message}. User can paste redirect URL or code.");
            }
        }

        private void StopListener()
        {
            try
            {
                if (_httpListener != null && _httpListener.IsListening)
                {
                    _httpListener.Stop();
                    _httpListener.Close();
                }
            }
            catch { }
            finally
            {
                _httpListener = null;
            }
        }

        private async void CompleteSignIn_Click(object? sender, RoutedEventArgs e)
        {
            await CompleteSignInWithCodeAsync(AuthCodeInputBox.Text);
        }

        private async void AuthCodeInputBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await CompleteSignInWithCodeAsync(AuthCodeInputBox.Text);
            }
        }

        private async Task CompleteSignInWithCodeAsync(string? rawInput)
        {
            if (_cloudSyncService == null) return;
            var code = ExtractCode(rawInput);
            if (string.IsNullOrWhiteSpace(code))
            {
                StatusTextBlock.Text = "Please paste the authorization code or full redirect URL.";
                StatusTextBlock.Foreground = Brush.Parse("#ef4444");
                return;
            }

            if (string.IsNullOrWhiteSpace(_codeVerifier))
            {
                // Generate a fresh URL to capture verifier if user directly pasted code
                _ = _cloudSyncService.GetAuthorizationUrl(out _codeVerifier);
            }

            try
            {
                LoginProgressBar.IsVisible = true;
                StatusTextBlock.Text = "Exchanging code with Google Drive...";
                StatusTextBlock.Foreground = Brush.Parse("#38bdf8");
                BtnComplete.IsEnabled = false;

                var success = await _cloudSyncService.CompleteSignInAsync(code, _codeVerifier);
                if (success)
                {
                    _isCompleted = true;
                    StopListener();

                    StatusTextBlock.Text = $"Signed in successfully as {_cloudSyncService.UserEmail ?? "Google Drive"}!";
                    StatusTextBlock.Foreground = Brush.Parse("#10b981");

                    // Trigger cloud sync and refresh UI
                    _ = _cloudSyncService.TriggerSyncAsync();

                    if (_viewModel != null)
                    {
                        await _viewModel.RefreshFolderTreeAsync();
                        await _viewModel.Refresh();
                        _viewModel.StatusText = $"Connected to Google Drive ({_cloudSyncService.UserEmail})";
                        _viewModel.ShowProfileDialog(); // Refresh profile dialog list
                    }

                    await Task.Delay(1200);
                    Close(true);
                }
                else
                {
                    StatusTextBlock.Text = "Authentication failed. Please verify the code or try again.";
                    StatusTextBlock.Foreground = Brush.Parse("#ef4444");
                    BtnComplete.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Authentication error: {ex.Message}";
                StatusTextBlock.Foreground = Brush.Parse("#ef4444");
                BtnComplete.IsEnabled = true;
            }
            finally
            {
                LoginProgressBar.IsVisible = false;
            }
        }

        private static string ExtractCode(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            input = input.Trim();

            if (input.Contains("code="))
            {
                var qIndex = input.IndexOf("code=", StringComparison.OrdinalIgnoreCase);
                var sub = input.Substring(qIndex + 5);
                var ampIndex = sub.IndexOf('&');
                var rawCode = ampIndex >= 0 ? sub.Substring(0, ampIndex) : sub;
                return Uri.UnescapeDataString(rawCode);
            }

            return input;
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            StopListener();
            Close(false);
        }
    }
}