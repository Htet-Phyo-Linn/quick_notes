Absolutely. Based on the design decisions so far, I would build this as a \*\*Windows-first, local-first Markdown note application\*\* with the filesystem as the only persistent source of truth.



\# QuickNote — Full Architecture Design



\## 1. Architecture goals



The application should optimize for:



\* ⚡ Very fast startup and typing

\* 📝 Notepad-like simplicity

\* 🧠 Markdown intelligence while typing

\* 💾 Automatic saving

\* 🛡️ Crash recovery

\* 📁 Real filesystem folders

\* 🔎 Fast search

\* 📑 Live document outline

\* 🏷️ Markdown-native tags

\* 🔄 External file change detection

\* 🚫 No database

\* 🚫 No proprietary note format

\* 🚫 No cloud dependency

\* 📦 Portable `.md` files

\* 🧹 Clean/SOLID architecture

\* 🪶 Low memory and CPU usage



The most important architectural rule:



> \*\*Markdown files and folders are the source of truth. Everything else is disposable runtime state.\*\*



\---



\# 2. High-level architecture



```text

&#x20;                        ┌─────────────────────────────┐

&#x20;                        │        QuickNote UI         │

&#x20;                        │            WPF              │

&#x20;                        ├─────────────────────────────┤

&#x20;                        │ Search │ Editor │ Outline   │

&#x20;                        │ Folder Tree │ Tabs │ Status  │

&#x20;                        └──────────────┬──────────────┘

&#x20;                                       │

&#x20;                                       ▼

&#x20;                        ┌─────────────────────────────┐

&#x20;                        │    Application Services     │

&#x20;                        ├─────────────────────────────┤

&#x20;                        │ Document Service             │

&#x20;                        │ Folder Service               │

&#x20;                        │ Search Service               │

&#x20;                        │ Navigation Service           │

&#x20;                        │ Autosave Service             │

&#x20;                        │ Recovery Service             │

&#x20;                        │ Watcher Service              │

&#x20;                        └──────────────┬──────────────┘

&#x20;                                       │

&#x20;                                       ▼

&#x20;                        ┌─────────────────────────────┐

&#x20;                        │        QuickNote Core       │

&#x20;                        ├─────────────────────────────┤

&#x20;                        │ Models                       │

&#x20;                        │ Interfaces                   │

&#x20;                        │ Markdown Parser              │

&#x20;                        │ Markdown Behaviors           │

&#x20;                        │ Search Engine                │

&#x20;                        │ Outline Engine               │

&#x20;                        │ Tag Engine                   │

&#x20;                        └──────────────┬──────────────┘

&#x20;                                       │

&#x20;                                       ▼

&#x20;                 ┌────────────────────────────────────────┐

&#x20;                 │       Infrastructure / FileSystem      │

&#x20;                 ├────────────────────────────────────────┤

&#x20;                 │ File I/O                                │

&#x20;                 │ Atomic Save                             │

&#x20;                 │ FileSystemWatcher                       │

&#x20;                 │ Logging                                 │

&#x20;                 │ Settings                                │

&#x20;                 │ Recovery Storage                         │

&#x20;                 └──────────────────┬─────────────────────┘

&#x20;                                    │

&#x20;                                    ▼

&#x20;                 ┌────────────────────────────────────────┐

&#x20;                 │             FILESYSTEM                  │

&#x20;                 │                                          │

&#x20;                 │ QuickNote/                               │

&#x20;                 │ ├── Inbox/                              │

&#x20;                 │ ├── Work/                               │

&#x20;                 │ ├── Projects/                           │

&#x20;                 │ └── Archive/                            │

&#x20;                 │                                          │

&#x20;                 │ \*.md + folders                          │

&#x20;                 └────────────────────────────────────────┘

```



\---



\# 3. Technology stack



I recommend:



| Component     | Technology                               |

| ------------- | ---------------------------------------- |

| OS            | Windows                                  |

| Language      | C#                                       |

| Runtime       | .NET 10 LTS                              |

| UI            | WPF                                      |

| Architecture  | Clean Architecture / layered             |

| MVVM          | CommunityToolkit.Mvvm                    |

| Markdown      | Markdig                                  |

| File watching | `FileSystemWatcher`                      |

| Search        | Custom in-memory engine                  |

| Persistence   | Markdown files                           |

| Configuration | JSON                                     |

| Logging       | Microsoft.Extensions.Logging             |

| Testing       | xUnit                                    |

| DI            | Microsoft.Extensions.DependencyInjection |

| Installer     | MSIX / WiX later                         |

| Database      | \*\*None\*\*                                 |



\---



\# 4. Solution structure



I recommend this structure rather than putting everything inside one WPF project.



