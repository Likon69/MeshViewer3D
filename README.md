# MeshViewer3D

> NavMesh viewer and editor for WoW 3.3.5a (build 12340), inspired by Honorbuddy's Tripper.Renderer.


![Orgrimmar](Resources/0014531.png)

---

## Overview

MeshViewer3D loads Detour/Recast `.mmtile` navigation tiles and provides tools to:

- **View** navmesh polygons with area-type coloring, wireframe, and OffMesh connections
- **Edit blackspots** — cylindrical no-go zones (compatible with Honorbuddy XML format)
- **Create jump links** — custom OffMesh connections between two points (XML + binary `.offmesh`)
- **Define convex volumes** — polygonal areas with custom area types (XML)
- **Visualise WMO buildings** — loads WMO geometry from MPQ archives via ADT tile references

All coordinate display is in WoW world space. Internal storage uses Detour coordinates.

---

## Requirements

| Requirement | Version |
|-------------|---------|
| OS | Windows 10/11 |
| Runtime | .NET 6.0 |
| GPU | OpenGL 3.3+ |

## Quick Start

```
cd MeshViewer3D
dotnet run
```

1. **Map > Load Tile** or **Load Folder** — select `.mmtile` file(s)
2. Press **B** to enter blackspot placement, click on terrain
3. Press **J** to enter jump link mode, click start then end
4. Press **V** to enter volume mode, click vertices, press **Enter** to finalize
5. Use the new camera controls: middle mouse to orbit, `Shift+Middle` or right mouse to pan, scroll to zoom toward the cursor
6. **Ctrl+S** to save

---

## Feature Status

| Feature | Status | Notes |
|---------|--------|-------|
| NavMesh rendering (area colors, wireframe) | **Done** | Multi-tile support with 60k vertex safety limit |
| OffMesh connection display (from tiles) | **Done** | Cyan = bidirectional, Orange = unidirectional |
| Blackspot editor | **Done** | Click to place, drag to move, scroll to resize, XML save/load |
| Jump Link editor | **Done** | Two-click placement, XML + `.offmesh` binary save/load, CSV export |
| Convex Volume editor | **Done** | Click vertices, Enter to finalize, XML save/load |
| WMO 3D visualization | **Done** | Pure C# MPQ reader, WMO v17 parser, ADT v18 MODF instances. Set Data folder via Map menu |
| Map directory lookup | **Done** | Dynamic Maps.json database (40 maps, all 3.3.5a), replaces hardcoded 5-map switch |
| WoW Data path persistence | **Done** | Auto-saved to settings.json, restored on startup with MPQ validation |
| Go To Coordinates | **Done** | G key, dialog input |
| Minimap (tile grid) | **Done** | Shows loaded tiles |
| Settings panel | **Done** | Toggle wireframe/lighting/offmesh/blackspots/volumes, color modes |
| Raytrace mode | **Done** | 3D cursor marker on navmesh, shows WoW coords + AreaType + poly index |
| Test Navigation (A→B pathfinding) | **Done** | A* + Funnel Algorithm, cross-tile, off-mesh traversal, smooth paths |
| NavMesh Analysis | **Done** | Connected components (BFS), degenerate polygon detection, component coloring (A key) |
| Export Paths | **Done** | JSON, CSV, HB Hotspot XML (Tools menu) |
| Undo/Redo | **Done** | Ctrl+Z / Ctrl+Y — blackspots, volumes, jump links (Command Pattern) |
| WMO Blacklist | **Done** | CheckedListBox, Select All/Deselect All, Export/Import |
| Per-Model Overrides | **Done** | Per-model volume/collision settings, JSON Export/Import |
| M2 model rendering | **Done** | Bounding geometry parser (0xD8 offsets), same coordinate pipeline as WMO |
| Terrain heightmap (ADT MCNK) | **Done** | ADT MCNK chunks → UV-textured OpenGL mesh, height-correct ground geometry, automatic center-tile load + optional 3×3 terrain grid |
| BLP texture loading | **Done** | MPQ → BlpFile.Load → GlTexture.FromBlp, per-layer texture draw groups |
| WDT parser (world tile index) | **Done** | WDT tile existence grid, minimap tile highlighting |
| NavMesh Fill toggle | **Done** | Settings panel checkbox — show/hide navmesh polygon fill |
| 3×3 ADT terrain grid | **Done** | Map > Load Terrain from ADT loads center tile + up to 8 surrounding tiles |

