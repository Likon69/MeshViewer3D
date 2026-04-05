using System;
using System.IO;
using OpenTK.Mathematics;
using MeshViewer3D.Data;

namespace MeshViewer3D.Core
{
    /// <summary>
    /// Écrit un NavMeshData en fichier .mmtile — miroir exact de MmtileLoader.
    /// Supporte la sauvegarde de tiles modifiées (blackspots bakés, area types changés).
    /// Format: MMAP header (20 bytes) + données Detour brutes.
    /// </summary>
    public static class MmtileWriter
    {
        // MMAP header constants (from MoveMapSharedDefines.h)
        private const uint MMAP_MAGIC = 0x4D4D4150;       // "MMAP"
        private const uint DT_NAVMESH_VERSION = 7;         // Detour version
        private const uint MMAP_VERSION = 4;               // MangosTwo MMAP version

        /// <summary>
        /// Sauvegarde un NavMeshData en fichier .mmtile.
        /// Format identique à celui produit par mmap-extractor.exe / MapBuilder.cpp.
        /// </summary>
        /// <param name="mesh">Données navmesh à écrire</param>
        /// <param name="outputPath">Chemin du fichier de sortie</param>
        /// <param name="usesLiquids">Flag liquides (default true)</param>
        public static void Save(NavMeshData mesh, string outputPath, bool usesLiquids = true)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path required", nameof(outputPath));

            // Ensure directory exists
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var fs = File.Create(outputPath);
            using var bw = new BinaryWriter(fs);

            // Calculate Detour data size (everything after MMAP header)
            uint detourDataSize = CalculateDetourDataSize(mesh.Header);

            // 1) Write MMAP header (20 bytes) — matches MmapTileHeader struct
            WriteMmapHeader(bw, detourDataSize, usesLiquids);

            // 2) Write dtMeshHeader (100 bytes)
            WriteMeshHeader(bw, mesh.Header);

            // 3) Write data sections in EXACT Detour order
            WriteVertices(bw, mesh.Vertices);
            WritePolys(bw, mesh.Polys);
            WriteLinks(bw, mesh.Links);
            WriteDetailMeshes(bw, mesh.DetailMeshes);
            WriteDetailVerts(bw, mesh.DetailVerts);
            WriteDetailTris(bw, mesh.DetailTris);
            WriteBVNodes(bw, mesh.BVTree);
            WriteOffMeshConnections(bw, mesh.OffMeshConnections);
        }

        /// <summary>
        /// Calculates the total size of the Detour tile data (after MMAP header).
        /// Must match what dtCreateNavMeshData produces.
        /// </summary>
        private static uint CalculateDetourDataSize(MeshHeader h)
        {
            uint size = 100;                                  // dtMeshHeader
            size += (uint)(h.VertCount * 12);                 // Vertices: 3 floats × 4 bytes
            size += (uint)(h.PolyCount * 32);                 // Polys: fixed 32 bytes each
            size += (uint)(h.MaxLinkCount * 16);              // Links: 16 bytes each (DT_POLYREF64: ref=8 + next=4 + 4 chars)
            size += (uint)(h.DetailMeshCount * 12);           // DetailMeshes: 8 data + 2 counts + 2 padding
            size += (uint)(h.DetailVertCount * 12);           // DetailVerts: 3 floats × 4 bytes
            size += (uint)(h.DetailTriCount * 4);             // DetailTris: 4 bytes each
            size += (uint)(h.BvNodeCount * 16);               // BVNodes: 6+6+4 = 16 bytes each
            size += (uint)(h.OffMeshConCount * 36);           // OffMesh: 36 bytes each
            return size;
        }

        /// <summary>
        /// Writes the 20-byte MMAP header (MmapTileHeader from MoveMapSharedDefines.h).
        /// Layout: magic(4) + dtVersion(4) + mmapVersion(4) + size(4) + usesLiquids(4)
        /// </summary>
        private static void WriteMmapHeader(BinaryWriter bw, uint detourDataSize, bool usesLiquids)
        {
            bw.Write(MMAP_MAGIC);              // offset 0:  "MMAP"
            bw.Write(DT_NAVMESH_VERSION);      // offset 4:  Detour version (7)
            bw.Write(MMAP_VERSION);            // offset 8:  MMAP version (4)
            bw.Write(detourDataSize);          // offset 12: Detour data size
            bw.Write(usesLiquids ? 1u : 0u);   // offset 16: liquids flag (bitfield + 3 bytes padding)
        }