```text

QuickNote/

│

├── QuickNote.sln

│

├── src/

│   │

│   ├── QuickNote.App/

│   │   ├── App.xaml

│   │   ├── App.xaml.cs

│   │   │

│   │   ├── Views/

│   │   │   ├── MainWindow.xaml

│   │   │   ├── EditorView.xaml

│   │   │   ├── SearchView.xaml

│   │   │   ├── FolderTreeView.xaml

│   │   │   ├── OutlineView.xaml

│   │   │   ├── TagView.xaml

│   │   │   └── SettingsView.xaml

│   │   │

│   │   ├── ViewModels/

│   │   │   ├── MainViewModel.cs

│   │   │   ├── EditorViewModel.cs

│   │   │   ├── SearchViewModel.cs

│   │   │   ├── FolderTreeViewModel.cs

│   │   │   ├── OutlineViewModel.cs

│   │   │   ├── TagViewModel.cs

│   │   │   └── SettingsViewModel.cs

│   │   │

│   │   ├── Controls/

│   │   │   ├── MarkdownEditor.xaml

│   │   │   ├── SearchBox.xaml

│   │   │   ├── FolderTree.xaml

│   │   │   ├── OutlinePanel.xaml

│   │   │   └── StatusBar.xaml

│   │   │

│   │   ├── Converters/

│   │   ├── Behaviors/

│   │   ├── Resources/

│   │   │   ├── Styles.xaml

│   │   │   ├── Themes/

│   │   │   └── Icons/

│   │   │

│   │   └── DependencyInjection/

│   │       └── ServiceRegistration.cs

│   │

│   ├── QuickNote.Core/

│   │   │

│   │   ├── Models/

│   │   │   ├── NoteDocument.cs

│   │   │   ├── FolderNode.cs

│   │   │   ├── Heading.cs

│   │   │   ├── Tag.cs

│   │   │   ├── SearchResult.cs

│   │   │   ├── SearchMatch.cs

│   │   │   └── DocumentMetadata.cs

│   │   │

│   │   ├── Interfaces/

│   │   │   ├── INoteRepository.cs

│   │   │   ├── IFolderService.cs

│   │   │   ├── IMarkdownParser.cs

│   │   │   ├── ISearchEngine.cs

│   │   │   ├── IOutlineService.cs

│   │   │   ├── ITagService.cs

│   │   │   ├── IFileWatcher.cs

│   │   │   ├── IAutosaveService.cs

│   │   │   ├── IRecoveryService.cs

│   │   │   ├── ISettingsService.cs

│   │   │   └── INavigationService.cs

│   │   │

│   │   ├── Markdown/

│   │   │   ├── MarkdownParser.cs

│   │   │   ├── HeadingParser.cs

│   │   │   ├── TagParser.cs

│   │   │   └── MarkdownBehaviorEngine.cs

│   │   │

│   │   ├── Search/

│   │   │   ├── SearchEngine.cs

│   │   │   ├── SearchIndex.cs

│   │   │   ├── SearchTokenizer.cs

│   │   │   └── SearchRanking.cs

│   │   │

│   │   ├── Outline/

│   │   │   ├── OutlineBuilder.cs

│   │   │   └── HeadingTree.cs

│   │   │

│   │   ├── Services/

│   │   │   ├── DocumentService.cs

│   │   │   ├── FolderService.cs

│   │   │   ├── TagService.cs

│   │   │   └── NavigationService.cs

│   │   │

│   │   └── Exceptions/

│   │

│   └── QuickNote.Infrastructure/

│       │

│       ├── FileSystem/

│       │   ├── FileNoteRepository.cs

│       │   ├── FileSystemService.cs

│       │   ├── AtomicFileWriter.cs

│       │   └── FileScanner.cs

│       │

│       ├── Watching/

│       │   ├── FileWatcher.cs

│       │   ├── FileChangeDebouncer.cs

│       │   └── FileChangeEvent.cs

│       │

│       ├── Recovery/

│       │   ├── RecoveryService.cs

│       │   └── RecoveryFileStore.cs

│       │

│       ├── Configuration/

│       │   └── JsonSettingsService.cs

│       │

│       └── Logging/

│           └── LoggingService.cs

│

├── tests/

│   │

│   ├── QuickNote.Core.Tests/

│   │   ├── Markdown/

│   │   ├── Search/

│   │   ├── Outline/

│   │   └── Services/

│   │

│   └── QuickNote.Infrastructure.Tests/

│       ├── FileSystem/

│       ├── Recovery/

│       └── Watching/

│

├── docs/

│   ├── architecture.md

│   ├── file-format.md

│   ├── search.md

│   └── development.md

│

└── README.md

```



\---



\# 5. Layer dependency rules



This is important for keeping the project maintainable.



```text

App

&#x20;│

&#x20;▼

Core

&#x20;▲

&#x20;│

Infrastructure

```



More precisely:



```text

QuickNote.App

&#x20;     │

&#x20;     ▼

QuickNote.Core

&#x20;     ▲

&#x20;     │

QuickNote.Infrastructure

```



\### App



Responsible for:



\* WPF

\* UI

\* ViewModels

\* commands

\* bindings

\* user interaction



\### Core



Responsible for:



\* business rules

\* Markdown behavior

\* search

\* outline

\* tags

\* document models

\* interfaces



Core should \*\*not know about WPF\*\*.



\### Infrastructure



Responsible for:



\* filesystem

\* file watcher

\* atomic writes

\* settings

\* logging

\* recovery files



This prevents filesystem code from spreading throughout the application.



\---



\# 6. Filesystem architecture



The filesystem is the database.



Example:



```text

C:\\Users\\User\\Documents\\QuickNote\\

│

├── Inbox\\

│   ├── Today.md

│   └── Ideas.md

│

├── Work\\

│   ├── Cortex.md

│   └── SOC.md

│

├── Projects\\

│   ├── Network\\

│   │   ├── OSPF.md

│   │   └── Firewall.md

│   │

│   └── QuickNote\\

│       └── Architecture.md

│

└── Archive\\

```



There is no:



```text

notes.db

index.db

notes.json

search.db

```



\---



