# Asset Tags

![INDiEA Asset Tags](Resources/Asset_Tags_Cover_Image.png)

---

© 2025-2026 **INDiEA Games**. All rights reserved.

---

## Introduction

![Asset Tags Search and Filter](Resources/Screenshot_1.png)

**Asset Tags** is a Unity editor extension designed to create a dedicated classification layer separate from Unity Asset Labels, while still allowing interoperability when needed. It helps teams assign tags to assets, search them quickly in the Project Browser, and maintain collaboration-friendly metadata with JSON persistence.

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
- [0Harmony](https://github.com/pardeike/Harmony): Search / toolbar integration may rely on **Lib.Harmony** and Unity editor internals; a future Unity version might require an updated asset release if those APIs change.

---

## Quick Start

1. Import the package into your Unity project.
2. Let scripts compile, then open the Project Browser.
3. Assign tags to assets from the Asset Tags UI.
4. Search with `tag:<keyword>` (for example, `tag:ui`, `tag:vfx`, `tag:all`).

---

## Key Features

- **Multi-tag assignment** per asset GUID.
- **Search integration** with `tag:` syntax in Project Browser / search provider.
- **Tag list management** (name, color, order).
- **Per-tag metadata tracking** for tag definitions, asset links, ordering, and hidden tag records.
- **Asset Tags / Labels conversion tools** for interoperability.

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

When multiple developers edit tags at the same time, teams usually face three problems:

1. **Frequent merge conflicts** when everyone writes to one shared tag file  
2. **Accidental overwrite** of someone else’s edits during rebase/merge  
3. **Hard-to-trace changes** when there is no per-tag modification metadata  

Asset Tags addresses these issues with a **local-first + metadata-aware merge** structure.

### 1) Local editable files (Git-trackable)

Each client writes only to its own local JSON files:

- `Assets/INDiEA/Asset Tags/Data/AssetTagsData_<clientId>.json`
- `Assets/INDiEA/Asset Tags/Data/AssetTagsList_<clientId>.json`

This reduces direct write contention between users and keeps edits scoped per client.

### 2) Global cache as generated runtime state

At runtime, the system also reads cache files from:

- `Library/INDiEA/Asset Tags/Data/AssetTagsData.json`
- `Library/INDiEA/Asset Tags/Data/AssetTagsList.json`
- `Library/INDiEA/Asset Tags/Data/ClientId.json`

These files are generated from the local JSON files and are not the normal save target for user edits.

### 3) Merge behavior

The in-memory state is rebuilt from all client JSON files:

- tag definitions use `tagId`, `tagUpdatedAt`, and `tagUpdatedBy`
- asset-tag links use `tagId`, `linkUpdatedAt`, and `linkUpdatedBy`
- tag ordering uses `orderKey`, `orderUpdatedAt`, and `orderUpdatedBy`
- hidden/deleted tag records are stored in `hiddenTags`
- save operations write to the current client's local JSON only

If `mergeDeletedTagRecords` is enabled, newer `hiddenTags` records can hide older tag definitions and asset-tag links from other clients during merge. In short, this model minimizes team collisions while preserving each user’s latest intent in their own editable source files.

---

## Asset Tags Settings

| Setting | Type | Default | Description |
| ------- | ---- | ------- | ----------- |
| `overrideProjectBrowserToolbar` | bool | `true` | Replaces Unity Project Browser toolbar behavior with Asset Tags toolbar integration. |
| `indexingSearchAfterTagChanges` | bool | `true` | Reindexes Project Browser search results after Asset Tags data changes. |
| `mergeDeletedTagRecords` | bool | `true` | Applies `hiddenTags` from all clients during merge so newer removals can hide older tag data. |
| `enableDebugLogs` | bool | `true` | Enables extra debug logs to help inspect toolbar/search and data flow behavior. |

---

| Button | Description |
| ------ | ----------- |
| `Save Current Snapshot to Local Data` | Writes the current merged Asset Tags snapshot to the current client's local JSON files. |
| `Clear Current Local Data` | Hides all currently known Asset Tags for this client, then clears this client's local tag assignments and tag list entries (with confirmation dialog). |
| `Convert All Asset Tags To Asset Labels` | Copies Asset Tags into Unity Asset Labels for all project assets (with confirmation dialog). |
| `Convert All Asset Labels To Asset Tags` | Imports Unity Asset Labels into Asset Tags data and tag list (with confirmation dialog). |

---

## Contact & Support

- **GitHub:** [github.com/INDiEA-Games/Asset-Tags](https://github.com/INDiEA-Games/Asset-Tags)
- **Discord:** [discord.gg/53FQb6dbFd](https://discord.gg/53FQb6dbFd)
- **Email:** [indiea.games.dev@gmail.com](mailto:indiea.games.dev@gmail.com)
