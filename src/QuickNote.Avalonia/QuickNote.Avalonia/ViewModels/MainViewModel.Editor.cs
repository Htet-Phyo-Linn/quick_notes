using System;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;
using QuickNote.Infrastructure.Interfaces;

namespace QuickNote.Avalonia.ViewModels
{
    // Editor mode commands, outline, word/line count, cloud sync, and RelayCommands.
    public partial class MainViewModel
    {
        // ── Top-level RelayCommand shortcuts ──────────────────────────
        [RelayCommand] public async System.Threading.Tasks.Task NewNote()   => await NewNoteAsync();
        [RelayCommand] public async System.Threading.Tasks.Task NewFolder() => await NewFolderAsync();
        [RelayCommand] public async System.Threading.Tasks.Task Save()      => await SaveNoteAsync();
        [RelayCommand] public async System.Threading.Tasks.Task Delete()    => await DeleteNoteAsync();
        [RelayCommand] public async System.Threading.Tasks.Task Refresh()
        {
            if (_cloudSyncService != null && _cloudSyncService.IsAuthenticated)
            {
                try { await _cloudSyncService.TriggerSyncAsync(); } catch { }
            }
            await LoadAllAsync();
        }

        // ── Editor mode ────────────────────────────────────────────
        [RelayCommand]
        public void SetNotionMode()  { IsNotionMode = true;  IsReadingMode = false; }

        [RelayCommand]
        public void SetNormalEditorMode() { IsNotionMode = false; IsReadingMode = false; IsSplitViewActive = false; }

        [RelayCommand]
        public void SetEditMode() => SetNormalEditorMode();

        [RelayCommand]
        public void SetSplitMode() => SplitEditorRight();

        [RelayCommand]
        public void SetPreviewMode() { IsNotionMode = false; IsReadingMode = true; IsSplitViewActive = false; }

        [RelayCommand] public void ToggleSplit() => ToggleSplitView();
        [RelayCommand] public void ToggleSplitView()
        {
            if (IsSplitViewActive) CloseSplitEditor();
            else SplitEditorRight();
        }
        [RelayCommand] public void ToggleReading() => IsReadingMode = !IsReadingMode;
        [RelayCommand] public void ToggleTheme()   => IsDarkMode    = !IsDarkMode;

        // ── Export commands ─────────────────────────────────────────
        [RelayCommand] public void ExportAsMarkdown() => ExportRequested?.Invoke("md");
        [RelayCommand] public void ExportAsWord()     => ExportRequested?.Invoke("docx");
        [RelayCommand] public void ExportAsPdf()      => ExportRequested?.Invoke("pdf");

        // ── Tags ───────────────────────────────────────────────────
        [RelayCommand]
        public void StartAddTag()
        {
            foreach (var t in CurrentNoteTags)
            {
                if (t.IsEditing)
                {
                    t.EditText = t.Name;
                    t.IsEditing = false;
                }
            }
            IsAddingTag = true;
            NewTagText = string.Empty;
        }

        [RelayCommand]
        public void CancelAddTag()
        {
            IsAddingTag = false;
            NewTagText = string.Empty;
        }

        [RelayCommand]
        public async System.Threading.Tasks.Task ConfirmAddTag()
        {
            if (CurrentNote == null)
            {
                IsAddingTag = false;
                NewTagText = string.Empty;
                return;
            }

            var cleanTag = (NewTagText ?? string.Empty).Trim().TrimStart('#');
            if (!string.IsNullOrWhiteSpace(cleanTag) &&
                !CurrentNoteTags.Any(t => string.Equals(t.Name, cleanTag, StringComparison.OrdinalIgnoreCase)))
            {
                CurrentNoteTags.Add(new TagItemViewModel(cleanTag));
                CurrentNote.Tags = CurrentNoteTags.Select(t => t.Name).ToArray();
                _isModified = true;
                await SaveNoteAsync();
            }

            IsAddingTag = false;
            NewTagText = string.Empty;
        }

        [RelayCommand]
        public void StartEditTagItem(TagItemViewModel? item)
        {
            if (item == null) return;
            IsAddingTag = false;
            foreach (var t in CurrentNoteTags)
            {
                if (t != item && t.IsEditing)
                {
                    t.EditText = t.Name;
                    t.IsEditing = false;
                }
            }
            item.EditText = item.Name;
            item.IsEditing = true;
        }

        [RelayCommand]
        public async System.Threading.Tasks.Task ConfirmEditTagItem(TagItemViewModel? item)
        {
            if (CurrentNote == null || item == null) return;
            var cleanTag = (item.EditText ?? string.Empty).Trim().TrimStart('#');
            if (string.IsNullOrWhiteSpace(cleanTag))
            {
                await RemoveTagItem(item);
                return;
            }

            item.Name = cleanTag;
            item.EditText = cleanTag;
            item.IsEditing = false;
            CurrentNote.Tags = CurrentNoteTags.Select(t => t.Name).ToArray();
            _isModified = true;
            await SaveNoteAsync();
        }

