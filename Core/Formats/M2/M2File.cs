// ============================================================================
//  M2File.cs  —  WoW 3.3.5a M2 model parser (bounding geometry only)
//
//  Extracts bounding vertices and triangles from M2 files, matching the
//  MaNGOS vmap-extractor approach (model.cpp → nBoundingVertices/Triangles).
//
//  WotLK (3.3.5a) M2 header layout (ModelHeaderOthers from modelheaders.h):
//    +0x00  char[4]   id ("MD20")
//    +0x04  uint8[4]  version
//    +0x08  uint32    nameLength
//    +0x0C  uint32    nameOfs
//    +0x10  uint32    type
//    ...
//    +0x3C  uint32    nVertices          (render verts, 48 bytes each)
//    +0x40  uint32    ofsVertices
//    +0x44  uint32    nViews             (no ofsViews in WotLK — external .skin)
//    ...
//    +0xD8  uint32    nBoundingTriangles (count of uint16 indices)
//    +0xDC  uint32    ofsBoundingTriangles
//    +0xE0  uint32    nBoundingVertices  (count of Vec3D = 12 bytes each)
//    +0xE4  uint32    ofsBoundingVertices
// ============================================================================

using System;
using System.IO;

namespace MeshViewer3D.Core.Formats.M2
{
    /// <summary>
    /// Parses a WoW 3.3.5a M2 model file and extracts bounding geometry
    /// for collision/navmesh visualization (same approach as MaNGOS vmap-extractor).
    /// </summary>
    public sealed class M2File
    {
        /// <summary>Flat XYZ vertex positions (3 floats per vertex).</summary>
        public float[] Vertices { get; private set; } = Array.Empty<float>();

        /// <summary>Triangle indices referencing into Vertices (3 per triangle).</summary>
        public int[] Indices { get; private set; } = Array.Empty<int>();

        /// <summary>True if the file was parsed successfully with geometry.</summary>
        public bool IsValid { get; private set; }

        /// <summary>Number of bounding vertices.</summary>
        public int VertexCount => Vertices.Length / 3;

        /// <summary>Number of bounding triangles.</summary>
        public int TriangleCount => Indices.Length / 3;

        // WotLK M2 header offsets for bounding geometry
        // Ref: https://wowdev.wiki/M2#Header (WotLK 3.3.5a = version 264)
        private const int OFS_MAGIC                  = 0x00; // "MD20"
        private const int OFS_N_BOUNDING_TRIANGLES   = 0xD8;  // collision_triangles.count
        private const int OFS_OFS_BOUNDING_TRIANGLES = 0xDC;  // collision_triangles.offset
        private const int OFS_N_BOUNDING_VERTICES    = 0xE0;  // collision_vertices.count
        private const int OFS_OFS_BOUNDING_VERTICES  = 0xE4;  // collision_vertices.offset
        private const int MIN_HEADER_SIZE            = 0xE8;  // need at least through ofsBoundingVertices

        /// <summary>
        /// Parse an M2 file from raw bytes (from MPQ).
        /// Uses bounding geometry like MaNGOS vmap-extractor model.cpp.
        /// </summary>
        public static M2File Load(byte[] data)
        {
            var m2 = new M2File();
            m2.Parse(data);
            return m2;
        }

        private void Parse(byte[] data)
        {
            if (data == null || data.Length < MIN_HEADER_SIZE)
            {
                Console.WriteLine("    M2 parse: file too small or null");
                return;
            }

            // Validate magic "MD20"
            if (data[0] != (byte)'M' || data[1] != (byte)'D' ||
                data[2] != (byte)'2' || data[3] != (byte)'0')
            {
                Console.WriteLine($"    M2 parse: bad magic: {(char)data[0]}{(char)data[1]}{(char)data[2]}{(char)data[3]}");
                return;
            }

            uint nBoundingTriangles  = BitConverter.ToUInt32(data, OFS_N_BOUNDING_TRIANGLES);
            uint ofsBoundingTris     = BitConverter.ToUInt32(data, OFS_OFS_BOUNDING_TRIANGLES);
            uint nBoundingVertices   = BitConverter.ToUInt32(data, OFS_N_BOUNDING_VERTICES);
            uint ofsBoundingVerts    = BitConverter.ToUInt32(data, OFS_OFS_BOUNDING_VERTICES);

            if (nBoundingVertices == 0 || nBoundingTriangles == 0)
            {
                Console.WriteLine($"    M2 parse: no bounding geometry (verts={nBoundingVertices}, tris={nBoundingTriangles})");
                return;
            }

            // Bounding vertices: each is 12 bytes (Vec3D = 3 floats)
            long vertEnd = (long)ofsBoundingVerts + (long)nBoundingVertices * 12;
            if (vertEnd > data.Length)
            {
                Console.WriteLine($"    M2 parse: bounding vertices overflow (ofs={ofsBoundingVerts}, n={nBoundingVertices}, fileLen={data.Length})");
                return;
            }

            // Bounding triangles: each index is uint16
            long triEnd = (long)ofsBoundingTris + (long)nBoundingTriangles * 2;
            if (triEnd > data.Length)
            {
                Console.WriteLine($"    M2 parse: bounding triangles overflow (ofs={ofsBoundingTris}, n={nBoundingTriangles}, fileLen={data.Length})");
                return;
            }

            // Read bounding vertices
            var verts = new float[nBoundingVertices * 3];
            using (var ms = new MemoryStream(data, (int)ofsBoundingVerts, (int)(nBoundingVertices * 12), writable: false))
            using (var br = new BinaryReader(ms))
            {
                for (int i = 0; i < (int)nBoundingVertices; i++)
                {
                    verts[i * 3 + 0] = br.ReadSingle();
                    verts[i * 3 + 1] = br.ReadSingle();
                    verts[i * 3 + 2] = br.ReadSingle();
                }
            }

            // Read bounding triangle indices (uint16 → int)
            // MaNGOS model.cpp swaps winding at (i%3)==1 for correct face orientation
            var indices = new int[nBoundingTriangles];
            using (var ms = new MemoryStream(data, (int)ofsBoundingTris, (int)(nBoundingTriangles * 2), writable: false))
            using (var br = new BinaryReader(ms))
            {
                for (int i = 0; i < (int)nBoundingTriangles; i++)
                    indices[i] = br.ReadUInt16();
            }

            // Swap winding order like MaNGOS: swap indices[1] and indices[2] of each triangle
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int tmp = indices[i + 1];
                indices[i + 1] = indices[i + 2];
                indices[i + 2] = tmp;
            }

            Vertices = verts;
            Indices = indices;
            IsValid = true;
        }
    }
}