\# 7. Note file format



Normal Markdown.



Example:



```markdown

\---

tags:

&#x20; - cybersecurity

&#x20; - cortex-xdr

&#x20; - soc

\---



\# Cortex XDR Assessment



\## Executive Summary



Assessment completed for the environment.



\## Incidents



\### High Severity



Three high severity incidents were identified.



\### Medium Severity



Twelve medium severity incidents were identified.



\## Recommendations



\- Review disconnected endpoints

\- Investigate Linux kernel module failures

```



This remains valid Markdown and can be opened by:



\* Notepad

\* VS Code

\* Obsidian

\* Joplin

\* Git

\* other Markdown applications



\---



\# 8. Application startup flow



The startup process should be asynchronous.



```text

Application Start

&#x20;      │

&#x20;      ▼

Load Settings

&#x20;      │

&#x20;      ▼

Determine Notes Root

&#x20;      │

&#x20;      ▼

Create Services

&#x20;      │

&#x20;      ▼

Start UI

&#x20;      │

&#x20;      ├───────────────► User can interact

&#x20;      │

&#x20;      ▼

Background File Scan

&#x20;      │

&#x20;      ▼

Discover folders

&#x20;      │

&#x20;      ▼

Discover \*.md files

&#x20;      │

&#x20;      ▼

Read metadata

&#x20;      │

&#x20;      ▼

Parse Markdown

&#x20;      │

&#x20;      ├── headings

&#x20;      ├── tags

&#x20;      └── title

&#x20;      │

&#x20;      ▼

Build In-Memory Index

&#x20;      │

&#x20;      ▼

Start FileSystemWatcher

&#x20;      │

&#x20;      ▼

Ready

```



The UI should \*\*not wait for every file to be parsed\*\*.



For example:



```text

QuickNote

Opening...



Files: 428

Indexed: 271

Indexed: 428



Ready

```



\---



\# 9. Core document model



```text

NoteDocument

│

├── Id

├── FullPath

├── RelativePath

├── FileName

├── FolderPath

├── Title

├── Content

├── Headings

├── Tags

├── LastModified

├── FileSize

└── ContentHash

```



Example:



```text

Id:

&#x20;   8c1b...



FullPath:

&#x20;   C:\\QuickNote\\Work\\Cortex.md



RelativePath:

&#x20;   Work\\Cortex.md



FileName:

&#x20;   Cortex.md



Title:

&#x20;   Cortex XDR Assessment



Tags:

&#x20;   cybersecurity

&#x20;   cortex-xdr

&#x20;   soc

```



\---



\# 10. In-memory index



This is the heart of the search system.



```text

&#x20;                  In-Memory Index

&#x20;                        │

&#x20;         ┌──────────────┼──────────────┐

&#x20;         ▼              ▼              ▼

&#x20;    Documents       Headings         Tags

&#x20;         │              │              │

&#x20;         ▼              ▼              ▼

&#x20;     Dictionary      Dictionary     Dictionary

```



Example:



```text

Documents



Cortex.md → NoteDocument

SOC.md    → NoteDocument

OSPF.md   → NoteDocument

```



Tag index:



```text

cybersecurity

&#x20;   ↓

Cortex.md

SOC.md



cortex-xdr

&#x20;   ↓

Cortex.md

XQL.md



network

&#x20;   ↓

OSPF.md

Firewall.md

```



Heading index:



```text

"Cortex XDR Assessment"

&#x20;       ↓

Cortex.md / line 6



"Broker VM"

&#x20;       ↓

Cortex.md / line 31

```



\---



\# 11. Search architecture



Search should \*\*never read the filesystem for every keystroke\*\*.



Instead:



```text

User types:



cortex

&#x20; │

&#x20; ▼

SearchViewModel

&#x20; │

&#x20; ▼

SearchService

&#x20; │

&#x20; ▼

In-Memory SearchIndex

&#x20; │

&#x20; ▼

Ranking

&#x20; │

&#x20; ▼

Search Results

```



Example result:



```text

Cortex XDR Assessment

Work/Cortex.md

\# Cortex XDR Assessment



Cortex XDR

Work/Cortex.md

...Cortex XDR...



XDR Investigation

Work/XQL.md

...Cortex...

```



\---



\# 12. Search ranking



I would implement weighted ranking.



```text

Exact H1                  +100

Exact H2                   +90

Exact H3                   +80

Heading contains           +70

Filename                   +60

Folder                     +40

Tag                        +50

Body                       +20

```



Then add modifiers:



```text

Exact phrase               +20

Word starts with           +10

Recent document              +5

```



Example:



```text

Search:

cortex

```



Result:



```text

1\. Cortex XDR Assessment

&#x20;  H1 match                 100



2\. Cortex XDR Investigation

&#x20;  H2 match                  90



3\. Cortex.md

&#x20;  filename                  60



4\. SOC.md

&#x20;  body                      20

```



\---



\# 13. Search navigation



Search results need to contain location information.



```text

SearchResult

│

├── DocumentId

├── FilePath

├── MatchType

├── MatchedText

├── LineNumber

├── StartOffset

├── EndOffset

└── Score

```



Therefore:



```text

Search result clicked

&#x20;       │

&#x20;       ▼

Open document

&#x20;       │

&#x20;       ▼

Load editor

&#x20;       │

&#x20;       ▼

Scroll to line

&#x20;       │

&#x20;       ▼

Highlight match

```



This is much better than simply opening the file.



