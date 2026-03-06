# MeshViewer3D — Documentation

> Version 3.0 — WoW 3.3.5a (build 12340)

---

## Table of Contents

1. [Overview](#overview)
2. [Requirements](#requirements)
3. [Getting Started](#getting-started)
4. [Camera Controls](#camera-controls)
5. [Editing Modes](#editing-modes)
6. [Blackspots](#blackspots)
7. [Jump Links (OffMesh Connections)](#jump-links)
8. [Convex Volumes](#convex-volumes)
9. [WMO 3D Visualization](#wmo-3d-visualization)
10. [Menus](#menus)
11. [UI Panels](#ui-panels)
12. [Keyboard Reference](#keyboard-reference)
13. [File Formats](#file-formats)
14. [Coordinate System](#coordinate-system)
15. [Color Reference](#color-reference)
16. [Architecture Decisions](#architecture-decisions)
17. [Feature Comparison with Tripper.Renderer](#feature-comparison)
18. [Limitations & Planned Features](#limitations)
19. [Troubleshooting](#troubleshooting)

---

## Overview

MeshViewer3D is a 3D navigation mesh viewer and editor for WoW 3.3.5a, recreating the functionality of Honorbuddy's **Tripper.Renderer**. It loads Detour/Recast `.mmtile` navigation tiles and provides tools to create, edit, and serialise navigation data: blackspots, jump links, and convex volumes.

The application also reads WoW MPQ archives directly (pure C#, no native DLLs) to display WMO building geometry and M2 doodad bounding geometry in the 3D scene, giving spatial context when editing navigation data.

---

## Requirements

| Requirement | Minimum |
|-------------|---------|
| OS | Windows 10 or later |
| Runtime | .NET 6.0 |
| GPU | OpenGL 3.3+ |
| WoW data (optional) | WoW 3.3.5a `Data/` folder with MPQ archives (for WMO visualization) |

---

## Getting Started

```
cd MeshViewer3D
dotnet run
```

1. **Map > Load Tile** — select a single `.mmtile` file, or **Load Folder** to load all tiles in a directory.
2. The navmesh appears as colored polygons. Use middle-mouse to orbit, right-mouse to pan, scroll to zoom.
3. Press **B**, **J**, or **V** to enter an editing mode. Press **Q** or **Escape** to return to navigation.
4. Save your work via **Mesh** menu or **Ctrl+S**.

---

## Camera Controls

| Action | Input |
|--------|-------|
| Orbit | Middle mouse drag or Left mouse drag |
| Pan | Right mouse drag |
| Zoom | Scroll wheel |
| Reset camera | `R` or `Home` |
| Focus selection | `F` |
| Go to coordinates | `G` (opens dialog) |

---

## Editing Modes

MeshViewer3D has four modes. Only one is active at a time.

| Mode | Activation | What it does |
|------|------------|-------------|
| **Navigation** (default) | `Q` | Camera movement and selection only |
| **Blackspot Placement** | `B` | Click on terrain to create cylindrical no-go zones |
| **Jump Link Placement** | `J` | Click start point, then end point to create an OffMesh connection |
| **Volume Placement** | `V` | Click vertices on terrain, `Enter` to finalize polygon |

Press **Escape** to cancel the current operation without leaving the mode.
Press **Q** to return to Navigation mode.

The HUD overlay (top-left) shows the active mode, e.g. `[CLICK MODE - Place Blackspot]`.

---

## Raytrace Mode

Raytrace mode provides real-time cursor inspection of the navmesh. When active, the application performs a ray-triangle intersection test from the camera through the mouse cursor into the scene on every mouse move.

### Usage
1. Click the **Raytrace** button in the toolbar (toggles ON/OFF).
2. Move the mouse over the navmesh.
3. A **colored 3D cross marker** appears at the exact hit point (RGB axis lines + yellow outer cross for visibility).
4. The HUD overlay shows:
   - **WoW coordinates** of the hit point
   - **Polygon index** in the navmesh
   - **AreaType name and ID** (e.g. `Ground (1)`, `Water (8)`)

### Technical details
- Ray casting uses the Möller-Trumbore algorithm (double-sided) via `RayCaster.RayTriangleIntersect`.
- Every navmesh polygon is tested (fan triangulation), closest hit wins.
- The marker is drawn with depth test disabled so it is always visible.
- Buffer uses `DynamicDraw` since it updates every frame.
- When mode is OFF, all raytrace state and GPU buffers are cleared.

---

## Test Navigation (A→B Pathfinding)

Interactive two-click pathfinding on the loaded NavMesh. Computes and displays the shortest walkable path between two points.

### Usage
1. Toggle the **Test Navigation** button in the toolbar (cursor becomes crosshair).
2. **Left-click** on the mesh to place the **start point** (green cross marker).
3. **Left-click** again to place the **end point** (red cross marker) — the A\* path is computed immediately.
4. The resulting path is drawn as a yellow-to-orange line strip on the mesh surface.
5. Click again to reset and place a new start point.
6. Toggle the button OFF to clear all markers and path state.

### Technical Details
- **Algorithm**: A\* search on the Detour polygon adjacency graph (`Neis[]` neighbor array).
- **Funnel Algorithm**: After A\* finds the polygon corridor, the Simple Stupid Funnel Algorithm (Mikko Mononen) produces the shortest smooth path through portal edges — no more zigzag edge-midpoint waypoints.
- **Cross-tile navigation**: `Merge()` reconnects external edges (0x8000) between tiles by matching vertex positions (epsilon=0.01f), enabling seamless multi-tile pathfinding.
- **Off-mesh connections**: A\* also traverses off-mesh connections (jump links) stored in the tile data. `BuildOffMeshLookup()` maps polygons to their off-mesh targets, and `FindClosestPoly()` resolves endpoint polygons using point-in-polygon (ray-casting on XZ plane) with Y-bounds validation, falling back to centroid distance.
- **Portal direction**: Off-mesh portal entry/exit is resolved via dual-centroid distance comparison (from-poly and to-poly centroids against both connection endpoints).
- **Priority queue**: .NET 6 `PriorityQueue<int, float>` — polygon index keyed by `gCost + heuristic`.
- **Heuristic**: Euclidean distance between polygon centroids.
- **Edge cost**: Euclidean distance between shared-edge midpoints, with a 2× penalty for non-Ground area types.
- **Portals**: Built as (left, right) edge endpoint pairs; off-mesh connections use degenerate portals (both endpoints at the connection position).
- **Rendering**: Yellow-orange gradient line strip (`GL_LINES`), `LineWidth = 4.0`, `DepthTest` disabled, vertices offset `Y + 0.3f` above the mesh surface.
- **Markers**: Green cross (start) and red cross (end), same offset and rendering settings.
- **HUD overlay**: Displays `[TEST NAV]` with start coordinates, waypoint count, or "click start/end" prompts.
- **Console output**: Path distance, waypoint count, and computation time (via `Stopwatch`).
- **Cleanup**: Toggling OFF or closing a tile calls `ClearTestNavState()` which resets all fields and GPU buffers.

---

## NavMesh Analysis Tools

Analyzes the loaded navmesh for structural quality: connected components (isolated islands) and degenerate polygons.

### Usage
1. Press `A` or use **Tools > Analyze NavMesh (A)** menu.
2. The console displays a report: number of connected components, their sizes, isolated polygons, and any degenerate polys.
3. The color mode switches to **By Component** — each connected region is colored differently.
4. Also available via **View > Color Mode > By Component (Analysis)**.

### Technical Details
- **Connected Components**: BFS flood-fill on polygon adjacency graph. Each connected region gets a unique ID.
- **Degenerate Detection**: Uses Newell's method for polygon area/normal. Flags polygons with area < 0.001 or inverted normals (Y < 0).
- **Color Palette**: 12-color cycling palette for visual distinction. Isolated single-polygon islands are immediately visible.
- **Implementation**: `Core/NavMeshAnalyzer.cs` — `FindConnectedComponents()`, `FindDegeneratePolygons()`, `GetAnalysisReport()`, `GenerateComponentColors()`.

---

## Export Paths

Export computed navigation paths in three formats for external use.

### Formats
- **JSON**: Array of waypoints with X/Y/Z coordinates, total distance, coordinate system metadata.
- **CSV**: `Index,X,Y,Z` columns — simple tabular format for analysis.
- **HB Hotspot XML**: Honorbuddy profile-compatible XML with `<Hotspots>` section. Coordinates are converted from Detour to WoW coordinate space.

### Usage
1. Compute a path using Test Navigation mode (place start + end).
2. Use **Tools > Export Path (JSON/CSV/HB Hotspot XML)...** menu.
3. A save dialog opens — choose location and filename.

### Safety
- **NaN/Infinity validation**: Paths containing NaN or Infinity waypoints are rejected before export.
- **Error handling**: File I/O exceptions (disk full, permissions) are caught and reported via dialog + console log.

---

## Undo/Redo

Full undo/redo support for all editing operations using the Command Pattern.

### Supported Operations
| Operation | Undo Effect |
|-----------|-------------|
| Add blackspot (click) | Removes the added blackspot |
| Delete blackspot | Re-inserts the blackspot at its original index |
| Move blackspot (drag) | Reverts to the original position |
| Resize blackspot (Shift/Ctrl+Scroll) | Restores previous radius/height |
| Finalize convex volume | Removes the volume |
| Edit volume properties (Apply) | Restores previous AreaType/MinHeight/MaxHeight |
| Create jump link | Removes the off-mesh connection |

### Shortcuts
| Key | Action |
|-----|--------|
| `Ctrl+Z` | Undo last operation |
| `Ctrl+Y` / `Ctrl+Shift+Z` | Redo last undone operation |
| `Ctrl+N` | Clear all + clear undo history |

### Technical Details
- **Pattern**: Command Pattern — `IEditCommand` interface with `Execute()` and `Undo()` methods.
- **Manager**: `UndoRedoManager` with two stacks (undo + redo). New actions clear the redo stack.
- **Commands**: `AddBlackspotCommand`, `RemoveBlackspotCommand`, `MoveBlackspotCommand`, `ResizeBlackspotCommand`, `AddVolumeCommand`, `RemoveVolumeCommand`, `EditVolumePropertiesCommand`, `AddOffMeshCommand`, `RemoveOffMeshCommand`.
- **Stack cap**: Maximum 100 undo operations. Oldest entries are evicted when exceeded.
- **Implementation**: `Core/UndoRedoManager.cs`, `Core/Commands/EditCommands.cs`.

---

## Blackspots

Blackspots are cylindrical no-go zones where a bot must never path. Used to mark bugged geometry, dangerous mob clusters, or unreachable areas.

### Creating

1. Press `B` to enter placement mode (cursor changes to crosshair).
2. Click on the navmesh surface — a red cylinder appears with a green flash (0.8 s).
3. The mode returns to navigation automatically after each placement.

### Editing

- **Select**: click on a blackspot in the 3D view or in the side panel list.
- **Move**: drag a selected blackspot across the navmesh surface.
- **Resize radius**: `Shift + Scroll Wheel` (±1 per notch, range 1–500).
- **Resize height**: `Ctrl + Scroll Wheel` (±1 per notch, range 1–200).
- **Edit properties**: change Name, X, Y, Z, Radius, Height in the panel, then click **Apply Changes**.
- **Delete**: select then press `Delete`, or click **Remove** in the panel.
- **Clear all**: click **Clear** in the panel (confirms before deleting).

### Visualization

| State | Color |
|-------|-------|
| Normal | Red, alpha 0.4 |
| Selected | Yellow/orange, alpha 0.4 |
| Just created (0.8 s) | Green pulse |

Cylindrical geometry: 24 segments, top/bottom caps.

### File Format

XML, compatible with Honorbuddy `.blackspot` format:

```xml
<Blackspots>
  <Blackspot X="1234.56" Y="7890.12" Z="345.67"
             Radius="10.00" Height="20.00" Name="Zone" MapId="0" />
</Blackspots>
```

Coordinates are in **WoW world space**. `MapId` is optional (defaults to `0` for backward compatibility).

---

## Jump Links

Jump links are custom OffMesh connections — manual navigation edges between two points. Typically used for jumps, drops, or shortcuts that Recast cannot generate automatically.

### Creating

1. Press `J` to enter jump link mode.
2. Click the **start point** on the navmesh.
3. Click the **end point** — the connection is created.
4. Toggle bidirectional/unidirectional in the panel properties.

### Visualization

| Type | Color |
|------|-------|
| Bidirectional | Cyan lines |
| Unidirectional | Orange lines |

Jump links from the mmtile data (read-only) are also displayed.

### File Formats

**XML:**
```xml
<OffMeshConnections Version="1.0">
  <Connection StartX="..." StartY="..." StartZ="..."
              EndX="..." EndY="..." EndZ="..."
              Radius="1.00" Bidirectional="True" />
</OffMeshConnections>
```

**Binary (`.offmesh`):** 36 bytes per connection — matches Detour's `dtOffMeshConnection` layout.

**CSV export:** available for spreadsheet analysis.

---

## Convex Volumes

Convex volumes are polygonal areas with a custom area type and height range. They override the navmesh area type within their bounds — useful for marking water zones, blocked areas, or custom regions.

### Creating

1. Press `V` to enter volume placement mode.
2. Click on the navmesh to place vertices (minimum 3). A live preview polygon is drawn.
3. Press `Enter` to finalize the volume.
4. Press `Escape` to cancel without creating.

### Properties

Each volume has:
- **Name** — descriptive label
- **AreaType** — enum value (Ground, Water, MagmaSlime, etc. — Trinity Core numbering)
- **MinHeight / MaxHeight** — vertical extent of the volume

When finalized, vertices are automatically processed through a **convex hull** algorithm (Graham scan on XZ plane) to ensure the volume is always convex.

### File Format

```xml
<ConvexVolumes Version="1.0">
  <Volume Name="Water Zone" AreaType="Water" MinHeight="0" MaxHeight="50">
    <Vertex X="1234.56" Y="5678.90" Z="100.00" />
    <Vertex X="1244.56" Y="5678.90" Z="100.00" />
    <Vertex X="1244.56" Y="5688.90" Z="100.00" />
  </Volume>
</ConvexVolumes>
```

---

## WMO 3D Visualization

MeshViewer3D can load WMO (World Map Object) building geometry from WoW 3.3.5a MPQ archives and render it alongside the navmesh. This gives spatial context when placing blackspots or jump links near buildings.

### How it works

1. The application reads WoW's MPQ archives (`common.MPQ`, `expansion.MPQ`, `lichking.MPQ`, `patch.MPQ`, etc.) using a pure C# MPQ reader — no native DLL dependencies.
2. When tiles are loaded, the ADT (terrain tile) file is parsed to find MODF entries (WMO placement records).
3. For each placement, the WMO root file and its group files are read from MPQ, providing collision geometry (vertices + triangles).
4. Each WMO instance is rendered in **yellow** with its correct world position, rotation, and scale baked on the CPU.
5. M2 doodads (trees, fences, furniture) are loaded from MDDF placements and rendered in **red** using bounding geometry.

### Setup

1. Go to **Map > Set WoW Data Folder...** and select your WoW 3.3.5a `Data/` folder (the one containing `.MPQ` archives). The path is saved automatically and restored on next launch.
2. Load a navmesh tile — WMO objects are loaded automatically from the corresponding ADT.
3. The Objects tab shows the WMO and M2 files referenced by the loaded tiles.

### Technical details

- **MPQ formats**: v1, v2, v3 with all decompression codecs (Deflate, Zlib, PKLib, Huffman, WAV ADPCM).
- **WMO format**: v17 — root file (MOHD, MOGI chunks) and group files (MOVT vertices, MOVI indices, MOPY materials).
- **ADT format**: v18 — MWMO/MWID name tables, MODF placements (64 bytes each), MDDF placements (36 bytes each).
- **Geometry selection**: collision indices are preferred over render indices (this is a navmesh editor, not a graphics viewer).
- **Coordinate transform**: Exact MaNGOS vmap-extractor pipeline: `fixCoords(pos)`, `G3D::fromEulerAnglesXYZ(-rz, -rx, -ry)` rotation matrix, vertex transform `v * rotation * scale + position`, mirror X/Y, `copyVertices(y,z,x)` swap to Recast space.
- **M2 format**: WotLK M2 header, bounding geometry at offsets 0xD8-0xE4 (`nBoundingTriangles`, `ofsBoundingTriangles`, `nBoundingVertices`, `ofsBoundingVertices`).

### Visibility

Toggle WMO visibility per-object in the Objects panel, or globally via the Settings panel.

---

## Map Database

The application uses `Maps.json` (in `Resources/`) as a database of all WoW 3.3.5a maps. Each entry contains:

| Field | Description |
|-------|-------------|
| `id` | Map ID (from Map.dbc) |
| `name` | Display name (e.g. "Eastern Kingdoms") |
| `continent` | Category (Azeroth, Instance, Raid, Battleground, etc.) |
| `directory` | WoW internal directory name (e.g. "Expansion01" for Outland) |

The `directory` field is the Map.dbc `InternalName` column — it maps map IDs to the internal path used in MPQ archives (e.g. `World\Maps\Expansion01\Expansion01_32_48.adt`). The database currently covers **40 maps** including all open-world continents, dungeons, raids, and battlegrounds from patch 3.3.5a.

Previously, this mapping was a hardcoded switch supporting only 5 maps. The `MapDatabase` class loads Maps.json lazily on first access and provides `GetDirectory()`, `GetName()`, and `Get()` lookups.

---

## Settings Persistence

Application settings are saved to `settings.json` next to the executable. Currently persisted:

| Setting | Description |
|---------|-------------|
| `wowDataPath` | Path to WoW 3.3.5a `Data/` folder |

On startup, the saved WoW Data path is restored automatically if: the directory still exists **and** contains at least one `.MPQ` file. This eliminates the need to re-select the folder every session.

---

## Menus

### Map
| Item | Description |
|------|-------------|
| Load Tile... | Open a single `.mmtile` file |
| Load Folder... | Load all `.mmtile` files in a directory (merges into one scene, 60k vertex limit) |
| Set WoW Data Folder... | Point to WoW 3.3.5a `Data/` folder to enable WMO visualization (auto-saved) |
| Close Tile | Unload the current navmesh |
| Exit | Quit the application |

### Mesh
| Item | Shortcut | Description |
|------|----------|-------------|
| Load Blackspots... | `Ctrl+O` | Load blackspots from XML |
| Save Blackspots... | `Ctrl+S` | Save blackspots to XML |
| Export Blackspots (CSV)... | | CSV export |
| Load Jump Links... | | Load from XML or `.offmesh` |
| Save Jump Links... | | Save to XML or `.offmesh` |
| Export Jump Links (CSV)... | | CSV export |
| Load Volumes... | | Load convex volumes XML |
| Save Volumes... | | Save convex volumes XML |
| Clear All Blackspots | `Ctrl+N` | Delete all blackspots (with confirmation) |

### View
| Item | Shortcut | Description |
|------|----------|-------------|
| Wireframe | `W` | Toggle navmesh wireframe overlay |
| OffMesh Connections | | Show/hide OffMesh lines from tile data |
| Lighting | `L` | Toggle dynamic lighting |
| Color Mode | | By Area Type / By Height |
| Reset Camera | `R` | Re-center camera on loaded geometry |

### Tools
| Item | Description |
|------|-------------|
| Clear Console | Clear the log output panel |

---

## UI Panels

The right side panel has tabs:

### Blackspots Tab
- List of all blackspots (format: `Name (R=radius)`)
- Property fields: Name, X, Y, Z, Radius, Height
- Buttons: Add, Remove, Clear, Apply Changes, Click to Place

### Jump Links Tab
- List of all custom jump links
- Property fields: Start/End coordinates, Radius, Bidirectional toggle
- Buttons: Add, Remove, Clear

### Convex Volumes Tab
- List of all volumes
- Property fields: Name, AreaType dropdown, MinHeight, MaxHeight, vertex list
- Buttons: Remove, Clear

### Objects Tab
- WMO and M2 file lists from ADT references
- Per-object visibility checkboxes
- LINQ-deduplicated (no duplicate entries)

### WMO Blacklist Tab
- CheckedListBox of all loaded WMO names
- Select All / Deselect All buttons
- Export / Import blacklist (text file)
- Blacklisted WMOs are hidden from the 3D view

### Per-Model Overrides Tab
- ComboBox model selector (WMO and M2 names)
- Ignore Collision, Area Type, Scale Override settings
- Add / Remove override buttons
- Export / Import overrides (JSON file)

### Settings Tab
- Rendering toggles: wireframe, lighting, offmesh display, blackspot display, volume display
- Color mode selection
- Alpha and fog sliders

### Minimap
- 64×64 tile grid showing which tiles are loaded

### Console (bottom)
- Color-coded log: green (success), red (error), orange (warning), white (info)

### HUD Overlay (top-left)
```
Pos: {X, Y, Z}              Camera position (WoW coords)
Tile: (31, 25)              Current tile coordinates
Polys: 2,456 | Verts: 4,912 Mesh statistics
Blackspots: 3 | Volumes: 0  Element counts
FPS: 60 (16.6 ms)           Frame timing
[CLICK MODE - Place Blackspot]  Active mode indicator
[RAYTRACE] Hit: {X, Y, Z} | Poly #42 | Ground (0)   Raytrace cursor info
```

---

## Keyboard Reference

### File Operations
| Key | Action |
|-----|--------|
| `Ctrl+O` | Load blackspots XML |
| `Ctrl+S` | Save blackspots XML |
| `Ctrl+N` | Clear all blackspots (with confirmation) + clear undo history |
| `Ctrl+Z` | Undo last editing operation |
| `Ctrl+Y` / `Ctrl+Shift+Z` | Redo last undone operation |

### Camera
| Key | Action |
|-----|--------|
| `R` / `Home` | Reset camera |
| `F` | Focus on selected element |
| `G` | Go to coordinates (dialog) |

### Mode Switching
| Key | Action |
|-----|--------|
| `Q` | Navigation mode (cancel all edit modes) |
| `B` | Blackspot placement mode |
| `J` | Jump link placement mode |
| `V` | Volume placement mode |

### Editing
| Key | Action |
|-----|--------|
| `Enter` | Finalize convex volume (in V mode) |
| `Escape` | Cancel current operation / deselect |
| `Delete` | Delete selected element |
| `A` | Analyze NavMesh (connected components) |

### Display Toggles
| Key | Action |
|-----|--------|
| `W` | Toggle wireframe |
| `L` | Toggle lighting |

### Mouse Modifiers
| Input | Action |
|-------|--------|
| Scroll | Zoom camera |
| `Shift + Scroll` | Adjust selected blackspot radius (±1) |
| `Ctrl + Scroll` | Adjust selected blackspot height (±1) |

---

## File Formats

### `.mmtile` — Navigation Tile (read-only)

Binary Detour format. Parsed by `MmtileLoader.cs` (Trinity/Mangos format variant with `MMT\0` magic + version header before the Detour tile data).

### Blackspots XML (Honorbuddy compatible)

```xml
<?xml version="1.0" encoding="utf-8"?>
<Blackspots>
  <Blackspot X="1234.56" Y="7890.12" Z="345.67"
             Radius="10.00" Height="20.00" Name="Zone" MapId="0" />
</Blackspots>
```

### Jump Links XML

```xml
<OffMeshConnections Version="1.0">
  <Connection StartX="..." StartY="..." StartZ="..."
              EndX="..." EndY="..." EndZ="..."
              Radius="1.00" Bidirectional="True" />
</OffMeshConnections>
```

### Jump Links Binary (`.offmesh`)

36 bytes per connection: `float[3] start, float[3] end, float radius, ushort flags, ushort area, uint userId`. Matches Detour's `dtOffMeshConnection` layout.

### Convex Volumes XML

```xml
<ConvexVolumes Version="1.0">
  <Volume Name="Water Zone" AreaType="Water" MinHeight="0" MaxHeight="50">
    <Vertex X="..." Y="..." Z="..." />
  </Volume>
</ConvexVolumes>
```

### Blackspots CSV Export

Comma-separated: Name, X, Y, Z, Radius, Height. For spreadsheet analysis.

All XML coordinates are in **WoW world space** (X=North, Y=West, Z=Up).

---

## Coordinate System

Three coordinate spaces are used:

| Space | Axes | Where used |
|-------|------|-----------|
| **WoW** | X = North, Y = West, Z = Up | UI display, XML files, user input |
| **Detour** | X = −WoW.Y, Y = WoW.Z, Z = −WoW.X | Internal storage, `.mmtile` data |
| **OpenGL** | Same as Detour | GPU rendering |

Conversions: `CoordinateSystem.WowToDetour()` / `DetourToWow()`

All coordinates shown in the UI and saved to XML are in **WoW space**. Internal data structures and the tile file format use **Detour space**.

---

## Color Reference

### NavMesh — By Area Type (MaNGOS NavTerrain)
| Area ID | Name | Color | RGB |
|---------|------|-------|-----|
| 0 | Empty | Dark Grey | (80, 80, 80) |
| 1 | Ground | Green | (50, 200, 50) |
| 2 | Magma | Red | (200, 0, 0) |
| 4 | Slime | Yellow-Green | (120, 200, 20) |
| 8 | Water | Blue | (30, 100, 220) |
| 63 | Unwalkable | Red | (200, 0, 0) |

### 3D Objects
| Object | Color | RGB (approx) |
|--------|-------|---------------|
| WMO (buildings) | Yellow | (230, 209, 64) |
| M2 (doodads: trees, fences) | Red | (217, 38, 38) |

### NavMesh — By Height
Gradient from low (blue) to high (red).

### OffMesh Connections
| Type | Color |
|------|-------|
| Bidirectional | Cyan |
| Unidirectional | Orange |

### Blackspots
| State | Color |
|-------|-------|
| Normal | Red (alpha 0.4) |
| Selected | Yellow/orange (alpha 0.4) |
| Just created | Green pulse (0.8 s) |

---

## Architecture Decisions

Key design choices made during development:

| Decision | Rationale |
|----------|-----------|
| `BinaryReader` field-by-field (no `MemoryMarshal.Read<T>`) | WoW structs have implicit padding; explicit reads are predictable and safe |
| Primitives-only structs (`posX/Y/Z` not `Vector3`) | Avoids OpenTK alignment assumptions across .NET runtime versions |
| CPU-side transform bake in `WmoRenderer` | One `uModel = Identity` draw call per instance, no per-instance uniform upload needed |
| `CollisionIndices` primary, `RenderIndices` fallback | This is a navmesh editor — collision triangles are the ground truth |
| `WmoGeometry` inside `WmoGroup` | Keeps the parse result self-contained; the renderer consumes only what it needs |
| `WowDataProvider` priority stack | Mirrors WoW client patch priority: `patch-3 > lichking > expansion > common` |

---

## Feature Comparison

Comparison with Honorbuddy's Tripper.Renderer:

| Feature | Tripper.Renderer | MeshViewer3D | Status |
|---------|-----------------|--------------|--------|
| NavMesh visualization | Yes | Yes | Done |
| Area type coloring | Yes | Yes | Done |
| Wireframe overlay | Yes | Yes | Done |
| OffMesh display (from tiles) | Yes | Yes | Done |
| Load Folder (multi-tile) | Yes | Yes | Done |
| Blackspot editor | Yes | Yes | Done |
| — Click to place | Yes | Yes | Done |
| — Drag to move | Yes | Yes | Done |
| — Scroll to resize | Yes | Yes | Done |
| — XML save/load | Yes | Yes | Done |
| Jump Link editor | Yes | Yes | Done |
| — Two-click placement | Yes | Yes | Done |
| — Bidirectional toggle | Yes | Yes | Done |
| — XML + `.offmesh` save/load | Yes | Yes | Done |
| — CSV export | Yes | Yes | Done |
| Convex Volume editor | Yes | Yes | Done |
| — Click to place vertices | Yes | Yes | Done |
| — Live preview | Yes | Yes | Done |
| — XML save/load | Yes | Yes | Done |
| WMO 3D objects | Yes | Yes | Done |
| M2 doodad rendering | Yes | Yes | Done |
| Map directory database | N/A | Yes | Done |
| Settings persistence | N/A | Yes | Done |
| WMO Blacklist | Yes | Yes | Done |
| Per-Model Overrides | Yes | Yes | Done |
| Test Navigation (pathfinding) | Yes | Yes | Done |
| — Funnel Algorithm (smooth paths) | No | Yes | Done |
| — Cross-tile navigation | No | Yes | Done |
| — Off-mesh connections in pathfinding | No | Yes | Done |
| Raytrace mode | Yes | Yes | Done |
| NavMesh analysis (components, degenerate) | No | Yes | Done |
| Export paths (JSON, CSV, HB XML) | No | Yes | Done |
| Undo/Redo | Yes | Yes | Done |

---

## Limitations

### Not implemented (out of scope)
- Terrain heightmap rendering (ADT MCNK chunks)
- BLP texture loading / GPU texture upload
- WDT parser (world tile index — tiles are loaded manually)
- Multi-selection
- Snap to grid
- Copy/paste

### Functional stubs (UI exists, logic not wired)

(None remaining — all stubs have been implemented.)

### Safety limits
- 60,000 vertex limit when loading a folder of tiles (hard abort to protect GPU buffers)

---

## Troubleshooting

### `.mmtile` file does not load
- Verify the file has a valid header (`MMT\0` magic + version).
- Check the console panel for detailed error messages.

### Cannot click on blackspot
- Ensure placement mode is disabled (press `B` to toggle off).
- The blackspot may be occluded by terrain — orbit the camera.

### Coordinates look wrong after load
- XML files use WoW world coordinates. Internal storage uses Detour coordinates.
- Verify your file was saved from this application and not manually edited with wrong coordinate space.

### Mouse wheel does not change radius/height
- A blackspot must be selected first.
- Hold `Shift` (radius) or `Ctrl` (height) while scrolling.
- Range limits: Radius 1–500, Height 1–200.

### WMO geometry does not appear
- Ensure the WoW 3.3.5a `Data/` folder is configured and contains MPQ archives.
- Check that the loaded tile's ADT file references WMO placements (not all areas have buildings).
- Check the console for MPQ read errors.

---

## Resources

- [Recast/Detour](https://github.com/recastnavigation/recastnavigation) — Navigation mesh library
- [wowdev.wiki/ADT/v18](https://wowdev.wiki/ADT/v18) — ADT format documentation
- [wowdev.wiki/WMO](https://wowdev.wiki/WMO) — WMO format documentation
- [OpenTK](https://opentk.net/) — OpenGL bindings for .NET
