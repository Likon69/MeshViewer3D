// WmoStructures.cs — WMO v17 flag enums for WoW 3.3.5a
// Source: https://wowdev.wiki/WMO

namespace MeshViewer3D.Core.Formats.Wmo
{
    /// <summary>Flags from the MOGI chunk (group info) and MOGP header.</summary>
    [System.Flags]
    public enum MogiGroupFlags : uint
    {
        HasBsp          = 0x00000001,
        HasLightMap     = 0x00000002,
        HasVertexColors = 0x00000004,
        Exterior        = 0x00000008,
        ExteriorLit     = 0x00000040,
        Unreachable     = 0x00000080,
        HasDoodads      = 0x00000200,
        HasWater        = 0x00000400,
        Interior        = 0x00000800,
        ExteriorCull    = 0x00001000,
        HasCollision    = 0x00008000,
    }

    /// <summary>Per-triangle polygon flags from the MOPY chunk.</summary>
    [System.Flags]
    public enum MopyFlags : byte
    {
        F_UNK_0x01     = 0x01,
        F_NOCAMCOLLIDE = 0x02,
        F_DETAIL       = 0x04,
        F_COLLISION    = 0x08,  // solid — for navmesh / physics
        F_HINT         = 0x10,
        F_RENDER       = 0x20,  // visible triangle rendered by client
        F_UNK_0x40     = 0x40,
        F_COLLIDE_HIT  = 0x80,
    }
}