\---



\# 14. Markdown outline engine



Every document gets a heading tree.



Input:



```markdown

\# Cortex XDR Assessment



\## Executive Summary



\## Incidents



\### High Severity



\### Medium Severity



\## Broker VM



\### CPU



\### Memory



\## Recommendations

```



Internal structure:



```text

Cortex XDR Assessment

│

├── Executive Summary

│

├── Incidents

│   ├── High Severity

│   └── Medium Severity

│

├── Broker VM

│   ├── CPU

│   └── Memory

│

└── Recommendations

```



Each heading stores:



```text

Heading

├── Level

├── Text

├── LineNumber

├── StartOffset

├── EndOffset

├── Parent

└── Children

```



\---



\# 15. Right-side outline



UI:



```text

┌────────────────────┐

│ OUTLINE            │

├────────────────────┤

│ ▼ Cortex XDR       │

│   Executive        │

│ ▼ Incidents        │

│     High Severity  │

│     Medium Severity│

│ ▼ Broker VM        │

│     CPU            │

│     Memory         │

│   Recommendations  │

└────────────────────┘

```



Click:



```text

Broker VM

```



Editor:



```text

&#x20;          ↓



\## Broker VM

```



Editor scrolls automatically.



\---



\# 16. Active heading tracking



While the user scrolls:



```text

Editor scroll

&#x20;    │

&#x20;    ▼

Current cursor position

&#x20;    │

&#x20;    ▼

Find nearest preceding heading

&#x20;    │

&#x20;    ▼

Update outline

&#x20;    │

&#x20;    ▼

Highlight current heading

```



Example:



```text

OUTLINE



Cortex XDR Assessment

Executive Summary

Incidents        ← active

&#x20; High Severity

&#x20; Medium Severity

Broker VM

```



\---



\# 17. Markdown typing engine



This should be a separate component.



```text

Text Input

&#x20;   │

&#x20;   ▼

MarkdownBehaviorEngine

&#x20;   │

&#x20;   ├── OrderedListBehavior

&#x20;   ├── BulletListBehavior

&#x20;   ├── CheckboxBehavior

&#x20;   ├── QuoteBehavior

&#x20;   └── IndentationBehavior

```



\---



\# 18. Automatic numbering



User types:



```text

1\. First

```



Press Enter:



```text

1\. First

2\. |

```



Then:



```text

3\. |

```



Important edge case:



```text

99\. Item

```



becomes:



```text

100\.

```



Not:



```text

991\.

```



\---



\# 19. Bullet behavior



Input:



```text

\- First item

```



Enter:



```text

\- First item

\- |

```



Enter on empty item:



```text

\- First item

|

```



The list terminates.



\---



\# 20. Checkbox behavior



Input:



```text

\- \[ ] Task

```



Enter:



```text

\- \[ ] Task

\- \[ ] |

```



Completed checkbox:



```text

\- \[x] Task

```



Can be clicked from the UI later.



\---



\# 21. Quote behavior



Input:



```text

> Important information

```



Enter:



```text

> Important information

> |

```



Empty quote:



```text

> Important



|

```



terminates quote mode.



\---



\# 22. Tag architecture



Tags should live in Markdown front matter.



```markdown

\---

tags:

&#x20; - cybersecurity

&#x20; - soc

&#x20; - cortex-xdr

\---

```



The file remains portable.



Runtime:



```text

Markdown

&#x20;  │

&#x20;  ▼

TagParser

&#x20;  │

&#x20;  ▼

Tag Collection

&#x20;  │

&#x20;  ▼

TagIndex

```



UI:



```text

Tags



\#cybersecurity

\#cortex-xdr

\#soc

\#network

```



Click:



```text

\#cortex-xdr

```



results:



```text

Cortex.md

XQL.md

Incident.md

```



\---



\# 23. Folder architecture



Folder tree should directly represent the filesystem.



```text

FILES



▼ QuickNote

│

├── Inbox

│   ├── Today.md

│   └── Ideas.md

│

├── Work

│   ├── Cortex.md

│   └── SOC.md

│

├── Projects

│   ├── Network

│   │   ├── OSPF.md

│   │   └── Firewall.md

│   │

│   └── QuickNote

│       └── Architecture.md

│

└── Archive

```



Folder operations:



```text

New Folder

New Note

Rename

Move

Delete

Restore

Refresh

Open in Explorer

```



\---



\# 24. Folder creation



User:



```text

New Folder

```



Application:



```text

FolderService

&#x20;     │

&#x20;     ▼

Directory.CreateDirectory()

&#x20;     │

&#x20;     ▼

FileSystemWatcher

&#x20;     │

&#x20;     ▼

Update FolderTree

```



No database update is required.



\---



\# 25. New note



User clicks:



```text

\+ New Note

```



Application:



```text

Create document

&#x20;      │

&#x20;      ▼

Create .md file

&#x20;      │

&#x20;      ▼

FileWatcher event

&#x20;      │

&#x20;      ▼

Index document

&#x20;      │

&#x20;      ▼

Open editor

&#x20;      │

&#x20;      ▼

Cursor ready

```



The user can immediately type.



\---



\# 26. Rename



Rename must update the actual filesystem path.



```text

Cortex.md

&#x20;   │

&#x20;   ▼

Rename

&#x20;   │

&#x20;   ▼

Cortex-XDR.md

&#x20;   │

&#x20;   ▼

FileSystemWatcher

&#x20;   │

&#x20;   ▼

Update index

```



