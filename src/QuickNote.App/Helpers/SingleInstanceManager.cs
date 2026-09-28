using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace QuickNote.App.Helpers
{
    public class SingleInstanceManager : IDisposable
    {
        private const string MutexName = "QuickNote_App_SingleInstance_Mutex";
        private const string PipeName = "QuickNote_App_NamedPipe";

        private Mutex? _mutex;
        private bool _hasHandle;
        private CancellationTokenSource? _cts;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public bool TryAcquireMutex()
        {
            try
            {
                _mutex = new Mutex(true, MutexName, out _hasHandle);
                return _hasHandle;
            }
            catch (AbandonedMutexException)
            {
                _hasHandle = true;
                return true;
            }
            catch
            {
                _hasHandle = false;
                return false;
            }
        }

        public static async Task NotifyExistingInstanceAsync(string[] args)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                await client.ConnectAsync(1500);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                var json = JsonSerializer.Serialize(args ?? Array.Empty<string>());
                await writer.WriteLineAsync(json);
            }
            catch
            {
                // Ignore if unable to notify
            }
        }

        public void StartServer(Action<string[]> onArgumentsReceived)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            PipeName, 
                            PipeDirection.In, 
                            1, 
                            PipeTransmissionMode.Byte, 
                            PipeOptions.Asynchronous);

                        await server.WaitForConnectionAsync(token);

                        using var reader = new StreamReader(server);
                        var line = await reader.ReadLineAsync(token);
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            try
                            {
                                var args = JsonSerializer.Deserialize<string[]>(line) ?? Array.Empty<string>();
                                onArgumentsReceived(args);
                            }
                            catch
                            {
                                onArgumentsReceived(Array.Empty<string>());
                            }
                        }
                        else
                        {
                            onArgumentsReceived(Array.Empty<string>());
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // Brief delay on pipe error before re-listening
                        await Task.Delay(500, token);
                    }
                }
            }, token);
        }

        public static void ActivateWindow(Window window)
        {
            if (window == null) return;

            window.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }

                    var hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        ShowWindow(hwnd, SW_RESTORE);
                        SetForegroundWindow(hwnd);
                    }

                    window.Show();
                    window.Activate();
                    window.Topmost = true;
                    window.Topmost = false;
                    window.Focus();
                }
                catch { }
            });
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            if (_hasHandle && _mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch { }
                _hasHandle = false;
            }

            _mutex?.Dispose();
        }
    }
}
