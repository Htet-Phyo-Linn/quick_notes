using System.Threading.Tasks;

namespace QuickNote.Core.Interfaces
{
    public class AppSettings
    {
        public string NotesRoot { get; set; } = string.Empty;
        public int AutoSaveDelayMs { get; set; } = 500;
        public double FontSize { get; set; } = 14.0;
        public string FontFamily { get; set; } = "Consolas";
        public bool IsOneDriveSyncEnabled { get; set; }
        public string? OneDriveNotesPath { get; set; }
        public string? SelectedOneDriveAccountName { get; set; }
        public bool IsSpellCheckEnabled { get; set; } = false;
        public System.Collections.Generic.List<string> IgnoredWords { get; set; } = new();

        private System.Collections.Generic.Dictionary<string, bool> _folderExpandedStates = new(System.StringComparer.OrdinalIgnoreCase);
        public System.Collections.Generic.Dictionary<string, bool> FolderExpandedStates
        {
            get => _folderExpandedStates;
            set => _folderExpandedStates = value != null
                ? new System.Collections.Generic.Dictionary<string, bool>(value, System.StringComparer.OrdinalIgnoreCase)
                : new System.Collections.Generic.Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        }
    }

    public interface ISettingsService
    {
        AppSettings Settings { get; }
        Task LoadSettingsAsync();
        Task SaveSettingsAsync();
    }
}
