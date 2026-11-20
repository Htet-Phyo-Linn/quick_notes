using CommunityToolkit.Mvvm.ComponentModel;
using QuickNote.Core.Interfaces;

namespace QuickNote.Avalonia.ViewModels
{
    public partial class ProfileItemViewModel : ObservableObject
    {
        public string DisplayName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty; // "Work Profile", "Personal Account", "Local Storage"
        public string Path { get; set; } = string.Empty;
        public bool IsBusiness { get; set; }
        public bool IsLocal { get; set; }
        public bool IsGoogleDrive { get; set; }
        [ObservableProperty] private bool _isActive;
        [ObservableProperty] private bool _isSignedIn = true;
        public OneDriveAccountInfo? OneDriveAccount { get; set; }

        public string IconGeometryKey => IsGoogleDrive ? "IconCloud" : (IsLocal ? "IconFolder" : (IsBusiness ? "IconBuilding" : "IconUser"));
        public string BadgeBackground => IsGoogleDrive ? "#1e3a5f" : (IsLocal ? "#27272a" : (IsBusiness ? "#1e3a8a" : "#064e3b"));
        public string BadgeForeground => IsGoogleDrive ? "#60a5fa" : (IsLocal ? "#9ca3af" : (IsBusiness ? "#93c5fd" : "#6ee7b7"));
        public string IconForeground => IsGoogleDrive ? "#38bdf8" : (IsLocal ? "#9ca3af" : (IsBusiness ? "#3b82f6" : "#10b981"));
    }
}