All open-document references need to update.



\---



\# 27. Move



Example:



```text

Work/Cortex.md

```



move to:



```text

Archive/Cortex.md

```



Flow:



```text

Move command

&#x20;   │

&#x20;   ▼

Filesystem move

&#x20;   │

&#x20;   ▼

Watcher event

&#x20;   │

&#x20;   ▼

Remove old index path

&#x20;   │

&#x20;   ▼

Add new index path

```



\---



\# 28. Delete



I recommend initially using Windows Recycle Bin semantics rather than permanent deletion.



```text

Delete

&#x20;  │

&#x20;  ▼

Recycle Bin

&#x20;  │

&#x20;  ▼

Filesystem event

&#x20;  │

&#x20;  ▼

Remove from memory index

```



This is much safer.



\---



\# 29. FileSystemWatcher



The watcher monitors:



```text

Created

Changed

Deleted

Renamed

```



Architecture:



```text

Filesystem

&#x20;   │

&#x20;   ▼

FileSystemWatcher

&#x20;   │

&#x20;   ▼

Debouncer

&#x20;   │

&#x20;   ▼

FileChangeProcessor

&#x20;   │

&#x20;   ├── Created → Index

&#x20;   ├── Changed → Reparse

&#x20;   ├── Deleted → Remove

&#x20;   └── Renamed → Update

```



\---



\# 30. Why debouncing is required



Some editors generate multiple filesystem events for one save.



For example:



```text

Changed

Changed

Changed

Changed

```



Instead of processing four times:



```text

Changed

Changed

Changed

Changed

&#x20;      ↓

&#x20; 300 ms debounce

&#x20;      ↓

&#x20;  Process once

```



\---



\# 31. External modification detection



This is critical.



Suppose QuickNote has:



```text

Cortex.md

```



open.



Another program modifies it:



```text

VS Code

&#x20;  ↓

Cortex.md

```



QuickNote detects:



```text

External modification detected.

```



Then:



```text

┌───────────────────────────────┐

│ Cortex.md was modified       │

│ outside QuickNote.           │

│                               │

│ \[Reload] \[Keep Mine] \[Compare]│

└───────────────────────────────┘

```



Do \*\*not\*\* silently overwrite the external modification.



\---



\# 32. Autosave architecture



Autosave should be debounce-based.



```text

User types

&#x20;  │

&#x20;  ▼

ContentChanged

&#x20;  │

&#x20;  ▼

Cancel previous timer

&#x20;  │

&#x20;  ▼

Start 500ms timer

&#x20;  │

&#x20;  ▼

No more typing?

&#x20;  │

&#x20;  ▼

Save

```



Example:



```text

Typing:



H

He

Hel

Hell

Hello



&#x20;       500ms silence

&#x20;             ↓

&#x20;           SAVE

```



Not:



```text

H → save

He → save

Hel → save

Hell → save

Hello → save

```



\---



\# 33. Save state



Bottom status bar:



```text

● Saved

```



During save:



```text

● Saving...

```



If save fails:



```text

⚠ Save failed

```



If modified:



```text

● Unsaved

```



\---



\# 34. Atomic save



Never simply depend on:



```text

File.WriteAllText(original)

```



A safer approach:



```text

Cortex.md

&#x20;   │

&#x20;   ├── Cortex.md.tmp

&#x20;   │

&#x20;   ▼

Write new content

&#x20;   │

&#x20;   ▼

Flush

&#x20;   │

&#x20;   ▼

Replace original

```



Conceptually:



```text

Original

&#x20;  │

&#x20;  ▼

Temporary file

&#x20;  │

&#x20;  ▼

Flush to disk

&#x20;  │

&#x20;  ▼

Atomic replace

&#x20;  │

&#x20;  ▼

New document

```



This reduces the chance of corrupting a note if the application or machine crashes during writing.



\---



\# 35. Crash recovery



Autosave protects normal operation, but recovery protects against crashes.



Runtime:



```text

Editor

&#x20; │

&#x20; ▼

RecoveryService

&#x20; │

&#x20; ▼

Temporary recovery data

```



If application crashes:



```text

Next startup

&#x20;    │

&#x20;    ▼

Find recovery data

&#x20;    │

&#x20;    ▼

Compare with actual file

&#x20;    │

&#x20;    ▼

Recovery dialog

```



Example:



```text

Recovered document found



Cortex.md



\[Recover] \[Discard]

```



After successful normal save/close:



```text

Recovery data

&#x20;    ↓

Delete

```



\---



\# 36. Recovery storage



Recovery data should \*\*not\*\* pollute the notes directory.



For example:



```text

%LOCALAPPDATA%\\QuickNote\\Recovery\\

```



Application configuration:



```text

%APPDATA%\\QuickNote\\

&#x20;   settings.json

```



Logs:



```text

%LOCALAPPDATA%\\QuickNote\\Logs\\

```



Notes:



```text

User-selected folder

```



Therefore:



```text

Notes

&#x20;   = user data



APPDATA

&#x20;   = application configuration



LOCALAPPDATA

&#x20;   = temporary/runtime data

```



\---



\# 37. No persistent index



This is one of the strongest parts of the architecture.



Do \*\*not\*\* create:



```text

search-index.json

search.db

notes.db

cache.db

```



Instead:



