using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Infrastructure.Cloud;

namespace QuickNote.Avalonia.Views
{
    public partial class MobileMainView
    {
        // ── Google Drive Cloud Sync Modals ────────────────────────────────
        private string? _pendingCodeVerifier;

        public void OnCloudSyncBadgeTapped(object? sender, TappedEventArgs e)
        {
            UpdateModalAccountStatus();
            GoogleDriveAccountModal.IsVisible = true;
        }

        private void UpdateModalAccountStatus()
        {
            bool signedIn = IsSignedInToGoogle;
            BtnConnectGoogle.IsVisible = !signedIn;
            BtnSyncNow.IsVisible = signedIn;
            BtnDisconnectGoogle.IsVisible = signedIn;
            OnPropertyChanged(nameof(ConnectedAccountEmail));
            OnPropertyChanged(nameof(LastSyncTimeText));
            OnPropertyChanged(nameof(CloudSyncStatusText));
            OnPropertyChanged(nameof(IsSignedInToGoogle));
            OnPropertyChanged(nameof(CloudSyncIconBrush));
        }

        private void OnCloseAccountModalClick(object? sender, RoutedEventArgs e)
        {
            GoogleDriveAccountModal.IsVisible = false;
        }

        private void OnConnectGoogleClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.CloudSyncService == null) return;

