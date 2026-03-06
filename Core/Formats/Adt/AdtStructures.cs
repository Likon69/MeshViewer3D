// ============================================================================
//  AdtStructures.cs  —  ADT v18 binary structures (WoW 3.3.5a / WotLK)
//
//  Version: ADT v18 (pre-Cataclysm)
//  Source: https://wowdev.wiki/ADT/v18
//
//  Key invariants:
//    - MODF is EXACTLY 64 bytes per entry
//    - MDDF is EXACTLY 36 bytes per entry
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
}
