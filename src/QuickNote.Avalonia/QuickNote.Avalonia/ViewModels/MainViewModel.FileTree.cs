using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using QuickNote.Core.Models;

namespace QuickNote.Avalonia.ViewModels
{
    // Folder tree: refresh, expand-state persistence, title sync with nodes.
    public partial class MainViewModel
    {
        private DispatcherTimer? _saveSettingsTimer;
        private bool _isRefreshingTree;

        public void DebounceSaveSettings()
        {
            if (_saveSettingsTimer == null)
            {
                _saveSettingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _saveSettingsTimer.Tick += async (s, e) =>
                {
                    _saveSettingsTimer.Stop();
                    try
                    {
                        if (_settingsService != null)
                            await _settingsService.SaveSettingsAsync();
                    }
                    catch { }
                };
            }
            _saveSettingsTimer.Stop();
            _saveSettingsTimer.Start();
        }

        public async Task RefreshFolderTreeAsync()
        {
            if (_isRefreshingTree) return;
            _isRefreshingTree = true;
            try
            {
                var expandedMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                CaptureExpandedStates(FolderNodes, expandedMap);

                if (_settingsService?.Settings?.FolderExpandedStates != null)
                {
                    foreach (var kvp in _settingsService.Settings.FolderExpandedStates)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Key) && !expandedMap.ContainsKey(kvp.Key))
                            expandedMap[kvp.Key] = kvp.Value;
                    }
                }

                var root = await _folderService.BuildFolderTreeAsync(_notesRoot);
                FolderNodes.Clear();
                if (root != null)
                {
                    ApplyNoteTitlesToTree(root);
                    RestoreExpandedStates(root, expandedMap);
                    foreach (var child in root.Children)
                    {
                        AttachExpandListener(child);
                        FolderNodes.Add(child);
                    }
                }

                DebounceSaveSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RefreshFolderTreeAsync] {ex.Message}");
            }
            finally
            {
                _isRefreshingTree = false;
            }
        }

        private void AttachExpandListener(FolderNode node)
        {
            if (node.IsDirectory)
            {
                node.PropertyChanged -= OnFolderNodePropertyChanged;
                node.PropertyChanged += OnFolderNodePropertyChanged;

                foreach (var child in node.Children)
                {
                    AttachExpandListener(child);
                }
            }
        }

        private void OnFolderNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FolderNode.IsExpanded) && sender is FolderNode folderNode && folderNode.IsDirectory)
            {
                var rel = RelativePath(folderNode);
                if (!string.IsNullOrWhiteSpace(rel) && _settingsService?.Settings?.FolderExpandedStates != null)
                {
                    _settingsService.Settings.FolderExpandedStates[rel] = folderNode.IsExpanded;
                    DebounceSaveSettings();
                }
            }
        }

        private void CaptureExpandedStates(
            IEnumerable<FolderNode> nodes, Dictionary<string, bool> map)
        {
            foreach (var node in nodes)
            {
                if (!node.IsDirectory) continue;

                var rel = RelativePath(node);
                if (!string.IsNullOrWhiteSpace(rel))
                {
                    map[rel] = node.IsExpanded;

                    if (_settingsService?.Settings?.FolderExpandedStates != null)
                        _settingsService.Settings.FolderExpandedStates[rel] = node.IsExpanded;
                }

                if (node.Children.Count > 0)
                    CaptureExpandedStates(node.Children, map);
            }
        }

        private void RestoreExpandedStates(FolderNode node, Dictionary<string, bool> map)
        {
            if (!node.IsDirectory) return;

            var rel = RelativePath(node);
            if (!string.IsNullOrWhiteSpace(rel))
            {
                if (map.TryGetValue(rel, out bool isExpanded))
                {
                    node.IsExpanded = isExpanded;
                }
                else if (_settingsService?.Settings?.FolderExpandedStates != null &&
                         _settingsService.Settings.FolderExpandedStates.TryGetValue(rel, out bool savedState))
                {
                    node.IsExpanded = savedState;
                }
            }

            foreach (var child in node.Children)
                RestoreExpandedStates(child, map);
        }

        private string RelativePath(FolderNode node)
        {
            if (!string.IsNullOrWhiteSpace(node.RelativePath))
                return node.RelativePath.Replace('\\', '/').Trim('/');

            if (!string.IsNullOrWhiteSpace(node.FullPath) && !string.IsNullOrWhiteSpace(_notesRoot))
                return Path.GetRelativePath(_notesRoot, node.FullPath).Replace('\\', '/').Trim('/');

            return string.Empty;
        }

        private void ApplyNoteTitlesToTree(FolderNode node)
        {
            if (!node.IsDirectory)
            {
                var match = Notes.FirstOrDefault(n =>
                    string.Equals(n.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    string display = !string.IsNullOrWhiteSpace(match.Title) ? match.Title : match.FileName;
                    node.Title = display;
                    node.Name  = display;
                }
            }
            else
            {
                foreach (var child in node.Children)
                    ApplyNoteTitlesToTree(child);
            }
        }

        // ── Tree-node path/title helpers ───────────────────────────────
        private void UpdateTreeNodeTitle(
            IEnumerable<FolderNode> nodes, string fullPath, string newTitle)
        {
            foreach (var node in nodes)
            {
                if (!node.IsDirectory &&
                    string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    node.Title = newTitle;
                    node.Name  = newTitle;
                    return;
                }
                if (node.IsDirectory && node.Children.Count > 0)
                    UpdateTreeNodeTitle(node.Children, fullPath, newTitle);
            }
        }

        private void UpdateTreeNodePathAndTitle(
            IEnumerable<FolderNode> nodes, string oldPath, string newPath, string newTitle)
        {
            foreach (var node in nodes)
            {
                if (!node.IsDirectory &&
                    string.Equals(node.FullPath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    node.FullPath = newPath;
                    node.Title    = newTitle;
                    node.Name     = newTitle;
                    return;
                }
                if (node.IsDirectory && node.Children.Count > 0)
                    UpdateTreeNodePathAndTitle(node.Children, oldPath, newPath, newTitle);
            }
        }
    }
}
