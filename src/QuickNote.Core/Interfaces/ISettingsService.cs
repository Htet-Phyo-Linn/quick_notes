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
        public string? OneDriveRefreshToken { get; set; }
        public string? OneDriveUserEmail { get; set; }
        public string? OneDriveUserName { get; set; }
        public string? OneDriveFolderId { get; set; }
        public string? OneDriveClientId { get; set; }
        public System.DateTime? LastCloudSyncTime { get; set; }
        public bool IsGoogleDriveSyncEnabled { get; set; }
        public string? GoogleDriveNotesPath { get; set; }
        public string? GoogleDriveRefreshToken { get; set; }
        public string? GoogleDriveUserEmail { get; set; }
        public string? GoogleDriveUserName { get; set; }
        public string? GoogleDriveFolderId { get; set; }
        public string? GoogleDriveClientId { get; set; }
        public string? GoogleDriveClientSecret { get; set; }
        public string? SelectedGoogleAccountName { get; set; }
        public System.DateTime? LastGoogleDriveSyncTime { get; set; }
        public bool IsSpellCheckEnabled { get; set; } = true;
        public string? MobileThemeStyle { get; set; } = "Android";
        public string CurrentThemeId { get; set; } = "android-dark";
        public System.Collections.Generic.List<QuickNote.Core.Models.ThemeSchema> CustomThemes { get; set; } = new();
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
