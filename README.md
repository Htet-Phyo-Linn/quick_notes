# QuickNote ⚡

A modern, blazing-fast, local-first Markdown note-taking application powered by **Avalonia UI (.NET 10)** and **Milkdown WYSIWYG**. Designed with absolute filesystem transparency, multi-cloud synchronization, dynamic cross-platform theming, and zero vendor lock-in.

---

## 🌟 Highlights & Philosophy

* **📁 Markdown Files Are the Source of Truth**: Plain `.md` files organized in real directories on disk. No hidden proprietary databases, no binary formats, no lock-in.
* **🖋️ Dual Visual & Markdown Editor**: Rich Notion-like editing powered by Milkdown with inline Markdown intelligence, math rendering (KaTeX), code blocks with syntax highlighting, and customizable spellcheck.
* **🔄 Zero-Overlap Multi-Profile Storage**: Switch seamlessly between Work, Personal, Google Drive, and Local storage profiles with isolated physical directories that never contaminate each other.
* **🎨 Cross-Platform Dynamic Theming & JSON Schema**: Liquid Glass & Obsidian Cobalt color schemas available on both Windows and Android with instant JSON theme import/export.
* **⚡ Instant Search & Live Outline**: Real-time multi-word search, tag filtering, automatic deduplication, and clickable outline navigation that scrolls directly to headings.
* **🛡️ Reliability & Crash Recovery**: Atomic file writes, automatic background saves, and backup snapshots.

---

## 📦 Storage Profiles & Directory Locations

QuickNote maintains dedicated, clean directories for each storage account to prevent sync loops and data conflicts across both Windows and Android:

### 🪟 Windows File & Folder Locations

| Item | Default Location | Description |
| :--- | :--- | :--- |
| **☁️ Google Drive Synced Notes** | `C:\Users\<User>\Documents\QuickNote-GoogleDrive` | Real unredirected local documents folder for Google Drive notes |
| **💻 Local Storage (Offline)** | `C:\Users\<User>\Documents\QuickNote-Local` | Offline local notes (isolated from cloud) |
| **🏢 Work Profile (Commercial)** | `C:\Users\<User>\OneDrive - <Tenant>\QuickNote` | Microsoft OneDrive for Business |
| **🏠 Personal OneDrive** | `C:\Users\<User>\OneDrive\QuickNote` | Microsoft OneDrive Personal |
| **⚙️ App Settings & Profiles** | `%AppData%\QuickNote\settings.json` | JSON settings: theme, profiles, Google Drive credentials |
| **🛡️ Version History & Backups** | `<NotesRoot>\.backups\` | Timestamped `.bak` pre-save and manual snapshot files |
| **🗑️ Trash Bin** | `<NotesRoot>\.trash\` | Trashed notes with `.trash_index.json` restore tracking |
| **☁️ Cloud Catalog & Manifest** | `<NotesRoot>\.sync_catalog.json` | Local cache of the cloud manifest for 1-call delta sync |

> [!NOTE]
> On Windows systems where OneDrive Folder Backup redirects "My Documents", QuickNote uses `StoragePathHelper` to anchor Google Drive and Local profiles to the real unredirected `C:\Users\<User>\Documents` directory, completely avoiding nested directory loops.

---

### 🤖 Android File & Folder Locations

On Android, notes and configurations are isolated within the app's secure internal storage:

| Item | Android Location | Description |
| :--- | :--- | :--- |
| **☁️ Google Drive Synced Notes** | `/data/user/0/com.quicknote.android/files/QuickNote-GoogleDrive` | Primary notes directory synced with Google Drive |
| **💻 Local Notes (Offline)** | `/data/user/0/com.quicknote.android/files/QuickNote-Local` | Offline local notes storage |
| **⚙️ App Settings** | `/data/user/0/com.quicknote.android/files/QuickNote/settings.json` | Android preferences, active theme, Google OAuth tokens |
| **🛡️ Backups** | `/data/user/0/com.quicknote.android/files/QuickNote-GoogleDrive/.backups/` | Automated and manual note version snapshots |
| **🗑️ Trash Bin** | `/data/user/0/com.quicknote.android/files/QuickNote-GoogleDrive/.trash/` | Deleted notes awaiting restoration or permanent purge |
| **☁️ Cloud Catalog Cache** | `/data/user/0/com.quicknote.android/files/QuickNote-GoogleDrive/.sync_catalog.json` | Cached copy of the cloud catalog |

---

### ☁️ Google Drive Remote Structure

When Google Drive sync is enabled, QuickNote organizes files in your Google Drive cloud root as:

```
Google Drive/
└── QuickNote/                              # Main cloud root folder
    ├── quicknote_catalog.json             # Central manifest (enables fast 1-call delta sync)
    ├── CyberArk/
    │   └── CPM deployment plan.md         # Plain Markdown note files
    └── Work/
        └── Project Notes.md
