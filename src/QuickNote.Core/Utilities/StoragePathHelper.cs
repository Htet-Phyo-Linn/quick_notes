using System;
using System.IO;

namespace QuickNote.Core.Utilities
{
    public static class StoragePathHelper
    {
        /// <summary>
        /// Returns the true, local (unredirected) Documents directory on Windows, Android, and other platforms.
        /// On Windows, when OneDrive backs up "Documents", Environment.SpecialFolder.MyDocuments
        /// gets redirected into the OneDrive sync folder. This method ensures we use the real local C:\Users\PC\Documents.
        /// </summary>
        public static string GetRealLocalDocumentsFolder()
        {
            if (OperatingSystem.IsWindows())
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(userProfile))
                {
                    var userDocs = Path.Combine(userProfile, "Documents");
                    if (Directory.Exists(userDocs))
                    {
                        return userDocs;
                    }
                    try
                    {
                        Directory.CreateDirectory(userDocs);
                        return userDocs;
                    }
                    catch { }
                }
            }

            return Environment.GetFolderPath(OperatingSystem.IsAndroid()
                ? Environment.SpecialFolder.Personal
                : Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>
        /// Default local directory for Google Drive synced notes.
        /// E.g. "C:\Users\PC\Documents\QuickNote-GoogleDrive"
        /// </summary>
        public static string GetDefaultGoogleDriveNotesPath()
        {
            return Path.Combine(GetRealLocalDocumentsFolder(), "QuickNote-GoogleDrive");
        }

        /// <summary>
        /// Default local offline notes directory.
        /// E.g. "C:\Users\PC\Documents\QuickNote-Local"
        /// </summary>
        public static string GetDefaultLocalNotesPath()
        {
            return Path.Combine(GetRealLocalDocumentsFolder(), "QuickNote-Local");
        }
    }
}
