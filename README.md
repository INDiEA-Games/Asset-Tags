# Asset Tags

![INDiEA Asset Tags](Resources/Asset_Tags_Cover_Image.png)

---

© 2025-2026 **INDiEA Games**. All rights reserved.

---

## Introduction

![Asset Tags Search and Filter](Resources/Screenshot_1.png)

**Asset Tags** adds a fast, visual tagging workflow to the Unity Project Browser. It keeps its tags separate from Unity Asset Labels, so you can organize assets with your own colored tags without changing the labels already used by your project.

Use it to classify assets by type, purpose, status, owner, workflow stage, or any category that folders alone cannot express well. With simple `tag:` searches, editable colors, and custom ordering, Asset Tags makes project content easier to find and easier to recognize.

---

## Overview

| Item    | Details |
| ------- | ------- |
| Name    | INDiEA Asset Tags |
| Version | `1.1.0` |
| Unity   | **2021.2** or newer |
| Scope   | Editor extension for Project Browser tagging and search |

---

## Requirements

| Symbol | Meaning |
| ------ | ------- |
| **O**  | Verified |
| **X**  | Not verified |
| **△**  | Partially verified |

---

| Version     | Built-In | URP | HDRP |
| ----------- | -------- | --- | ---- |
| 2021.3.0f1  | O        | O   | O    |
| 2022.3.0f1  | O        | O   | O    |
| 6000.0.23f1 | O        | O   | O    |

- Runtime: Editor-only (`#if UNITY_EDITOR`)
- [0Harmony](https://github.com/pardeike/Harmony): Search and toolbar integration may use **Lib.Harmony** and Unity editor internals. If Unity changes those internals in a future version, an Asset Tags update may be required.

---

## Quick Start

1. Import the package into your Unity project.
2. Let scripts compile, then open the Project Browser.
3. Assign tags to assets from the Asset Tags UI.
4. Search with `tag:<keyword>` (for example, `tag:ui`, `tag:vfx`, `tag:all`).

---

## Key Features

- **Multiple tags per asset**: Add more than one tag to the same asset, so a single item can belong to several categories at once.
- **Quick Asset Tags popup**: Open the popup from the Project Browser to assign tags, rename them, change colors, reorder the list, or remove unused tags.
- **Visual tag UI**: Show tags directly in the Project Browser as clear visual elements instead of plain text-only metadata.
- **Indexed tag search**: Use `tag:<keyword>` in Project Browser search, or `tag:all` to show every tagged asset.
- **Separate tag layer**: Keep Asset Tags separate from Unity Asset Labels, with conversion tools available when you need them.

---

## Search Syntax

| Query | Description |
| ----- | ----------- |
| `tag:<keyword>` | Case-insensitive partial match |
| `tag:all` | Returns all tagged assets |
| `AssetTags\<keyword>` | Legacy syntax (supported) |
| `AssetTags\all` | Legacy all query |

---

## Team Collaboration Strategy

Asset Tags stores editable tag data in client-specific JSON files. This helps reduce direct file conflicts when multiple people work in the same project:

- `Assets/INDiEA/Asset Tags/Data/AssetTagsData_<clientId>.json`
- `Assets/INDiEA/Asset Tags/Data/AssetTagsList_<clientId>.json`

The merged tag view is rebuilt automatically from those files. User edits are saved to the current client's local data, and generated cache files are kept under `Library/INDiEA/Asset Tags/Data`.

---

## Asset Tags Settings

| Setting | Type | Default | Description |
| ------- | ---- | ------- | ----------- |
| `overrideProjectBrowserToolbar` | bool | `true` | Enables the Asset Tags toolbar integration in the Project Browser. |
| `indexingSearchAfterTagChanges` | bool | `true` | Updates the search index after tag changes. |
| `mergeDeletedTagRecords` | bool | `true` | Applies tag removal records from other clients during merge. |
| `enableDebugLogs` | bool | `true` | Prints extra logs for troubleshooting toolbar, search, and data behavior. |

---

| Button | Description |
| ------ | ----------- |
| `Save Current Snapshot to Local Data` | Saves the current Asset Tags snapshot to this client's local JSON files. |
| `Clear Current Local Data` | Hides all currently known tags for this client, then clears this client's local tag data (with confirmation dialog). |
| `Convert All Asset Tags To Asset Labels` | Copies Asset Tags into Unity Asset Labels for all project assets (with confirmation dialog). |
| `Convert All Asset Labels To Asset Tags` | Imports Unity Asset Labels into Asset Tags data (with confirmation dialog). |

---

## Contact & Support

- **GitHub:** [github.com/INDiEA-Games/Asset-Tags](https://github.com/INDiEA-Games/Asset-Tags)
- **Discord:** [discord.gg/53FQb6dbFd](https://discord.gg/53FQb6dbFd)
- **Email:** [indiea.games.dev@gmail.com](mailto:indiea.games.dev@gmail.com)
