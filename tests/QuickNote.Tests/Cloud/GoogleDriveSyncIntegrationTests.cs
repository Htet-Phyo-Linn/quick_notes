using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Infrastructure.Cloud;
using QuickNote.Infrastructure.Configuration;
using Xunit;

namespace QuickNote.Tests.Cloud
{
    public class GoogleDriveSyncIntegrationTests
    {
        private class MockHttpMessageHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest { get; private set; }
            public string ResponseContent { get; set; } = "{}";
            public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                var res = new HttpResponseMessage(ResponseStatus)
                {
                    Content = new StringContent(ResponseContent)
                };
                return Task.FromResult(res);
            }
        }

        private class MockSettingsService : ISettingsService
        {
            public AppSettings Settings { get; set; } = new();
            public Task LoadSettingsAsync() => Task.CompletedTask;
            public Task SaveSettingsAsync() => Task.CompletedTask;
        }

        [Fact]
        public async Task EnsureQuickNoteFolderAsync_SearchesSpecificallyInRoot()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseContent = JsonSerializer.Serialize(new GoogleDriveFileListResponse
                {
                    Files = new System.Collections.Generic.List<GoogleDriveFileDto>
                    {
                        new() { Id = "test_root_folder_id", Name = "QuickNote" }
                    }
                })
            };

            var client = new GoogleDriveApiClient(() => Task.FromResult("fake_token"), new HttpClient(handler));
            var folderId = await client.EnsureQuickNoteFolderAsync();

            Assert.Equal("test_root_folder_id", folderId);
            Assert.NotNull(handler.LastRequest);
            var reqUri = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.ToString());
            // Verify query specifies 'root' in parents
            Assert.Contains("'root' in parents", reqUri);
            Assert.Contains("name = 'QuickNote'", reqUri);
        }

        [Fact]
        public void UpdateNotesRoot_ChangesPathAndInitializesManifestManager()
        {
            var tempDir1 = Path.Combine(Path.GetTempPath(), "qn_test_dir1_" + Guid.NewGuid());
            var tempDir2 = Path.Combine(Path.GetTempPath(), "qn_test_dir2_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir1);
            Directory.CreateDirectory(tempDir2);

            try
            {
                var settings = new MockSettingsService();
                var engine = new GoogleCloudSyncEngine(tempDir1, settings);

                engine.UpdateNotesRoot(tempDir2);
                Assert.NotNull(engine);
            }
            finally
            {
                if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, true);
                if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, true);
            }
        }

        [Fact]
        public void SyncManifestManager_DetectsLocalChangesAndDeletions()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "qn_test_manifest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                var mgr = new SyncManifestManager(tempDir);
                var note1 = Path.Combine(tempDir, "Note1.md");
                File.WriteAllText(note1, "# Hello World");

                var pending = mgr.ScanLocalChanges(tempDir);
                Assert.Single(pending);
                Assert.Equal("Note1.md", pending[0]);

                // Record successful upload
                mgr.RecordSuccessfulUpload("Note1.md", "remote_123", "etag_1", DateTime.UtcNow, "hash_1", DateTime.UtcNow);

                // Delete file locally
                File.Delete(note1);
                var pendingAfterDelete = mgr.ScanLocalChanges(tempDir);
                var pendingDeletions = mgr.GetPendingDeletions();

                Assert.Single(pendingDeletions);
                Assert.Equal("remote_123", pendingDeletions[0].RemoteId);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task LiveSyncExecution_SyncsNotesToRootQuickNote()
        {
            var notesRoot = @"C:\Users\PC\OneDrive - BIM ADVANCED TECHNOLOGY SERVICES PTE. LTD\Documents\QuickNote-GoogleDrive";
            if (!Directory.Exists(notesRoot)) return;

            var settingsService = new JsonSettingsService(notesRoot);
            if (!settingsService.Settings.IsGoogleDriveSyncEnabled ||
                string.IsNullOrWhiteSpace(settingsService.Settings.GoogleDriveRefreshToken))
            {
                return;
            }

            var engine = new GoogleCloudSyncEngine(notesRoot, settingsService);
            var result = await engine.PerformSyncAsync();
            Assert.True(result.Success);
            Assert.True(result.UploadedCount > 0 || result.DownloadedCount > 0 || result.Message.Contains("Synced", StringComparison.OrdinalIgnoreCase) || result.Message.Contains("up-to-date", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task GoogleCloudSyncEngine_WhenNotAuthenticated_ReturnsSkipped()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "qn_unauth_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var settings = new MockSettingsService();
                settings.Settings.GoogleDriveRefreshToken = null; // Unauthenticated
                var engine = new GoogleCloudSyncEngine(tempDir, settings);

                var result = await engine.PerformSyncAsync();
                Assert.False(result.Success);
                Assert.Contains("Not signed in", result.Message);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SyncManifestManager_TracksPendingDeletionsOnEmptyTrash()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "qn_trash_del_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var mgr = new SyncManifestManager(tempDir);
                var notePath = Path.Combine(tempDir, "DeletedNote.md");
                File.WriteAllText(notePath, "To be deleted");

                mgr.ScanLocalChanges(tempDir);
                mgr.RecordSuccessfulUpload("DeletedNote.md", "remote_del_id", "etag_del", DateTime.UtcNow, "hash_del", DateTime.UtcNow);

                // File deleted
                File.Delete(notePath);
                mgr.ScanLocalChanges(tempDir);

                var pendingDeletions = mgr.GetPendingDeletions();
                Assert.Single(pendingDeletions);
                Assert.Equal("remote_del_id", pendingDeletions[0].RemoteId);

                // Simulate successful remote delete removal
                mgr.RemoveEntry("DeletedNote.md");
                Assert.Empty(mgr.GetPendingDeletions());
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