            GoogleDriveAccountModal.IsVisible = false;
            var url = _viewModel.CloudSyncService.GetAuthorizationUrl(out _pendingCodeVerifier);
            if (!string.IsNullOrEmpty(url))
            {
                GoogleDriveAuthModal.IsVisible = true;
                AuthWebView.Navigate(new Uri(url));
            }
        }

        private void OnCloseAuthModalClick(object? sender, RoutedEventArgs e)
        {
            GoogleDriveAuthModal.IsVisible = false;
        }

        private void OnAuthWebViewNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
        {
            try
            {
                if (e.Request != null)
                {
                    var url = e.Request.ToString();
                    Console.WriteLine($"[MobileMainView] Auth NavigationStarting: {url}");
                    if (url.Contains("code=") || url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
                    {
                        var code = ExtractQueryParam(url, "code");
                        if (!string.IsNullOrEmpty(code))
                        {
                            Console.WriteLine($"[MobileMainView] Intercepted OAuth code in NavigationStarting: {code.Substring(0, Math.Min(code.Length, 10))}...");
                            e.Cancel = true;
                            GoogleDriveAuthModal.IsVisible = false;
                            _ = HandleAuthCodeAsync(code);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Auth navigation starting error: {ex.Message}");
            }
        }

        private async void OnAuthWebViewNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
        {
            try
            {
                if (e.Request != null)
                {
                    var url = e.Request.ToString();
                    Console.WriteLine($"[MobileMainView] Auth NavigationCompleted: {url} (Success: {e.IsSuccess})");
                    if (url.Contains("code="))
                    {
                        var code = ExtractQueryParam(url, "code");
                        if (!string.IsNullOrEmpty(code))
                        {
                            Console.WriteLine($"[MobileMainView] Intercepted OAuth code in NavigationCompleted: {code.Substring(0, Math.Min(code.Length, 10))}...");
                            GoogleDriveAuthModal.IsVisible = false;
                            _ = HandleAuthCodeAsync(code);
                            return;
                        }
                    }
                }

                var href = await AuthWebView.InvokeScript("window.location.href");
                if (!string.IsNullOrEmpty(href))
                {
                    Console.WriteLine($"[MobileMainView] Auth WebView window.location.href: {href}");
                    if (href.Contains("code="))
                    {
                        var code = ExtractQueryParam(href, "code");
                        if (!string.IsNullOrEmpty(code))
                        {
                            Console.WriteLine($"[MobileMainView] Intercepted OAuth code from JS href: {code.Substring(0, Math.Min(code.Length, 10))}...");
                            GoogleDriveAuthModal.IsVisible = false;
                            _ = HandleAuthCodeAsync(code);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] Auth navigation completed error: {ex.Message}");
            }
        }

        private async Task HandleAuthCodeAsync(string code)
        {
            try
            {
                Console.WriteLine($"[MobileMainView] HandleAuthCodeAsync starting...");
                ShowToast("Signing in & synchronizing notes...", isProgress: true);
                var verifier = _pendingCodeVerifier;
                _pendingCodeVerifier = null;

                if (!string.IsNullOrEmpty(verifier) && _viewModel?.CloudSyncService != null)
                {
                    var success = await _viewModel.CloudSyncService.CompleteSignInAsync(code, verifier);
                    Console.WriteLine($"[MobileMainView] CompleteSignInAsync result: {success}");
                    await _viewModel.RefreshFolderTreeAsync();
                    await _viewModel.Refresh();
                    UpdateModalAccountStatus();
                    RebuildTagChips();
                    RebuildDrawerFolders();
                    RebuildDrawerTags();
                    ApplyFilters();
                    ShowToast(success ? "Signed in & sync completed!" : "Failed to sign in.", isProgress: false, isError: !success);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileMainView] HandleAuthCodeAsync error: {ex.Message}\n{ex.StackTrace}");
                ShowToast($"Sign in error: {ex.Message}", isProgress: false, isError: true);
            }
        }

        private bool _isLocalSyncInProgress = false;

        private async void OnSyncNowClick(object? sender, RoutedEventArgs e)
        {
            if (_isLocalSyncInProgress || _viewModel?.CloudSyncService?.IsSyncing == true)
            {
                ShowToast("Sync is already in progress. Please wait...", isProgress: true, autoDismissMs: 2000);
                return;
            }

            if (_viewModel?.CloudSyncService != null)
            {
                _isLocalSyncInProgress = true;
                Console.WriteLine($"[MobileMainView] OnSyncNowClick triggered");
                if (SyncProgressContainer != null) SyncProgressContainer.IsVisible = true;
                if (BtnSyncNow != null)
                {
                    BtnSyncNow.IsEnabled = false;
                    BtnSyncNow.Content = "Syncing...";
                }
                if (SyncPhaseLabel != null) SyncPhaseLabel.Text = "Starting synchronization...";
                ShowToast("Syncing with Google Drive...", isProgress: true);
                try
                {
                    var result = await _viewModel.CloudSyncService.TriggerSyncAsync();
                    Console.WriteLine($"[MobileMainView] TriggerSyncAsync result: {result.Message}");
                    await _viewModel.RefreshFolderTreeAsync();
                    await _viewModel.Refresh();
                    UpdateModalAccountStatus();
                    RebuildTagChips();
                    RebuildDrawerFolders();
                    RebuildDrawerTags();
                    ApplyFilters();
                    ShowToast(result.Success ? "Sync completed successfully" : $"Sync warning: {result.Message}", isProgress: false, isError: !result.Success);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MobileMainView] OnSyncNowClick error: {ex.Message}");
                    ShowToast($"Sync error: {ex.Message}", isProgress: false, isError: true);
                }
                finally
                {
                    _isLocalSyncInProgress = false;
                    if (SyncProgressContainer != null) SyncProgressContainer.IsVisible = false;
                    if (BtnSyncNow != null)
                    {
                        BtnSyncNow.IsEnabled = true;
                        BtnSyncNow.Content = "Sync Now";
                    }
                }
            }
        }

        private async void OnSyncClick(object? sender, RoutedEventArgs e)
        {
            if (_isLocalSyncInProgress || _viewModel?.CloudSyncService?.IsSyncing == true)
            {
                ShowToast("Sync is already in progress. Please wait...", isProgress: true, autoDismissMs: 2000);
                return;
            }

            if (_viewModel != null)
            {
                _isLocalSyncInProgress = true;
                ShowToast("Syncing with Google Drive...", isProgress: true);
                bool success = true;
                string msg = "Sync completed successfully";
                try
                {
                    if (_viewModel.CloudSyncService != null)
                    {
                        var result = await _viewModel.CloudSyncService.TriggerSyncAsync();
                        success = result.Success;
                        msg = result.Success ? "Sync completed successfully" : $"Sync: {result.Message}";
                    }
                    await _viewModel.RefreshFolderTreeAsync();
                    await _viewModel.Refresh();
                    RebuildTagChips();
                    RebuildDrawerFolders();
                    RebuildDrawerTags();
                    ApplyFilters();
                    UpdateModalAccountStatus();
                }
                catch (Exception ex)
                {
                    success = false;
                    msg = $"Sync error: {ex.Message}";
                }
                finally
                {
                    _isLocalSyncInProgress = false;
                }
                ShowToast(msg, isProgress: false, isError: !success);
            }
        }

        // ── Real-time Sync Progress, Logs & Toast Notifications ───────────
        private CancellationTokenSource? _toastCts;
        private readonly RotateTransform _spinnerTransform = new RotateTransform();
        private DispatcherTimer? _spinnerTimer;

        private void StartSpinnerAnimation()
        {
            _spinnerTimer?.Stop();
            _spinnerTransform.Angle = 0;
            if (ToastSpinnerIcon != null)
            {
                ToastSpinnerIcon.RenderTransform = _spinnerTransform;
            }
            _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            _spinnerTimer.Tick += (s, e) =>
            {
                _spinnerTransform.Angle = (_spinnerTransform.Angle + 12) % 360;
            };
            _spinnerTimer.Start();
        }

        private void StopSpinnerAnimation()
        {
            _spinnerTimer?.Stop();
            _spinnerTimer = null;
        }

        public void ShowToast(string message, bool isProgress = false, bool isError = false, int autoDismissMs = 2500)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _toastCts?.Cancel();
                _toastCts = new CancellationTokenSource();
                var token = _toastCts.Token;

                if (ToastCapsule == null) return;
                if (ToastMessageText != null) ToastMessageText.Text = message;
                if (TopSyncProgressBar != null) TopSyncProgressBar.IsVisible = isProgress;

                if (isProgress)
                {
                    if (ToastSpinnerContainer != null) ToastSpinnerContainer.IsVisible = true;
                    if (ToastIconContainer != null) ToastIconContainer.IsVisible = false;
                    StartSpinnerAnimation();
                    ToastCapsule.BorderBrush = AccentBrush;
                }
                else
                {
                    StopSpinnerAnimation();
                    if (ToastSpinnerContainer != null) ToastSpinnerContainer.IsVisible = false;
                    if (ToastIconContainer != null) ToastIconContainer.IsVisible = true;
                    if (ToastIcon != null)
                    {
                        var iconColor = isError ? Color.Parse("#ef4444") : Color.Parse("#10b981");
                        ToastIcon.Foreground = new SolidColorBrush(iconColor);
                        if (Resources.TryGetResource(isError ? "IconClose" : "IconCheck", null, out var geomObj) && geomObj is StreamGeometry geom)
                        {
                            ToastIcon.Data = geom;
                        }
                        if (ToastIconContainer != null)
                        {
                            ToastIconContainer.Background = new SolidColorBrush(Color.FromArgb(32, iconColor.R, iconColor.G, iconColor.B));
                        }
                        ToastCapsule.BorderBrush = new SolidColorBrush(iconColor);
                    }
                }

                ToastCapsule.IsVisible = true;

                if (!isProgress)
                {
                    Task.Delay(autoDismissMs, token).ContinueWith(t =>
                    {
                        if (!t.IsCanceled)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (ToastCapsule != null) ToastCapsule.IsVisible = false;
                            });
                        }
                    });
                }
                else
                {
                    // Watchdog: progress toast automatically times out after 30 seconds to prevent getting stuck permanently
                    Task.Delay(30000, token).ContinueWith(t =>
                    {
                        if (!t.IsCanceled)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (ToastCapsule != null && ToastSpinnerContainer?.IsVisible == true)
                                {
                                    HideToast();
                                }
                            });
                        }
                    });
                }
            });
        }

        public void HideToast()
        {
            Dispatcher.UIThread.Post(() =>
            {
                _toastCts?.Cancel();
                StopSpinnerAnimation();
                if (ToastCapsule != null) ToastCapsule.IsVisible = false;
                if (TopSyncProgressBar != null) TopSyncProgressBar.IsVisible = false;
            });
        }

        private void OnCloseToastClick(object? sender, RoutedEventArgs e)
        {
            HideToast();
        }

        private void OnToggleSyncLogsClick(object? sender, RoutedEventArgs e)
        {
            if (SyncLogsContainer == null) return;
            bool isVisible = !SyncLogsContainer.IsVisible;
            SyncLogsContainer.IsVisible = isVisible;
            if (SyncLogsChevron != null && Resources.TryGetValue(isVisible ? "IconChevronUp" : "IconChevronDown", out var iconGeo))
            {
                SyncLogsChevron.Data = (StreamGeometry)iconGeo!;
            }
            if (isVisible && SyncLogsText != null)
            {
                SyncLogsText.Text = SyncLogger.GetFormattedLogs();
            }
        }

        private void OnSyncLogAdded(string log)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (SyncLogsText != null && SyncLogsContainer?.IsVisible == true)
                {
                    SyncLogsText.Text = SyncLogger.GetFormattedLogs();
                }
            });
        }

        private void OnCloudSyncStatusChanged(object? sender, CloudSyncStatus status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (status.State == CloudSyncState.Syncing)
                {
                    if (TopSyncProgressBar != null) TopSyncProgressBar.IsVisible = true;
                    if (SyncProgressContainer != null) SyncProgressContainer.IsVisible = true;
                    if (SyncPhaseLabel != null) SyncPhaseLabel.Text = status.Message;
                    if (SyncPercentLabel != null) SyncPercentLabel.Text = "Syncing...";
                    if (ToastCapsule != null && ToastCapsule.IsVisible && ToastSpinnerContainer?.IsVisible == true)
                    {
                        if (ToastMessageText != null) ToastMessageText.Text = status.Message;
                    }
                }
                else if (status.State == CloudSyncState.Synced)
                {
                    if (TopSyncProgressBar != null) TopSyncProgressBar.IsVisible = false;
                    if (SyncProgressContainer != null) SyncProgressContainer.IsVisible = false;
                    OnPropertyChanged(nameof(LastSyncTimeText));
                    OnPropertyChanged(nameof(CloudSyncStatusText));
                    OnPropertyChanged(nameof(CloudSyncIconBrush));
                    if (ToastCapsule != null && ToastCapsule.IsVisible)
                    {
                        ShowToast("Google Drive synced", isProgress: false, isError: false, autoDismissMs: 2000);
                    }
                }
                else if (status.State == CloudSyncState.Error)
                {
                    if (TopSyncProgressBar != null) TopSyncProgressBar.IsVisible = false;
                    if (SyncProgressContainer != null) SyncProgressContainer.IsVisible = false;
                    OnPropertyChanged(nameof(LastSyncTimeText));
                    OnPropertyChanged(nameof(CloudSyncStatusText));
                    OnPropertyChanged(nameof(CloudSyncIconBrush));
                    if (ToastCapsule != null && ToastCapsule.IsVisible)
                    {
                        ShowToast($"Sync error: {status.Message}", isProgress: false, isError: true, autoDismissMs: 4000);
                    }
                }
            });
        }

        private async void OnDisconnectGoogleClick(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.CloudSyncService != null)
            {
                await _viewModel.CloudSyncService.SignOutAsync();
                UpdateModalAccountStatus();
                ApplyFilters();
            }
        }

        private static string? ExtractQueryParam(string url, string paramName)
        {
            try
            {
                var qIndex = url.IndexOf('?');
                if (qIndex < 0) return null;
                var query = url.Substring(qIndex + 1);
                var hashIndex = query.IndexOf('#');
                if (hashIndex >= 0) query = query.Substring(0, hashIndex);
                var pairs = query.Split('&');
                foreach (var p in pairs)
                {
                    var parts = p.Split(new[] { '=' }, 2);
                    if (parts.Length == 2 && string.Equals(parts[0], paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        return Uri.UnescapeDataString(parts[1]);
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
