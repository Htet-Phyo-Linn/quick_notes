using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.ViewModels
{
    public partial class BackupHistoryViewModel : ViewModelBase
    {
        private readonly IBackupService _backupService;
        private readonly MainViewModel _mainViewModel;
        private List<NoteBackupGroup> _allGroups = new();
        private CancellationTokenSource? _previewCts;

        public ObservableCollection<NoteBackupGroup> FilteredGroups { get; } = new();
        public ObservableCollection<NoteBackupVersion> Versions { get; } = new();

        [ObservableProperty] private string _searchText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
        private NoteBackupGroup? _selectedGroup;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedVersion))]
        private NoteBackupVersion? _selectedVersion;

        [ObservableProperty] private string _previewContent = string.Empty;
        [ObservableProperty] private string _statusMessage = string.Empty;
        [ObservableProperty] private bool _isLoading;

        public bool HasSelectedGroup => SelectedGroup != null;
        public bool HasSelectedVersion => SelectedVersion != null;

        public BackupHistoryViewModel(IBackupService backupService, MainViewModel mainViewModel)
        {
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        }

        public async Task InitializeAsync(string? preferredNoteTitle = null)
        {
            IsLoading = true;
            StatusMessage = "Scanning version history...";
            try
            {
                _allGroups = await _backupService.GetBackupGroupsAsync();
                ApplyFilter();

                // Select preferred note (current note in editor) if available
                NoteBackupGroup? toSelect = null;
                if (!string.IsNullOrWhiteSpace(preferredNoteTitle))
                {
                    toSelect = FilteredGroups.FirstOrDefault(g =>
                        string.Equals(g.NoteTitle, preferredNoteTitle, StringComparison.OrdinalIgnoreCase));
                }
                toSelect ??= FilteredGroups.FirstOrDefault();

                if (toSelect != null)
                {
                    SelectedGroup = null;
                    SelectedGroup = toSelect;
                }
                else
                {
                    SelectedGroup = null;
                    StatusMessage = _allGroups.Count == 0 ? "No backups found yet." : "Select a note to view history.";
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedGroupChanged(NoteBackupGroup? value)
        {
            UpdateVersionsList(value);
        }

        partial void OnSelectedVersionChanged(NoteBackupVersion? value)
        {
            _ = LoadPreviewForVersionAsync(value);
        }

        private void ApplyFilter()
        {
            FilteredGroups.Clear();
            var query = SearchText?.Trim() ?? string.Empty;

            var filtered = string.IsNullOrEmpty(query)
                ? _allGroups
                : _allGroups.Where(g => g.NoteTitle.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var g in filtered)
            {
                FilteredGroups.Add(g);
            }

            if (SelectedGroup != null && !FilteredGroups.Contains(SelectedGroup))
            {
                SelectedGroup = FilteredGroups.FirstOrDefault();
            }
        }

        private void UpdateVersionsList(NoteBackupGroup? group)
        {
            Versions.Clear();
            PreviewContent = string.Empty;

            if (group != null)
            {
                foreach (var v in group.Versions)
                {
                    Versions.Add(v);
                }

                StatusMessage = $"{group.NoteTitle}: {Versions.Count} revision(s)";
                SelectedVersion = Versions.Count > 0 ? Versions[0] : null;
            }
            else
            {
                SelectedVersion = null;
                StatusMessage = _allGroups.Count == 0 ? "No backups found yet." : "Select a note to view history.";
            }
        }

        public async Task LoadPreviewForVersionAsync(NoteBackupVersion? version)
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = null;

            if (version == null)
            {
                PreviewContent = string.Empty;
                return;
            }

            var cts = new CancellationTokenSource();
            _previewCts = cts;

            try
            {
                var content = await _backupService.ReadBackupContentAsync(version.BackupFilePath);
                if (!cts.Token.IsCancellationRequested)
                {
                    PreviewContent = content;
                }
            }
            catch (OperationCanceledException)
            {
                // Task canceled because user switched selection rapidly
            }
            catch (Exception ex)
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    PreviewContent = $"[Error reading snapshot: {ex.Message}]";
                }
            }
        }

        [RelayCommand]
        public void SelectGroup(NoteBackupGroup? group)
        {
            if (SelectedGroup == group)
            {
                UpdateVersionsList(group);
            }
            else
            {
                SelectedGroup = group;
            }
        }

        [RelayCommand]
        public async Task SelectVersion(NoteBackupVersion? version)
        {
            if (SelectedVersion == version)
            {
                await LoadPreviewForVersionAsync(version);
            }
            else
            {
                SelectedVersion = version;
            }
        }

        [RelayCommand]
        public async Task RestoreVersionAsync()
        {
            if (SelectedGroup == null || SelectedVersion == null) return;

            var notePath = SelectedGroup.OriginalFilePath;
            var backupPath = SelectedVersion.BackupFilePath;
            var versionTime = SelectedVersion.FormattedDateTime;
            var currentTitle = SelectedGroup.NoteTitle;

            StatusMessage = "Restoring version...";
            try
            {
                var success = await _backupService.RestoreBackupAsync(notePath, backupPath);
                if (success)
                {
                    StatusMessage = $"Restored snapshot from {versionTime}!";

                    // If restored note is currently open, reload content in editor
                    if (_mainViewModel.CurrentNote != null &&
                        string.Equals(Path.GetFullPath(_mainViewModel.CurrentNote.FullPath), Path.GetFullPath(notePath), StringComparison.OrdinalIgnoreCase))
                    {
                        var content = await File.ReadAllTextAsync(notePath);
                        _mainViewModel.CurrentNote.Content = content;
                        _mainViewModel.CurrentNote.LastModified = DateTime.Now;
                        _mainViewModel.EditorContent = content;
                        await _mainViewModel.SaveNoteAsync();
                    }

                    await _mainViewModel.RefreshFolderTreeAsync();
                    _allGroups = await _backupService.GetBackupGroupsAsync();
                    ApplyFilter();

                    SelectedGroup = null;
                    SelectedGroup = FilteredGroups.FirstOrDefault(g =>
                        string.Equals(g.NoteTitle, currentTitle, StringComparison.OrdinalIgnoreCase)) ?? FilteredGroups.FirstOrDefault();
                }
                else
                {
                    StatusMessage = "Failed to restore backup snapshot.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Restore failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task CreateManualSnapshotAsync()
        {
            var note = _mainViewModel.CurrentNote;
            if (note == null || !File.Exists(note.FullPath))
            {
                StatusMessage = "No active note to snapshot.";
                return;
            }

            StatusMessage = "Creating manual snapshot...";
            try
            {
                // Ensure note is saved first
                await _mainViewModel.SaveNoteAsync();
                var created = await _backupService.CreateManualBackupAsync(note.FullPath);
                if (created != null)
                {
                    StatusMessage = $"Manual snapshot created: {Path.GetFileName(created)}";
                    _allGroups = await _backupService.GetBackupGroupsAsync();
                    ApplyFilter();

                    var curGroup = FilteredGroups.FirstOrDefault(g =>
                        string.Equals(g.NoteTitle, note.Title, StringComparison.OrdinalIgnoreCase));
                    if (curGroup != null)
                    {
                        SelectedGroup = null;
                        SelectedGroup = curGroup;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Manual snapshot error: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task CopyPreviewAsync()
        {
            if (string.IsNullOrEmpty(PreviewContent)) return;

            try
            {
                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var clipboard = lifetime?.MainWindow?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(PreviewContent);
                    StatusMessage = "Snapshot text copied to clipboard!";
                }
            }
            catch { }
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            await InitializeAsync(SelectedGroup?.NoteTitle);
        }

        [RelayCommand]
        public void Close()
        {
            _mainViewModel.IsBackupHistoryDialogVisible = false;
        }
    }
}
