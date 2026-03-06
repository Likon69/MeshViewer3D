// WmoGroup.cs — WMO v17 group file parser, WoW 3.3.5a
// Parses MOVT (vertices) + MOVI (indices) + MOPY (flags).
// Filters triangles at parse time into CollisionIndices and RenderIndices.
// Source: https://wowdev.wiki/WMO#MOGP_chunk

using System;
using System.Collections.Generic;
using System.IO;
using MeshViewer3D.Core.Formats;
using OpenTK.Mathematics;

namespace MeshViewer3D.Core.Formats.Wmo
{
    /// <summary>
    /// Parses a single WMO v17 group file (_NNN.wmo).
    /// Exposes <see cref="Geometry"/> with collision and render triangle arrays
    /// pre-filtered from MOPY flags at parse time.
    /// </summary>
    public sealed class WmoGroup
    {
        private const byte FlagCollision = 0x08; // MOPY: solid (navmesh/physics)
        private const byte FlagRender    = 0x20; // MOPY: rendered by WoW client
        private const int  MogpHdrSize   = 68;   // v17 MOGP header is exactly 68 bytes

        public bool        IsValid    { get; private set; }
        public uint        GroupFlags { get; private set; }
        public Vector3     BoundsMin  { get; private set; }
        public Vector3     BoundsMax  { get; private set; }
        public WmoGeometry Geometry   { get; private set; } = WmoGeometry.Empty;

        public bool IsExterior => (GroupFlags & 0x00000008u) != 0;
        public bool IsInterior => (GroupFlags & 0x00000800u) != 0;

        /// <param name="data">Raw bytes from MPQ for this _NNN.wmo group file.</param>
        public WmoGroup(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            Parse(data);
        }

        private void Parse(byte[] data)
        {
            // Find MOGP chunk (all geometry lives inside it as sub-chunks)
            int mogpOff = -1, mogpLen = -1;
            foreach (var (tag, off, len) in ChunkReader.ReadChunks(data, 0, data.Length))
            {
                if (tag == "MVER")
                {
                    int ver = BitConverter.ToInt32(data, off);
                    if (ver != 17) throw new InvalidDataException($"Expected WMO group v17, got v{ver}.");
                }
                else if (tag == "MOGP") { mogpOff = off; mogpLen = len; break; }
            }

            if (mogpOff < 0) throw new InvalidDataException("MOGP chunk not found.");
            if (mogpLen < MogpHdrSize)
                throw new InvalidDataException($"MOGP is {mogpLen} bytes; needs ≥{MogpHdrSize}.");

            // Read 68-byte MOGP header field-by-field
            // MOGP header layout v17 (source: wowdev.wiki/WMO#MOGP_chunk):
            // +0x00  4  GroupNameOffset
            // +0x04  4  DescNameOffset
            // +0x08  4  Flags          ← GroupFlags
            // +0x0C 12  BoundingBoxMin ← BoundsMin
            // +0x18 12  BoundingBoxMax ← BoundsMax
            // +0x24  2  PortalRefOffset
            // +0x26  2  PortalRefCount
            // +0x28  2  TransBatchCount
            // +0x2A  2  IntBatchCount
            // +0x2C  2  ExtBatchCount
            // +0x2E  2  _batchPad
            // +0x30  4  FogIndex[4] (packed)
            // +0x34  4  LiquidType
            // +0x38  4  GroupWMOId
            // +0x3C  4  Flags2
            // +0x40  4  _unused
            // Total: 4+4+4+12+12+6×2+4+4+4+4+4 = 68 bytes ✓
            using (var ms = new MemoryStream(data, mogpOff, MogpHdrSize, writable: false))
            using (var br = new BinaryReader(ms))
            {
                br.ReadUInt32();                    // GroupNameOffset   +0x00
                br.ReadUInt32();                    // DescNameOffset    +0x04
                GroupFlags = br.ReadUInt32();       // Flags             +0x08
                float mnX = br.ReadSingle();        // BoundsMin.X       +0x0C
                float mnY = br.ReadSingle();
                float mnZ = br.ReadSingle();
                float mxX = br.ReadSingle();        // BoundsMax.X       +0x18
                float mxY = br.ReadSingle();
                float mxZ = br.ReadSingle();
                BoundsMin = new Vector3(mnX, mnY, mnZ);
                BoundsMax = new Vector3(mxX, mxY, mxZ);
                // remaining 32 bytes not needed for Phase 2 rendering
            }

            // Walk sub-chunks inside MOGP (start after the 68-byte header)
            int subStart = mogpOff + MogpHdrSize;
            int subEnd   = mogpOff + mogpLen;

            int movtOff = -1, movtLen = 0;
            int moviOff = -1, moviLen = 0;
            int mopyOff = -1, mopyLen = 0;

            foreach (var (tag, off, len) in ChunkReader.ReadChunks(data, subStart, subEnd))
            {
                switch (tag)
                {
                    case "MOVT": movtOff = off; movtLen = len; break;
                    case "MOVI": moviOff = off; moviLen = len; break;
                    case "MOPY": mopyOff = off; mopyLen = len; break;
                    // MONR, MOTV, MOBA, MOLR, MODR, MLIQ, MOCV: deferred
                }
            }

            Geometry = BuildGeometry(data, movtOff, movtLen, moviOff, moviLen, mopyOff, mopyLen);
            IsValid  = true;
        }