        [RelayCommand]
        public void CancelEditTagItem(TagItemViewModel? item)
        {
            if (item == null) return;
            item.EditText = item.Name;
            item.IsEditing = false;
        }

        [RelayCommand]
        public async System.Threading.Tasks.Task RemoveTagItem(TagItemViewModel? item)
        {
            if (CurrentNote == null || item == null) return;
            if (CurrentNoteTags.Remove(item))
            {
                CurrentNote.Tags = CurrentNoteTags.Select(t => t.Name).ToArray();
                _isModified = true;
                await SaveNoteAsync();
            }
        }

        public async System.Threading.Tasks.Task CommitAllEditingTags()
        {
            bool needsSave = false;

            if (IsAddingTag)
            {
                var cleanNew = (NewTagText ?? string.Empty).Trim().TrimStart('#');
                if (!string.IsNullOrWhiteSpace(cleanNew) &&
                    CurrentNote != null &&
                    !CurrentNoteTags.Any(t => string.Equals(t.Name, cleanNew, StringComparison.OrdinalIgnoreCase)))
                {
                    CurrentNoteTags.Add(new TagItemViewModel(cleanNew));
                    needsSave = true;
                }
                IsAddingTag = false;
                NewTagText = string.Empty;
            }

            var editingTags = CurrentNoteTags.Where(t => t.IsEditing).ToList();
            foreach (var tag in editingTags)
            {
                var cleanTag = (tag.EditText ?? string.Empty).Trim().TrimStart('#');
                if (string.IsNullOrWhiteSpace(cleanTag))
                {
                    CurrentNoteTags.Remove(tag);
                    needsSave = true;
                }
                else if (!string.Equals(tag.Name, cleanTag, StringComparison.Ordinal))
                {
                    tag.Name = cleanTag;
                    tag.EditText = cleanTag;
                    tag.IsEditing = false;
                    needsSave = true;
                }
                else
                {
                    tag.IsEditing = false;
                }
            }

            if (needsSave && CurrentNote != null)
            {
                CurrentNote.Tags = CurrentNoteTags.Select(t => t.Name).ToArray();
                _isModified = true;
                await SaveNoteAsync();
            }
        }

        // ── Heading navigation ───────────────────────────────────────
        [RelayCommand]
        public void NavigateToHeading(Heading? heading)
        {
            if (heading == null) return;
            HeadingNavigationRequested?.Invoke(heading);
            if (heading.LineNumber > 0)
                ScrollToLineRequested?.Invoke(heading.LineNumber);
        }

        // ── Outline ───────────────────────────────────────────────
        private void UpdateOutline(string markdown)
        {
            OutlineHeadings.Clear();
            if (string.IsNullOrWhiteSpace(markdown)) return;
            try
            {
                foreach (var h in _outlineService.BuildOutline(markdown))
                    OutlineHeadings.Add(h);
            }
            catch { }
        }

        // ── Word / line count ───────────────────────────────────────
        private void UpdateWordAndLineCounts(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                WordCountText = "0 words";
                LineCountText = "0 lines";
                return;
            }

            int words = 0, lines = 1;
            bool inWord = false;
            foreach (char c in text)
            {
                if (c == '\n') lines++;
                if (char.IsWhiteSpace(c)) { inWord = false; }
                else if (!inWord)         { inWord = true; words++; }
            }

            WordCountText = $"{words} words";
            LineCountText = $"{lines} lines";
        }

        // ── Cloud sync ──────────────────────────────────────────────
        private void InitializeCloudSync()
        {
            try
            {
                var status = _cloudSyncService.GetSyncStatus();
                UpdateCloudSyncState(status);
                _cloudSyncService.SyncStatusChanged += (s, e) =>
                    Dispatcher.UIThread.Post(() => UpdateCloudSyncState(e));
            }
            catch { }
        }

        private void UpdateCloudSyncState(CloudSyncStatus status)
        {
            if (status == null) return;
            IsCloudSyncEnabled = status.State != CloudSyncState.Disabled;
            IsCloudSyncWarning = status.State == CloudSyncState.Warning ||
                                 status.State == CloudSyncState.Error;

            string stateStr = status.State switch
            {
                CloudSyncState.Synced  => "Synced",
                CloudSyncState.Syncing => "Syncing...",
                CloudSyncState.Warning => "Offline",
                CloudSyncState.Error   => "Error",
                _                      => "Disabled"
            };

            CloudSyncStatusText = !string.IsNullOrWhiteSpace(status.Message) ? status.Message : $"Sync: {stateStr}";
            CloudSyncTooltip    = $"Account: {status.AccountName ?? "Connected"}\n"
                                 + $"Status: {status.Message}\n"
                                 + $"Folder: {status.ActiveNotesPath ?? _notesRoot}";
        }
    }
}
