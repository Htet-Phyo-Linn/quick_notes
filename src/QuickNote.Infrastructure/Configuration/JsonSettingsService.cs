using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using QuickNote.Core.Interfaces;

namespace QuickNote.Infrastructure.Configuration
{
    public class JsonSettingsService : ISettingsService
    {
        private readonly string _settingsFilePath;
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
                            _settings = loaded;
                            File.WriteAllText(_settingsFilePath, json);
                        }
                    }
                }
            }
            catch { }

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
                        _settings = loaded;
                    }
                }
            }
            catch { }
        }

        public async Task SaveSettingsAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_settingsFilePath, json);
            }
            catch { }
        }
    }
}
