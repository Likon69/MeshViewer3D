// ============================================================================
//  AdtStructures.cs  —  ADT v18 binary structures (WoW 3.3.5a / WotLK)
//
//  Version: ADT v18 (pre-Cataclysm)
//  Source: https://wowdev.wiki/ADT/v18
//
//  Key invariants:
//    - MODF is EXACTLY 64 bytes per entry
//    - MDDF is EXACTLY 36 bytes per entry
//    - MCNK header is EXACTLY 128 bytes
//    - No Vector3 in struct fields — field-by-field BinaryReader reads only
//      (consistent with WmoGroup.cs; avoids OpenTK alignment assumptions)
// ============================================================================

using System.Runtime.InteropServices;

namespace MeshViewer3D.Core.Formats.Adt
{
    /// <summary>
    /// MODF — WMO (building/structure) placement record from the MODF chunk.
    /// Exactly 64 bytes per entry.
    ///
    /// Layout:
    /// +0x00 (4)  uint    mwidEntry      Index into MWID uint array → byte offset in MWMO blob
    /// +0x04 (4)  uint    uniqueId       Per-instance unique ID
    /// +0x08 (4)  float   posX           World-space X
    /// +0x0C (4)  float   posY           World-space Y
    /// +0x10 (4)  float   posZ           World-space Z
    /// +0x14 (4)  float   rotX           Rotation X (degrees)
    /// +0x18 (4)  float   rotY           Rotation Y (degrees)
    /// +0x1C (4)  float   rotZ           Rotation Z (degrees)
    /// +0x20 (4)  float   boundsMinX     AABB min X
    /// +0x24 (4)  float   boundsMinY     AABB min Y
    /// +0x28 (4)  float   boundsMinZ     AABB min Z
    /// +0x2C (4)  float   boundsMaxX     AABB max X
    /// +0x30 (4)  float   boundsMaxY     AABB max Y
    /// +0x34 (4)  float   boundsMaxZ     AABB max Z
    /// +0x38 (2)  ushort  flags          WMO placement flags
    /// +0x3A (2)  ushort  doodadSet      Doodad set index
    /// +0x3C (2)  ushort  nameSet        Name set index
    /// +0x3E (2)  ushort  scale          Scale: 1024 = 1.0×
    /// Total: 64 bytes ✓
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MODF
    {
        public uint   mwidEntry;
        public uint   uniqueId;
        public float  posX,      posY,      posZ;
        public float  rotX,      rotY,      rotZ;
        public float  boundsMinX, boundsMinY, boundsMinZ;
        public float  boundsMaxX, boundsMaxY, boundsMaxZ;
        public ushort flags;
        public ushort doodadSet;
        public ushort nameSet;
        public ushort scale;
    }

    /// <summary>
    /// MDDF — M2 (doodad/model) placement record from the MDDF chunk.
    /// Exactly 36 bytes per entry.
    ///
    /// Layout:
    /// +0x00 (4)  uint    mmidEntry      Index into MMID uint array → byte offset in MMDX blob
    /// +0x04 (4)  uint    uniqueId       Per-instance unique ID
    /// +0x08 (4)  float   posX           World-space X
    /// +0x0C (4)  float   posY           World-space Y
    /// +0x10 (4)  float   posZ           World-space Z
    /// +0x14 (4)  float   rotX           Rotation X (degrees)
    /// +0x18 (4)  float   rotY           Rotation Y (degrees)
    /// +0x1C (4)  float   rotZ           Rotation Z (degrees)
    /// +0x20 (2)  ushort  scale          Scale: 1024 = 1.0×
    /// +0x22 (2)  ushort  flags          M2 placement flags
    /// Total: 36 bytes ✓
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MDDF
    {
        public uint   mmidEntry;
        public uint   uniqueId;
        public float  posX, posY, posZ;
        public float  rotX, rotY, rotZ;
        public ushort scale;
        public ushort flags;
    }

    // ========================================================================
    //  MCNK terrain structures
    // ========================================================================

    /// <summary>
    /// Parsed terrain chunk data from one MCNK sub-chunk of an ADT tile.
    /// Each ADT tile contains 16×16 = 256 terrain chunks.
    ///
    /// The heightmap grid is 9×9 outer + 8×8 inner = 145 vertices.
    /// Effective resolution: 17×17 per chunk (33.33 yards per chunk side).
    ///
    /// Source: https://wowdev.wiki/ADT/v18#MCNK_chunk
    /// </summary>
    public sealed class TerrainChunk
    {
        /// <summary>Chunk index X (0–15) within the ADT tile.</summary>
        public int IndexX { get; set; }

        /// <summary>Chunk index Y (0–15) within the ADT tile.</summary>
        public int IndexY { get; set; }

        /// <summary>World-space X position of the chunk corner (ADT coord space).</summary>
        public float PositionX { get; set; }

        /// <summary>World-space Z/depth position of the chunk corner (ADT coord space).</summary>
        public float PositionY { get; set; }

        /// <summary>Base height (added to all MCVT values).</summary>
        public float BaseHeight { get; set; }

        /// <summary>Hole flags. Each bit = one 4×4 sub-chunk is a hole (not rendered).</summary>
        public uint Holes { get; set; }

        /// <summary>
        /// Heightmap vertices: 145 floats from MCVT sub-chunk.
        /// Layout: 9 rows of 9 (outer grid), interleaved with 8 rows of 8 (inner grid).
        /// Row order: 9-outer, 8-inner, 9-outer, 8-inner, … (alternating, 17 rows total).
        /// </summary>
        public float[] Heights { get; set; } = new float[145];

        /// <summary>
        /// Per-vertex normals from MCNR sub-chunk (145 × 3 bytes).
        /// Stored as sbyte XYZ components, normalized to [-1, 1].
        /// Null if MCNR chunk not found.
        /// </summary>
        public sbyte[]? Normals { get; set; }

        /// <summary>
        /// Texture layer definitions from MCLY sub-chunk.
        /// Each layer is 16 bytes. Null if MCLY not found.
        /// </summary>
        public MclyEntry[]? TextureLayers { get; set; }

        /// <summary>Number of texture layers for this chunk (0–4).</summary>
        public int LayerCount => TextureLayers?.Length ?? 0;
    }

    /// <summary>
    /// MCLY texture layer entry — 16 bytes each.
    /// Describes one texture layer on a terrain chunk.
    /// Source: https://wowdev.wiki/ADT/v18#MCLY_chunk
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MclyEntry
    {
        public uint   textureId;     // +0x00  Index into MTEX filename blob
        public uint   flags;         // +0x04  Layer flags (bit 0 = animated, etc.)
        public uint   alphaOffset;   // +0x08  Offset into MCAL chunk for alpha map
        public uint   effectId;      // +0x0C  Ground effect texture ID
    }
    // Total: 16 bytes ✓
}