---

## Controls

### Camera

| Action | Input |
|--------|-------|
| Orbit | Middle mouse drag |
| Pan | `Shift+Middle` drag or Right mouse drag |
| Zoom | Scroll wheel or `Ctrl+Middle` drag |
| Frame scene | `R` or `Home` |
| View presets | Numpad `1` / `3` / `7` and `Ctrl` variants |
| Focus selection | `F` |
| Toggle free camera | `C` |
| Free camera move | `W` / `A` / `S` / `D` / `Q` / `E` |

The old left-drag camera orbit is no longer used. Camera navigation follows a Blender-style controller, and free camera movement uses `Q/E` for vertical motion while active.

### Editing

| Key | Action |
|-----|--------|
| `B` | Toggle blackspot placement mode |
| `J` | Toggle jump link placement mode (click start, then end) |
| `V` | Toggle convex volume placement mode (click vertices) |
| `Enter` | Finalize convex volume (in V mode) |
| `Escape` | Cancel current mode / deselect |
| `Q` | Return to navigation mode (disable all edit modes) |
| `Delete` | Delete selected element |
| `G` | Go to coordinates dialog |
| `F` | Focus current selection |
| `C` | Toggle free camera mode |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` or `Ctrl+Shift+Z` | Redo |
| `Shift+Wheel` | Adjust blackspot radius |
| `Ctrl+Wheel` | Adjust blackspot height |

### File Shortcuts

| Key | Action |
|-----|--------|
| `Ctrl+O` | Load blackspots XML |
| `Ctrl+S` | Save blackspots XML |
| `Ctrl+N` | Clear all blackspots |
| `W` | Toggle wireframe |
| `L` | Toggle lighting |

---

## File Formats

### Blackspots (XML — Honorbuddy compatible)
```xml
<Blackspots>
  <Blackspot X="1234.56" Y="7890.12" Z="345.67"
             Radius="10.00" Height="20.00" Name="Zone" />
</Blackspots>
```

### Jump Links (XML)
```xml
<OffMeshConnections Version="1.0">
  <Connection StartX="..." StartY="..." StartZ="..."
              EndX="..." EndY="..." EndZ="..."
              Radius="1.00" Bidirectional="True" />
</OffMeshConnections>
```

### Convex Volumes (XML)
```xml
<ConvexVolumes Version="1.0">
  <Volume Name="Water Zone" AreaType="Water" MinHeight="0" MaxHeight="50">
    <Vertex X="1234.56" Y="5678.90" Z="100.00" />
    <Vertex X="1244.56" Y="5678.90" Z="100.00" />
    <Vertex X="1244.56" Y="5688.90" Z="100.00" />
  </Volume>
</ConvexVolumes>
```

All XML coordinates are in **WoW world space** (X=North, Y=West, Z=Up).

---


### Technologies

- **.NET 6.0-windows** — WinForms application
- **OpenTK 4.8.2** — OpenGL 3.3+ bindings
- **Newtonsoft.Json** — Map name lookup
- **Pure C# MPQ** — No native DLL dependencies for archive reading

---

## Coordinate System

| Space | Axes | Usage |
|-------|------|-------|
| **WoW** | X=North, Y=West, Z=Up | UI display, XML files |
| **Detour** | X=-WoW.Y, Y=WoW.Z, Z=-WoW.X | Internal storage, tile data |
| **OpenGL** | Same as Detour | Rendering |

Conversion: `CoordinateSystem.WowToDetour()` / `DetourToWow()`

---

## Reference

- [Recast/Detour](https://github.com/recastnavigation/recastnavigation) — Navigation mesh library
- [wowdev.wiki/ADT](https://wowdev.wiki/ADT/v18) — ADT v18 format documentation
- [wowdev.wiki/WMO](https://wowdev.wiki/WMO) — WMO v17 format documentation
- [DOCUMENTATION.md](DOCUMENTATION.md) — Full user guide
