using System;
using Microsoft.Extensions.DependencyInjection;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Markdown;
using QuickNote.Core.Markdown.Behaviors;
using QuickNote.Core.Outline;
using QuickNote.Core.Search;
using QuickNote.Core.Services;
using QuickNote.Infrastructure.Cloud;
using QuickNote.Infrastructure.Configuration;
using QuickNote.Infrastructure.FileSystem;
using QuickNote.Infrastructure.Interfaces;
using QuickNote.Infrastructure.Recovery;
using QuickNote.Infrastructure.Services;
using QuickNote.Infrastructure.Watching;

namespace QuickNote.Avalonia.DependencyInjection
{
    public static class ServiceRegistration
    {
        public static void AddInfrastructure(this IServiceCollection services, string notesRoot)
        {
            // Core markdown behaviors
            services.AddSingleton<ICheckboxBehavior, CheckboxBehavior>();
            services.AddSingleton<IOrderedListBehavior, OrderedListBehavior>();
            services.AddSingleton<IBulletListBehavior, BulletListBehavior>();
            services.AddSingleton<IQuoteBehavior, QuoteBehavior>();
            services.AddSingleton<IIndentationBehavior, IndentationBehavior>();
            services.AddSingleton<IMarkdownBehaviorEngine, MarkdownBehaviorEngine>();

            // Core parsing, outline, search, tags, and folders
            services.AddSingleton<IMarkdownParser, MarkdownParser>();
            services.AddSingleton<IOutlineService, OutlineBuilder>();
            services.AddSingleton<ISearchEngine, SearchEngine>();
            services.AddSingleton<ITagService, TagService>();
            services.AddSingleton<IFolderService, FolderService>();

            // Infrastructure services
            services.AddSingleton<AtomicFileWriter>();
            services.AddSingleton<INoteRepository>(sp =>
                new FileNoteRepository(notesRoot, sp.GetRequiredService<IMarkdownParser>(), sp.GetRequiredService<AtomicFileWriter>()));

            services.AddSingleton<IFileChangeProcessor, FileChangeProcessor>();
            services.AddSingleton<IFileWatcher>(sp =>
                new FileWatcher(notesRoot, sp.GetRequiredService<IFileChangeProcessor>()));

            services.AddSingleton<IRecoveryService>(sp =>
                new RecoveryService(notesRoot));

            services.AddSingleton<ITrashBinService>(sp =>
                new TrashBinService(notesRoot));

            services.AddSingleton<ISettingsService>(sp =>
                new JsonSettingsService(notesRoot));

            if (OperatingSystem.IsAndroid())
            {
                services.AddSingleton<ICloudSyncService, AndroidCloudSyncService>();
            }
            else
            {
                services.AddSingleton<ICloudSyncService, OneDriveSyncService>();
            }
        }
    }
}
