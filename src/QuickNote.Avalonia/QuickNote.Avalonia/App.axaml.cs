using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using QuickNote.Avalonia.DependencyInjection;
using QuickNote.Avalonia.Themes;
using QuickNote.Avalonia.ViewModels;
using QuickNote.Avalonia.Views;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Utilities;
using QuickNote.Infrastructure.Configuration;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.Avalonia;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        string defaultNotesRoot = StoragePathHelper.GetDefaultLocalNotesPath();

        // Attempt to load settings to respect any configured OneDrive or custom notes folder
        var settingsService = new JsonSettingsService(defaultNotesRoot);
        ThemeManager.Instance.Initialize(settingsService);
        string notesRoot = !string.IsNullOrWhiteSpace(settingsService.Settings.NotesRoot)
            ? settingsService.Settings.NotesRoot
            : defaultNotesRoot;

        if (!Directory.Exists(notesRoot))
        {
            try
            {
                Directory.CreateDirectory(notesRoot);
            }
            catch { }
        }

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddInfrastructure(notesRoot);
        serviceCollection.AddSingleton<MainViewModel>(sp => new MainViewModel(
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
            notesRoot,
            sp.GetService<IMarkdownParser>(),
            sp.GetService<IBackupService>()
        ));

        Services = serviceCollection.BuildServiceProvider();
        var mainViewModel = Services.GetRequiredService<MainViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            if (desktop.Args?.Contains("--mobile") == true || Environment.GetEnvironmentVariable("SIMULATE_MOBILE") == "1")
            {
                mainWindow.Title = "QuickNote Mobile Simulator (Android 412x869)";
                mainWindow.Width = 412;
                mainWindow.Height = 869;
                mainWindow.MinWidth = 360;
                mainWindow.MinHeight = 600;
                mainWindow.Content = new MobileMainView
                {
                    DataContext = mainViewModel
                };
            }
            desktop.MainWindow = mainWindow;
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MobileMainView { DataContext = mainViewModel };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MobileMainView
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}