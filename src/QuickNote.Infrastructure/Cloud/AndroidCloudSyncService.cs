using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.Infrastructure.Cloud
{
    public class AndroidCloudSyncService : ICloudSyncService
    {
        private readonly ISettingsService _settingsService;
        private readonly INoteRepository _noteRepository;
        private readonly IFileWatcher _fileWatcher;
        private readonly CloudSyncEngine _syncEngine;
        private CloudSyncStatus _currentStatus = new();

        public event EventHandler<CloudSyncStatus>? SyncStatusChanged;

        public bool IsAuthenticated => _syncEngine.IsAuthenticated;
        public string? UserEmail => _syncEngine.UserEmail;
        public string? UserName => _syncEngine.UserName;
        public DateTime? LastSyncTime => _syncEngine.LastSyncTime;

        public AndroidCloudSyncService(
            ISettingsService settingsService,
            INoteRepository noteRepository,
            IFileWatcher fileWatcher)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _noteRepository = noteRepository ?? throw new ArgumentNullException(nameof(noteRepository));
            _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));

            _syncEngine = new CloudSyncEngine(_noteRepository.NotesRoot, _settingsService);
            _syncEngine.SyncStatusChanged += OnEngineSyncStatusChanged;

            RefreshStatus();
        }

        private void OnEngineSyncStatusChanged(object? sender, CloudSyncStatus e)
        {
            _currentStatus = e;
            SyncStatusChanged?.Invoke(this, e);
        }

        public string GetAuthorizationUrl(out string codeVerifier)
        {
            return _syncEngine.GetAuthorizationUrl(out codeVerifier);
        }

        public async Task<bool> CompleteSignInAsync(string code, string codeVerifier)
        {
            var success = await _syncEngine.SignInWithCodeAsync(code, codeVerifier);
            RefreshStatus();
            return success;
        }

        public async Task SignOutAsync()
        {
            await _syncEngine.SignOutAsync();
            RefreshStatus();
        }

        public async Task<SyncResult> TriggerSyncAsync()
        {
            if (!_syncEngine.IsAuthenticated)
            {
                return SyncResult.Fail("Not signed in to OneDrive");
            }
            return await _syncEngine.PerformSyncAsync();
        }

        public List<OneDriveAccountInfo> DetectOneDriveAccounts()
        {
            var accounts = new List<OneDriveAccountInfo>();
            if (_syncEngine.IsAuthenticated)
            {
                accounts.Add(new OneDriveAccountInfo
                {
                    AccountName = _syncEngine.UserEmail ?? "Connected Microsoft Account",
                    UserFolder = _noteRepository.NotesRoot,
                    IsBusiness = false
                });
            }
            return accounts;
        }

        public async Task<bool> EnableOneDriveSyncAsync(OneDriveAccountInfo account, bool migrateExistingNotes, string currentNotesRoot)
        {
            _settingsService.Settings.IsOneDriveSyncEnabled = true;
            await _settingsService.SaveSettingsAsync();
            RefreshStatus();
            return true;
        }

        public async Task<bool> DisableOneDriveSyncAsync(string localDocumentsNotesRoot)
        {
            await SignOutAsync();
            return true;
        }

        public CloudSyncStatus GetSyncStatus()
        {
            RefreshStatus(raiseEvent: false);
            return _currentStatus;
        }

        private void RefreshStatus(bool raiseEvent = true)
        {
            CloudSyncStatus newStatus;

            if (!_syncEngine.IsAuthenticated)
            {
                newStatus = new CloudSyncStatus
                {
                    State = CloudSyncState.Disabled,
                    Message = "OneDrive: Connect",
                    ActiveNotesPath = _noteRepository.NotesRoot,
                    AccountName = null
                };
            }
            else
            {
                newStatus = new CloudSyncStatus
                {
                    State = CloudSyncState.Synced,
                    Message = $"OneDrive: {_syncEngine.UserEmail ?? "Connected"}",
                    ActiveNotesPath = _noteRepository.NotesRoot,
                    AccountName = _syncEngine.UserEmail,
                    LastSyncTime = _syncEngine.LastSyncTime
                };
            }

            bool changed = _currentStatus.State != newStatus.State ||
                           _currentStatus.Message != newStatus.Message;

            _currentStatus = newStatus;

            if (changed && raiseEvent)
            {
                SyncStatusChanged?.Invoke(this, _currentStatus);
            }
        }
    }
}