```text

Application starts

&#x20;       │

&#x20;       ▼

Scan filesystem

&#x20;       │

&#x20;       ▼

Build index

&#x20;       │

&#x20;       ▼

RAM

```



Application closes:



```text

RAM index disappears

```



Next startup:



```text

Filesystem

&#x20;    ↓

Rebuild index

```



This prevents index corruption and synchronization problems.



\---



\# 38. Index lifecycle



```text

&#x20;                  APPLICATION

&#x20;                      │

&#x20;            ┌─────────┴─────────┐

&#x20;            ▼                   ▼

&#x20;       Initial Scan        File Watcher

&#x20;            │                   │

&#x20;            ▼                   ▼

&#x20;       Parse Files          Detect Change

&#x20;            │                   │

&#x20;            └────────┬──────────┘

&#x20;                     ▼

&#x20;                Index Manager

&#x20;                     │

&#x20;                     ▼

&#x20;                  RAM Index

```



\---



\# 39. Threading architecture



Don't perform filesystem scanning on the UI thread.



```text

UI THREAD

│

├── Typing

├── Cursor

├── Scroll

├── Selection

└── Rendering



BACKGROUND

│

├── Initial filesystem scan

├── Markdown parsing

├── Search index update

├── File watcher processing

├── Autosave I/O

├── Recovery

└── Logging

```



\---



\# 40. Thread safety



Because the index can be modified while searching:



```text

FileWatcher

&#x20;    │

&#x20;    ▼

Index update

```



while:



```text

Search

&#x20;    │

&#x20;    ▼

Index read

```



You need safe access.



For the first implementation:



```text

ReaderWriterLockSlim

```



or immutable index snapshots.



I prefer eventually:



```text

ImmutableDictionary

```



for search snapshots because search can operate against a consistent snapshot while the watcher builds a new one.



\---



\# 41. UI architecture



The main window should be simple.



```text

┌──────────────────────────────────────────────────────────────┐

│ 🔍 Search...                         + New Note    ⚙          │

├──────────────┬────────────────────────────────┬──────────────┤

│              │                                │              │

│ FILES        │ EDITOR                         │ OUTLINE      │

│              │                                │              │

│ ▼ Inbox      │ # Cortex XDR Assessment        │ Cortex XDR   │

│   Today.md   │                                │   Executive  │

│   Ideas.md   │ ## Executive Summary           │   Incidents  │

│              │                                │     High     │

│ ▼ Work       │ text...                        │     Medium   │

│   Cortex.md  │                                │   Broker VM  │

│   SOC.md     │ ## Incidents                   │     CPU      │

│              │                                │     Memory   │

│ ▼ Projects  │                                │              │

│              │                                │              │

├──────────────┴────────────────────────────────┴──────────────┤

│ ● Saved             Cortex.md                1,247 words     │

└──────────────────────────────────────────────────────────────┘

```



\---



\# 42. Three main panels



\### Left



Filesystem/navigation:



```text

Folders

Notes

Tags

Favorites

```



\### Center



Actual Markdown editor.



\### Right



Document outline.



This is the most important UI structure.



\---



\# 43. Search UI



Search should always be visible.



```text

┌───────────────────────────────────────────────────┐

│ 🔍 Search notes...                                │

└───────────────────────────────────────────────────┘

```



Typing:



```text

cortex

```



shows:



```text

SEARCH RESULTS



Cortex XDR Assessment

Work/Cortex.md

H1 match



Cortex XDR Investigation

Work/XQL.md

H2 match



Cortex.md

Work/Cortex.md

Filename

```



\---



\# 44. Optional search syntax



Normal search remains the default.



Later:



```text

tag:cortex-xdr

```



or:



```text

folder:Work

```



or:



```text

title:cortex

```



or:



```text

tag:soc cortex

```



But this should be \*\*optional\*\*.



The user should not need to learn a query language.



\---



\# 45. Navigation architecture



A central navigation service avoids random ViewModels controlling each other.



```text

Search result

&#x20;    │

&#x20;    ▼

NavigationService

&#x20;    │

&#x20;    ├── Open document

&#x20;    ├── Position cursor

&#x20;    ├── Scroll

&#x20;    └── Highlight

```



Outline uses the same service:



```text

Outline click

&#x20;    │

&#x20;    ▼

NavigationService

&#x20;    │

&#x20;    ▼

Editor

&#x20;    │

&#x20;    ▼

Heading position

```



\---



\# 46. Open document manager



If multiple files are open:



```text

Cortex.md ×

SOC.md ×

OSPF.md ×

```



Internally:



```text

DocumentManager

│

├── OpenDocuments

├── ActiveDocument

└── DocumentStates

```



Each open document has:



```text

OpenDocumentState

├── DocumentId

├── EditorContent

├── CursorPosition

├── Selection

├── ScrollPosition

├── IsDirty

└── LastSavedHash

```



\---



\# 47. Difference between index and open documents



Very important.



The index can contain:



```text

10,000 notes

```



But only:



```text

2 or 3 documents

```



need to be actively loaded into the editor.



Therefore:



```text

Filesystem

&#x20;   │

&#x20;   ▼

Index

&#x20;   │

&#x20;   ├── 10,000 metadata records

&#x20;   │

&#x20;   └── 3 active editors

```



This keeps memory usage reasonable.



\---



\# 48. Performance architecture



For a large note collection:



```text

10,000 Markdown files

```



Startup:



```text

Filesystem enumeration

&#x20;      ↓

Metadata

&#x20;      ↓

Parallel parsing

&#x20;      ↓

Build index

```