        private static WmoGeometry BuildGeometry(
            byte[] data,
            int movtOff, int movtLen,
            int moviOff, int moviLen,
            int mopyOff, int mopyLen)
        {
            if (movtOff < 0 || moviOff < 0 || mopyOff < 0)
                return WmoGeometry.Empty; // water-only or empty group

            int vertCount = movtLen / 12; // 3 floats × 4 bytes = 12 bytes/vertex
            int triCount  = mopyLen /  2; // 2 bytes per triangle in MOPY
            int idxCount  = moviLen /  2; // 1 ushort per index

            // MOVT → float[]: one bulk memcpy (same throughput as MemoryMarshal.Cast,
            // no struct layout risk because float is a primitive with no padding).
            float[] vertices = new float[vertCount * 3];
            Buffer.BlockCopy(data, movtOff, vertices, 0, movtLen);

            // MOVI → ushort[]: same bulk copy
            ushort[] indices = new ushort[idxCount];
            Buffer.BlockCopy(data, moviOff, indices, 0, moviLen);

            // Filter triangles by MOPY flags into collision and render lists
            var colBuf = new List<int>(triCount);
            var renBuf = new List<int>(triCount);
            var matBuf = new List<byte>(triCount / 3);

            for (int t = 0; t < triCount && t * 3 + 2 < idxCount; t++)
            {
                byte flags = data[mopyOff + t * 2    ];
                byte matId = data[mopyOff + t * 2 + 1];
                int  i0    = indices[t * 3    ];
                int  i1    = indices[t * 3 + 1];
                int  i2    = indices[t * 3 + 2];

                if ((flags & FlagCollision) != 0) { colBuf.Add(i0); colBuf.Add(i1); colBuf.Add(i2); }
                if ((flags & FlagRender)    != 0) { renBuf.Add(i0); renBuf.Add(i1); renBuf.Add(i2); matBuf.Add(matId); }
            }

            return new WmoGeometry(vertices, colBuf.ToArray(), renBuf.ToArray(), matBuf.ToArray());
        }
    }

    // ── Geometry output container ─────────────────────────────────────────────

    /// <summary>
    /// Filtered geometry from one WMO group.
    /// <see cref="CollisionIndices"/> and <see cref="RenderIndices"/> both reference
    /// the same <see cref="Vertices"/> array.
    /// </summary>
    public sealed class WmoGeometry
    {
        /// <summary>Flat vertex array [x0,y0,z0, x1,y1,z1, …] in WoW world-space coords.</summary>
        public float[] Vertices { get; }

        /// <summary>Triangle indices for F_COLLISION (0x08) triangles — navmesh/physics.</summary>
        public int[] CollisionIndices { get; }

        /// <summary>Triangle indices for F_RENDER (0x20) triangles — OpenGL draw calls.</summary>
        public int[] RenderIndices { get; }

        /// <summary>Material index per render triangle (parallel to RenderIndices / 3).</summary>
        public byte[] RenderMaterialIndices { get; }

        public int VertexCount            => Vertices.Length / 3;
        public int CollisionTriangleCount => CollisionIndices.Length / 3;
        public int RenderTriangleCount    => RenderIndices.Length / 3;

        public static WmoGeometry Empty { get; } = new WmoGeometry(
            Array.Empty<float>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<byte>());

        public WmoGeometry(float[] vertices, int[] collisionIndices,
                           int[] renderIndices, byte[] renderMaterialIndices)
        {
            Vertices              = vertices;
            CollisionIndices      = collisionIndices;
            RenderIndices         = renderIndices;
            RenderMaterialIndices = renderMaterialIndices;
        }
    }
}
