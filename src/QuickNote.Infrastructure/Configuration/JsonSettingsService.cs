using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;
using QuickNote.Infrastructure.FileSystem;
using QuickNote.Infrastructure.Security;

namespace QuickNote.Infrastructure.Configuration
{
    public class JsonSettingsService : ISettingsService
    {
        private readonly string _settingsFilePath;
        private readonly AtomicFileWriter _atomicFileWriter = new();
        private AppSettings _settings;

        public AppSettings Settings => _settings;

        public JsonSettingsService(string notesRoot)
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "QuickNote");

            Directory.CreateDirectory(appDataDir);
            _settingsFilePath = Path.Combine(appDataDir, "settings.json");
            _settings = new AppSettings { NotesRoot = notesRoot };

            LoadSettingsSync(notesRoot);
        }

        private void DecryptSensitiveTokens(AppSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.GoogleDriveRefreshToken))
                settings.GoogleDriveRefreshToken = SecureStorageHelper.Unprotect(settings.GoogleDriveRefreshToken);

            if (!string.IsNullOrEmpty(settings.GoogleDriveClientSecret))
                settings.GoogleDriveClientSecret = SecureStorageHelper.Unprotect(settings.GoogleDriveClientSecret);

            if (!string.IsNullOrEmpty(settings.OneDriveRefreshToken))
                settings.OneDriveRefreshToken = SecureStorageHelper.Unprotect(settings.OneDriveRefreshToken);
        }

        private AppSettings CreateEncryptedClone(AppSettings source)
        {
            var json = JsonSerializer.Serialize(source);
            var clone = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            if (!string.IsNullOrEmpty(clone.GoogleDriveRefreshToken))
                clone.GoogleDriveRefreshToken = SecureStorageHelper.Protect(clone.GoogleDriveRefreshToken);

            if (!string.IsNullOrEmpty(clone.GoogleDriveClientSecret))
                clone.GoogleDriveClientSecret = SecureStorageHelper.Protect(clone.GoogleDriveClientSecret);

            if (!string.IsNullOrEmpty(clone.OneDriveRefreshToken))
                clone.OneDriveRefreshToken = SecureStorageHelper.Protect(clone.OneDriveRefreshToken);

            return clone;
        }

        private void LoadSettingsSync(string fallbackNotesRoot)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        DecryptSensitiveTokens(loaded);
                        _settings = loaded;
                    }
                }
                else
                {
                    // Fallback migration: check if legacy .settings.json exists in fallbackNotesRoot
                    string legacyPath = Path.Combine(fallbackNotesRoot, ".settings.json");
                    if (File.Exists(legacyPath))
                    {
                        var json = File.ReadAllText(legacyPath);
                        var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                        if (loaded != null)
                        {
                            DecryptSensitiveTokens(loaded);
                            _settings = loaded;
                            _ = SaveSettingsAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JsonSettingsService] LoadSettingsSync error: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(_settings.NotesRoot))
            {
                _settings.NotesRoot = fallbackNotesRoot;
            }
        }

        public async Task LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        DecryptSensitiveTokens(loaded);
                        _settings = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JsonSettingsService] LoadSettingsAsync error: {ex.Message}");
            }
        }

        public async Task SaveSettingsAsync()
        {
            try
            {
                var encryptedCopy = CreateEncryptedClone(_settings);
                var json = JsonSerializer.Serialize(encryptedCopy, new JsonSerializerOptions { WriteIndented = true });
                await _atomicFileWriter.WriteAllTextAsync(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JsonSettingsService] SaveSettingsAsync error: {ex.Message}");
            }
        }
    }
}