```

---

## 🎨 Color Schemas & Dynamic Theming

QuickNote ships with 4 built-in presets accessible across desktop and mobile, with support for custom JSON theme configs:

* **Android Dark (Liquid Glass)**: Deep midnight space (`#0b0e14`, `#10141d`) with vibrant electric cyan accent (`#3390ec`) and translucent frosted cards.
* **Android Light (Daylight Canvas)**: Crisp snow canvas (`#f4f5f8`, `#ffffff`) with bright electric blue accent (`#3390ec`).
* **Windows Dark (Obsidian Cobalt)**: Classic obsidian slate (`#11141c`, `#171b26`) with desktop cobalt blue accent (`#2563eb`).
* **Windows Light (Modern Slate White)**: Clean white canvas (`#f8fafc`, `#ffffff`) with desktop cobalt blue accent (`#2563eb`).

### Importing Custom Themes (JSON)
Themes can be exported and imported using standard JSON files in the Settings dialog:

```json
{
  "id": "dracula-custom",
  "name": "Dracula Theme",
  "isDark": true,
  "colors": {
    "appBackground": "#282a36",
    "appCardBackground": "#343746",
    "appBorder": "#44475a",
    "appTextPrimary": "#f8f8f2",
    "appTextSecondary": "#6272a4",
    "accent": "#bd93f9",
    "editorBackground": "#282a36",
    "editorForeground": "#f8f8f2"
  }
}
```

---

## 🛠️ Project Structure

```
quick_notes/
├── src/
│   ├── QuickNote.Core/                    # Pure domain models, interfaces, Markdown parser, outline builder, theme schemas
│   │   ├── Models/                        # NoteDocument, SearchResult, ThemeSchema
│   │   ├── Interfaces/                    # INoteRepository, ICloudSyncService, ISearchEngine
│   │   ├── Markdown/                      # FrontMatter parsing, heading extraction, tag extraction
│   │   ├── Outline/                       # Live document outline tree generator
│   │   ├── Search/                        # SearchEngine with multi-word scoring & deduplication
│   │   └── Utilities/                     # StoragePathHelper, FileNameSanitizer
│   │
│   ├── QuickNote.Infrastructure/          # File storage, atomic writes, configuration, cloud sync engines
│   │   ├── Cloud/                         # GoogleDriveApiClient, GoogleCloudSyncEngine, OneDriveSyncService
│   │   ├── Configuration/                 # JsonSettingsService, AppSettings
│   │   └── FileSystem/                    # FileNoteRepository, AtomicFileWriter, TrashBinService
│   │
│   ├── QuickNote.Avalonia/                # Shared cross-platform Avalonia UI
│   │   ├── Assets/                        # Milkdown HTML/JS bundle & webview runtime
│   │   ├── Notion/                        # Milkdown WebView2 / WebView editor control
│   │   ├── Themes/                        # ThemeManager (DynamicResource brush injector)
│   │   ├── ViewModels/                    # MainViewModel (Editor, Notes, Tabs, Profile, Tree)
│   │   └── Views/                         # MainView (Desktop), MobileMainView (Android), SettingsWindow
│   │
│   ├── QuickNote.Avalonia.Desktop/        # Windows desktop runtime entry point (net10.0)
│   ├── QuickNote.Avalonia.Android/        # Android mobile runtime entry point (net10.0-android)
│   └── QuickNote.Installer/               # Standalone single-file installer builder (net10.0-windows)
│
├── tests/
│   └── QuickNote.Tests/                   # 150+ unit tests (xUnit) covering Core, Cloud, Search, and Theming
│
└── export.ps1                             # Unified export script -> dist/ (Windows Installer + Android APK)
```

---

## 🚀 Building & Running

### Prerequisites
* [.NET 10 SDK](https://dotnet.microsoft.com/)
* Windows 10/11 (for Desktop) or Android SDK (for Mobile)

### Run Desktop App
```powershell
dotnet run --project src/QuickNote.Avalonia/QuickNote.Avalonia.Desktop/QuickNote.Avalonia.Desktop.csproj
```

### Run Tests
```powershell
dotnet test tests/QuickNote.Tests/QuickNote.Tests.csproj
```

### Export Packages (Windows & Android)
QuickNote provides a single unified export script `export.ps1`:

* **Export Everything** (Windows Installer + Android APK):
  ```powershell
  .\export.ps1
  ```
* **Export Windows Installer Only** (`dist/QuickNoteSetup.exe`):
  ```powershell
  .\export.ps1 -Target Windows
  ```
* **Export Android APK Only** (`dist/QuickNote.apk`):
  ```powershell
  .\export.ps1 -Target Android
  ```

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| `Ctrl + N` | Create a new note |
| `Ctrl + S` | Force save active note |
| `Ctrl + F` | Focus search bar |
| `Ctrl + T` | Toggle dark / light mode |
| `Ctrl + B` | Toggle explorer sidebar |
| `Ctrl + Shift + O` | Toggle live document outline |
| `Ctrl + P` | Open Settings & Profiles dialog |
| `Escape` | Clear search or exit modals |

---

## 📄 License
This project is licensed under the MIT License.
