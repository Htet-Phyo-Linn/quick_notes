using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QuickNote.App.DependencyInjection;
using QuickNote.App.Helpers;
using QuickNote.App.ViewModels;
using QuickNote.Core.Interfaces;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.App
{
    public partial class App : Application
    {
        public IServiceProvider? ServiceProvider { get; private set; }

        private SingleInstanceManager? _singleInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (sender, args) =>
            {
                try
                {
                    LogCrash("DispatcherUnhandledException", args.Exception);
                    System.Diagnostics.Debug.WriteLine($"[QuickNote] Handled UI Exception: {args.Exception}");
                }
                catch { }
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                try
                {
                    LogCrash("AppDomain UnhandledException", args.ExceptionObject as Exception);
                }
                catch { }
            };

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                try
                {
                    LogCrash("TaskScheduler UnobservedTaskException", args.Exception);
                }
                catch { }
                args.SetObserved();
            };

            _singleInstance = new SingleInstanceManager();

            if (!_singleInstance.TryAcquireMutex())
            {
                // Another instance is already running — notify it and exit
                var args = e.Args;
                Task.Run(async () => await SingleInstanceManager.NotifyExistingInstanceAsync(args)).Wait(2000);
                Shutdown(0);
                return;
            }

            try
            {
                var services = new ServiceCollection();

                string defaultNotesRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "QuickNote");

                if (!Directory.Exists(defaultNotesRoot))
                    Directory.CreateDirectory(defaultNotesRoot);

                string notesRoot = defaultNotesRoot;

                // Check AppData settings first for persistent OneDrive configuration
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "QuickNote");
                string globalSettingsFile = Path.Combine(appDataDir, "settings.json");

                if (File.Exists(globalSettingsFile))
                {
                    try
                    {
                        var json = File.ReadAllText(globalSettingsFile);
                        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                        if (settings != null && settings.IsOneDriveSyncEnabled &&
                            !string.IsNullOrWhiteSpace(settings.OneDriveNotesPath) &&
                            Directory.Exists(settings.OneDriveNotesPath))
                        {
                            notesRoot = settings.OneDriveNotesPath;
                        }
                    }
                    catch { }
                }
                else
                {
                    var legacySettingsFile = Path.Combine(defaultNotesRoot, ".settings.json");
                    if (File.Exists(legacySettingsFile))
                    {
                        try
                        {
                            var json = File.ReadAllText(legacySettingsFile);
                            var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                            if (settings != null && settings.IsOneDriveSyncEnabled &&
                                !string.IsNullOrWhiteSpace(settings.OneDriveNotesPath) &&
                                Directory.Exists(settings.OneDriveNotesPath))
                            {
                                notesRoot = settings.OneDriveNotesPath;
                            }
                        }
                        catch { }
                    }
                }

                if (!Directory.Exists(notesRoot))
                    Directory.CreateDirectory(notesRoot);

                services.AddInfrastructure(notesRoot);

                services.AddTransient<MainViewModel>(sp => new MainViewModel(
                    sp.GetRequiredService<INoteRepository>(),
                    sp.GetRequiredService<ISearchEngine>(),
                    sp.GetRequiredService<IOutlineService>(),
                    sp.GetRequiredService<ITagService>(),
                    sp.GetRequiredService<IFolderService>(),
                    sp.GetRequiredService<IRecoveryService>(),
                    sp.GetRequiredService<ITrashBinService>(),
                    sp.GetRequiredService<IFileWatcher>(),
                    sp.GetRequiredService<ICloudSyncService>(),
                    sp.GetRequiredService<ISettingsService>(),
                    notesRoot));

                services.AddTransient<MainWindow>(sp => new MainWindow(
                    sp.GetRequiredService<MainViewModel>(),
                    sp.GetRequiredService<IMarkdownBehaviorEngine>(),
                    sp.GetRequiredService<ICloudSyncService>(),
                    sp.GetRequiredService<ISettingsService>(),
                    sp.GetRequiredService<ITrashBinService>()));

                ServiceProvider = services.BuildServiceProvider();

                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();

                // Start named pipe server for single-instance activation
                _singleInstance.StartServer(args =>
                {
                    Dispatcher.Invoke(async () =>
                    {
                        SingleInstanceManager.ActivateWindow(mainWindow);

                        // If a file path was passed as argument, open it in a tab
                        if (args.Length > 0 && File.Exists(args[0]))
                        {
                            var vm = mainWindow.DataContext as MainViewModel;
                            if (vm != null)
                            {
                                await vm.OpenNoteByPathAsync(args[0]);
                            }
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Application failed to start:\n{ex.Message}", "QuickNote Startup Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        private static void LogCrash(string category, Exception? ex)
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "QuickNote");
                Directory.CreateDirectory(appDataDir);
                string crashLog = Path.Combine(appDataDir, "crash.log");
                string content = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}] [{category}]\n{ex}\n\n";
                File.AppendAllText(crashLog, content);
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}