        /// <summary>
        /// Writes dtMeshHeader (100 bytes) — exact mirror of MmtileLoader.ReadMeshHeader.
        /// </summary>
        private static void WriteMeshHeader(BinaryWriter bw, MeshHeader h)
        {
            bw.Write(h.Magic);                 // +0
            bw.Write(h.Version);               // +4
            bw.Write(h.TileX);                 // +8
            bw.Write(h.TileY);                 // +12
            bw.Write(h.Layer);                 // +16
            bw.Write(h.UserId);                // +20
            bw.Write(h.PolyCount);             // +24
            bw.Write(h.VertCount);             // +28
            bw.Write(h.MaxLinkCount);          // +32
            bw.Write(h.DetailMeshCount);       // +36
            bw.Write(h.DetailVertCount);       // +40
            bw.Write(h.DetailTriCount);        // +44
            bw.Write(h.BvNodeCount);           // +48
            bw.Write(h.OffMeshConCount);       // +52
            bw.Write(h.OffMeshBase);           // +56
            bw.Write(h.WalkableHeight);        // +60
            bw.Write(h.WalkableRadius);        // +64
            bw.Write(h.WalkableClimb);         // +68
            bw.Write(h.BMin.X);                // +72
            bw.Write(h.BMin.Y);                // +76
            bw.Write(h.BMin.Z);                // +80
            bw.Write(h.BMax.X);                // +84
            bw.Write(h.BMax.Y);                // +88
            bw.Write(h.BMax.Z);                // +92
            bw.Write(h.BvQuantFactor);         // +96
        }

        /// <summary>
        /// Writes vertices (3 floats = 12 bytes each).
        /// </summary>
        private static void WriteVertices(BinaryWriter bw, Vector3[] vertices)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                bw.Write(vertices[i].X);
                bw.Write(vertices[i].Y);
                bw.Write(vertices[i].Z);
            }
        }

        /// <summary>
        /// Writes polygons (32 bytes each) — exact mirror of ReadPolys.
        /// This is where blackspot modifications (AreaAndType changes) are persisted.
        /// </summary>
        private static void WritePolys(BinaryWriter bw, NavPoly[] polys)
        {
            for (int i = 0; i < polys.Length; i++)
            {
                bw.Write(polys[i].FirstLink);

                // 6 vertex indices (12 bytes)
                for (int j = 0; j < NavPoly.MAX_VERTS; j++)
                    bw.Write(polys[i].Verts[j]);

                // 6 neighbor indices (12 bytes)
                for (int j = 0; j < NavPoly.MAX_VERTS; j++)
                    bw.Write(polys[i].Neis[j]);

                bw.Write(polys[i].Flags);
                bw.Write(polys[i].VertCount);
                bw.Write(polys[i].AreaAndType);
            }
        }

        /// <summary>
        /// Writes links (16 bytes each with DT_POLYREF64) — exact mirror of ReadLinks.
        /// </summary>
        private static void WriteLinks(BinaryWriter bw, NavLink[] links)
        {
            for (int i = 0; i < links.Length; i++)
            {
                bw.Write(links[i].Ref);
                bw.Write(links[i].Next);
                bw.Write(links[i].Edge);
                bw.Write(links[i].Side);
                bw.Write(links[i].BMin);
                bw.Write(links[i].BMax);
            }
        }

        /// <summary>
        /// Writes detail meshes (12 bytes each with 2-byte padding) — exact mirror of ReadDetailMeshes.
        /// </summary>
        private static void WriteDetailMeshes(BinaryWriter bw, NavPolyDetail[] details)
        {
            for (int i = 0; i < details.Length; i++)
            {
                bw.Write(details[i].VertBase);
                bw.Write(details[i].TriBase);
                bw.Write(details[i].VertCount);
                bw.Write(details[i].TriCount);
                bw.Write((ushort)0); // 2 bytes padding
            }
        }

        /// <summary>
        /// Writes detail vertices (3 floats = 12 bytes each).
        /// </summary>
        private static void WriteDetailVerts(BinaryWriter bw, Vector3[] verts)
        {
            for (int i = 0; i < verts.Length; i++)
            {
                bw.Write(verts[i].X);
                bw.Write(verts[i].Y);
                bw.Write(verts[i].Z);
            }
        }

        /// <summary>
        /// Writes detail triangles (4 bytes per tri: 3 indices + flags).
        /// Raw byte array — written as-is.
        /// </summary>
        private static void WriteDetailTris(BinaryWriter bw, byte[] detailTris)
        {
            bw.Write(detailTris);
        }

        /// <summary>
        /// Writes BV tree nodes (16 bytes each) — exact mirror of ReadBVNodes.
        /// </summary>
        private static void WriteBVNodes(BinaryWriter bw, BVNode[] nodes)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                // BMin (3 ushorts = 6 bytes)
                for (int j = 0; j < 3; j++)
                    bw.Write(nodes[i].BMin[j]);

                // BMax (3 ushorts = 6 bytes)
                for (int j = 0; j < 3; j++)
                    bw.Write(nodes[i].BMax[j]);

                bw.Write(nodes[i].Index);
            }
        }

        /// <summary>
        /// Writes off-mesh connections (36 bytes each) — exact mirror of ReadOffMeshConnections.
        /// </summary>
        private static void WriteOffMeshConnections(BinaryWriter bw, OffMeshConnection[] cons)
        {
            for (int i = 0; i < cons.Length; i++)
            {
                bw.Write(cons[i].Start.X);
                bw.Write(cons[i].Start.Y);
                bw.Write(cons[i].Start.Z);
                bw.Write(cons[i].End.X);
                bw.Write(cons[i].End.Y);
                bw.Write(cons[i].End.Z);
                bw.Write(cons[i].Radius);
                bw.Write(cons[i].Poly);
                bw.Write(cons[i].Flags);
                bw.Write(cons[i].Side);
                bw.Write(cons[i].UserId);
            }
        }
    }
}
