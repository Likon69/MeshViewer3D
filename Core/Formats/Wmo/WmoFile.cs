// WmoFile.cs — WMO v17 root file parser, WoW 3.3.5a
// Reads only what Phase 2 needs: GroupCount + bounding box from MOHD.
// Source: https://wowdev.wiki/WMO#MOHD

using System;
using System.IO;
using MeshViewer3D.Core.Formats;
using OpenTK.Mathematics;

namespace MeshViewer3D.Core.Formats.Wmo
{
    /// <summary>
    /// Minimal WMO v17 root file parser.
    /// Reads MOHD for <see cref="GroupCount"/> and bounding box.
    /// Group files are loaded separately via <see cref="GetGroupFilePath"/>.
    /// </summary>
    public sealed class WmoFile
    {
        /// <summary>Number of group files (_000.wmo … _NNN.wmo) referenced by this root.</summary>
        public int     GroupCount { get; private set; }

        /// <summary>World-space bounding box minimum.</summary>
        public Vector3 BoundsMin  { get; private set; }

        /// <summary>World-space bounding box maximum.</summary>
        public Vector3 BoundsMax  { get; private set; }

        /// <param name="data">Raw bytes from MPQ.</param>
        public WmoFile(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            Parse(data);
        }

        private void Parse(byte[] data)
        {
            foreach (var (tag, off, len) in ChunkReader.ReadChunks(data, 0, data.Length))
            {
                switch (tag)
                {
                    case "MVER":
                        if (len < 4) throw new InvalidDataException("MVER too small.");
                        int ver = BitConverter.ToInt32(data, off);
                        if (ver != 17)
                            throw new InvalidDataException($"Expected WMO v17, got v{ver}.");
                        break;

                    case "MOHD":
                        // MOHD v17 layout — 64 bytes total
                        // Offset  Size  Field
                        // 0x00     4    nMaterials     (skip)
                        // 0x04     4    nGroups        ← GroupCount
                        // 0x08     4    nPortals       (skip)
                        // 0x0C     4    nLights        (skip)
                        // 0x10     4    nDoodadNames   (skip)
                        // 0x14     4    nDoodadDefs    (skip)
                        // 0x18     4    nDoodadSets    (skip)
                        // 0x1C     4    ambientColor   (skip)
                        // 0x20     4    wmoID          (skip)
                        // 0x24    12    BoundingBoxMin ← BoundsMin
                        // 0x30    12    BoundingBoxMax ← BoundsMax
                        // 0x3C     2    flags          (skip)
                        // 0x3E     2    numLod         (skip)
                        // Total: 9×4 + 6×4 + 2×2 = 64 bytes
                        if (len < 64)
                            throw new InvalidDataException($"MOHD is {len} bytes; v17 requires 64.");

                        using (var ms = new MemoryStream(data, off, len, writable: false))
                        using (var br = new BinaryReader(ms))
                        {
                            br.ReadUInt32();                    // nMaterials   +0x00
                            GroupCount = (int)br.ReadUInt32(); // nGroups      +0x04
                            br.ReadUInt32();                    // nPortals     +0x08
                            br.ReadUInt32();                    // nLights      +0x0C
                            br.ReadUInt32();                    // nDoodadNames +0x10
                            br.ReadUInt32();                    // nDoodadDefs  +0x14
                            br.ReadUInt32();                    // nDoodadSets  +0x18
                            br.ReadUInt32();                    // ambientColor +0x1C
                            br.ReadUInt32();                    // wmoID        +0x20
                            float mnX = br.ReadSingle();       // BoundsMin.X  +0x24
                            float mnY = br.ReadSingle();       //           .Y +0x28
                            float mnZ = br.ReadSingle();       //           .Z +0x2C
                            float mxX = br.ReadSingle();       // BoundsMax.X  +0x30
                            float mxY = br.ReadSingle();       //           .Y +0x34
                            float mxZ = br.ReadSingle();       //           .Z +0x38
                            BoundsMin = new Vector3(mnX, mnY, mnZ);
                            BoundsMax = new Vector3(mxX, mxY, mxZ);
                        }
                        return; // MOHD is all we need — stop scanning
                }
            }

            throw new InvalidDataException("MOHD chunk not found; not a valid WMO root file.");
        }

        /// <summary>
        /// Derives the path for group file <paramref name="groupIndex"/> from the root path.
        /// WoW convention: "Path/Name.wmo" → "Path/Name_000.wmo", "Path/Name_001.wmo", …
        /// </summary>
        public static string GetGroupFilePath(string rootWmoPath, int groupIndex)
        {
            string dir  = Path.GetDirectoryName(rootWmoPath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(rootWmoPath);
            return Path.Combine(dir, $"{name}_{groupIndex:000}.wmo");
        }
    }
}