Search:



```text

RAM

&#x20;↓

very fast

```



Open document:



```text

Disk

&#x20;↓

load one file

```



Save:



```text

Disk

&#x20;↓

one file

```



You don't need a database for this architecture.



\---



\# 49. Search index optimization



Don't tokenize everything unnecessarily in v1.



Start with:



```text

Dictionary<DocumentId, NoteDocument>

Dictionary<string, List<DocumentId>> Tags

Dictionary<string, List<HeadingReference>> Headings

```



Then search:



```text

title

filename

folder

heading

body

tags

```



Only optimize further if profiling shows a problem.



\---



\# 50. Markdown parser architecture



Use Markdig for full Markdown parsing.



But don't rely exclusively on Markdig for editor behaviors.



Separate:



```text

Markdig

&#x20;   ↓

Markdown AST / rendering



Custom parser

&#x20;   ↓

Heading metadata

Tag metadata

Editor behavior

```



This keeps the editor behavior under your control.



\---



\# 51. Editor behavior pipeline



```text

Keyboard Event

&#x20;      │

&#x20;      ▼

EditorBehaviorManager

&#x20;      │

&#x20;      ├── Enter

&#x20;      │    ├── Ordered List

&#x20;      │    ├── Bullet List

&#x20;      │    ├── Checkbox

&#x20;      │    └── Quote

&#x20;      │

&#x20;      ├── Tab

&#x20;      │    └── Indentation

&#x20;      │

&#x20;      └── Backspace

&#x20;           └── List handling

```



This should be modular.



\---



\# 52. SOLID design



\### Single Responsibility



Don't create:



```text

NoteService

```



that does everything.



Instead:



```text

DocumentService

FolderService

SearchService

TagService

AutosaveService

RecoveryService

```



Each has one responsibility.



\---



\### Open/Closed



Adding:



```text

new Markdown behavior

```



should not require rewriting the entire editor.



For example:



```text

IMarkdownBehavior

```



Implement:



```text

OrderedListBehavior

BulletListBehavior

CheckboxBehavior

QuoteBehavior

```



\---



\### Dependency Inversion



Core should depend on:



```text

INoteRepository

IFileWatcher

IRecoveryService

```



not:



```text

File.WriteAllText()

```



directly.



\---



\# 53. Error handling



Errors should not crash the application.



Example:



```text

Save failed

&#x20;    │

&#x20;    ▼

Log error

&#x20;    │

&#x20;    ▼

Keep editor content in RAM

&#x20;    │

&#x20;    ▼

Show warning

```



The user's text must not disappear simply because disk I/O failed.



\---



\# 54. Logging



Example:



```text

2026-09-11 12:01:23 INFO  Application started

2026-09-11 12:01:23 INFO  Notes root: C:\\QuickNote

2026-09-11 12:01:24 INFO  Scanning filesystem

2026-09-11 12:01:25 INFO  Discovered 428 markdown files

2026-09-11 12:01:25 INFO  Index built

2026-09-11 12:02:11 INFO  Opened Work\\Cortex.md

2026-09-11 12:02:14 INFO  Saved Work\\Cortex.md

```



Errors:



```text

ERROR Save failed: Work\\Cortex.md

ERROR Access denied

```



\---



\# 55. Configuration



Only application settings go into JSON.



Example:



```json

{

&#x20; "notesRoot": "C:\\\\Users\\\\User\\\\Documents\\\\QuickNote",

&#x20; "theme": "system",

&#x20; "fontSize": 15,

&#x20; "autosaveDelayMs": 500,

&#x20; "showOutline": true,

&#x20; "showTags": true,

&#x20; "restoreOpenDocuments": true

}

```



This is \*\*not\*\* a note database.



\---



\# 56. What should NOT be persisted



Do not persist:



```text

Search index

Heading index

Tag index

Folder tree cache

Parsed Markdown AST

Search results

```



These are disposable.



```text

Filesystem

&#x20;   ↓

Truth

```



\---



\# 57. What should be persisted



Only:



```text

Markdown files

Folders

Application settings

Optional recovery files

```



Potentially later:



```text

Theme

Window size

Panel width

Last opened folder

```



\---



\# 58. Startup recovery



Startup sequence should become:



```text

Launch

&#x20;│

&#x20;├── Load settings

&#x20;│

&#x20;├── Detect recovery files

&#x20;│

&#x20;├── Initialize UI

&#x20;│

&#x20;├── Scan filesystem

&#x20;│

&#x20;├── Build index

&#x20;│

&#x20;└── Start watcher

```



If recovery exists:



```text

Recovery available



Cortex.md

Last edited: 2 minutes ago



\[Recover] \[Discard]

```



\---



\# 59. Security considerations



Because this is a local application:



\* Don't execute Markdown.

\* Don't execute embedded HTML/JavaScript.

\* Sanitize rendered HTML if preview is added.

\* Restrict file operations to the configured notes root where appropriate.

\* Protect against path traversal from future import features.

\* Don't follow symbolic links blindly if you later add recursive workspace scanning.

\* Don't automatically execute external links/files.

\* Treat front matter as untrusted text.



\---



\# 60. Future Markdown preview



Don't make WYSIWYG the initial architecture.



Initial:



```text

SOURCE EDITOR

```



Later:



```text

┌──────────────────────┬──────────────────────┐

│ Markdown Source      │ Preview              │

│                      │                      │

│ # Hello              │ Hello                │

│                      │                      │

│ \*\*Important\*\*        │ Important            │

└──────────────────────┴──────────────────────┘

```



The core architecture doesn't need to change.



\---



\# 61. Future features



The architecture leaves room for:



```text

Markdown preview

Wiki links

\[\[Cortex XDR]]

Backlinks

Favorites

Recent notes

Pinned notes

Global hotkey

Command palette

Themes

Keyboard navigation

Drag/drop

Attachments

Images

PDF attachments

Export

Import

Git integration

Cloud sync

Plugins

```



But \*\*do not implement these in the first version\*\*.



\---



\# 62. Recommended MVP



I would build the first usable version in this order.



\### Phase 1 — Filesystem



```text

Open notes folder

Folder tree

New folder

New note

Rename

Move

Delete

Open Markdown

Save Markdown

```



\### Phase 2 — Editor



```text

Markdown editor

Autosave

Status

Ordered lists

Bullet lists

Checkboxes

Quotes

```



\### Phase 3 — Intelligence



```text

Heading parser

Outline

Active heading

Tags

```



\### Phase 4 — Search



```text

In-memory index

Filename search

Title search

Heading search

Body search

Tag search

Ranking

Jump to match

```



\### Phase 5 — Reliability



```text

Atomic save

Crash recovery

External modification detection

FileSystemWatcher

Debouncing

```



\### Phase 6 — Polish



```text

Tabs

Themes

Favorites

Recent documents

Settings

Keyboard accelerators

Installer

```



\---



\# 63. Final architecture



The complete system can be summarized as:



```text

&#x20;                        ┌─────────────────────┐

&#x20;                        │      WPF UI         │

&#x20;                        │                     │

&#x20;                        │ Search              │

&#x20;                        │ Folder Tree         │

&#x20;                        │ Markdown Editor     │

&#x20;                        │ Outline             │

&#x20;                        │ Tags                │

&#x20;                        └──────────┬──────────┘

&#x20;                                   │

&#x20;                                   ▼

&#x20;                        ┌─────────────────────┐

&#x20;                        │   ViewModels /      │

&#x20;                        │   Application       │

&#x20;                        │   Services          │

&#x20;                        └──────────┬──────────┘

&#x20;                                   │

&#x20;                                   ▼

&#x20;      ┌─────────────────────────────────────────────────┐

&#x20;      │                    CORE                          │

&#x20;      │                                                 │

&#x20;      │ Document Model                                  │

&#x20;      │ Markdown Engine                                 │

&#x20;      │ Markdown Behaviors                              │

&#x20;      │ Search Engine                                   │

&#x20;      │ Search Ranking                                  │

&#x20;      │ Outline Engine                                 │

&#x20;      │ Tag Engine                                     │

&#x20;      │ Navigation                                     │

&#x20;      └──────────────────────┬──────────────────────────┘

&#x20;                             │

&#x20;                             ▼

&#x20;      ┌─────────────────────────────────────────────────┐

&#x20;      │              INFRASTRUCTURE                     │

&#x20;      │                                                 │

&#x20;      │ File Repository                                │

&#x20;      │ File Scanner                                   │

&#x20;      │ Atomic Writer                                  │

&#x20;      │ FileSystemWatcher                              │

&#x20;      │ Recovery                                       │

&#x20;      │ Configuration                                  │

&#x20;      │ Logging                                        │

&#x20;      └──────────────────────┬──────────────────────────┘

&#x20;                             │

&#x20;                             ▼

&#x20;      ┌─────────────────────────────────────────────────┐

&#x20;      │                  FILESYSTEM                     │

&#x20;      │                                                 │

&#x20;      │ QuickNote/                                     │

&#x20;      │ ├── Inbox/                                     │

&#x20;      │ ├── Work/                                      │

&#x20;      │ ├── Projects/                                  │

&#x20;      │ └── Archive/                                   │

&#x20;      │                                                 │

&#x20;      │ \*.md                                            │

&#x20;      └─────────────────────────────────────────────────┘

```



\## The key design principle



```text

&#x20;                ┌───────────────────────┐

&#x20;                │      FILESYSTEM       │

&#x20;                │                       │

&#x20;                │ Markdown + Folders    │

&#x20;                └───────────┬───────────┘

&#x20;                            │

&#x20;                      Source of Truth

&#x20;                            │

&#x20;                            ▼

&#x20;                ┌───────────────────────┐

&#x20;                │     IN-MEMORY INDEX   │

&#x20;                │                       │

&#x20;                │ Search                │

&#x20;                │ Headings              │

&#x20;                │ Tags                  │

&#x20;                │ Folder tree           │

&#x20;                └───────────┬───────────┘

&#x20;                            │

&#x20;                      Disposable

&#x20;                            │

&#x20;                            ▼

&#x20;                ┌───────────────────────┐

&#x20;                │       WPF UI          │

&#x20;                └───────────────────────┘

```



That gives you the \*\*Notepad simplicity\*\* you want while still providing the useful parts of \*\*Notion/Obsidian\*\* without forcing the user to learn a complicated application or maintain a database.



The next implementation step should be to turn this architecture into a \*\*complete Visual Studio/.NET solution with the `.sln`, three projects, NuGet packages, interfaces, models, DI setup, and the first working WPF UI\*\*, then implement the features phase-by-phase rather than trying to write the entire application as one giant class.



