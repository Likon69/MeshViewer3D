using System;
using System.IO;
using System.Linq;
using OpenTK.Mathematics;
using System.Collections.Generic;
using MeshViewer3D.Data;

namespace MeshViewer3D.Core
{
    /// <summary>
    /// Parser complet pour fichiers .mmtile Trinity Core
    /// Format Detour/Recast avec header custom Trinity optionnel
    /// Qualité Honorbuddy - lecture binaire exacte des structures
    /// </summary>
    public class MmtileLoader
    {
        // Magic numbers (little-endian format comme lu par BinaryReader)
        public const uint MMAP_MAGIC = 0x4D4D4150;      // "MMAP" en little-endian (bytes: 50 41 4D 4D)
        public const uint DETOUR_MAGIC_DNAV = 0x444E4156; // "DNAV" en little-endian (bytes: 56 41 4E 44)
        public const uint DETOUR_MAGIC_VAND = 0x5641564E; // "VAND" en little-endian (bytes: 4E 56 41 56)
        
        /// <summary>
        /// Charge une tile navmesh depuis un fichier .mmtile
        /// </summary>
        /// <param name="filePath">Chemin complet vers le fichier .mmtile</param>
        /// <returns>Données navmesh complètes</returns>
        public static NavMeshData LoadTile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Tile file not found: {filePath}");

            using var fs = File.OpenRead(filePath);
            using var br = new BinaryReader(fs);
            
            // Extraire info depuis nom de fichier
            var (mapId, tileX, tileY) = CoordinateSystem.ParseTileFileName(filePath);
            
            // Vérifier si header MMAP Trinity existe (20 bytes total)
            uint firstInt = br.ReadUInt32();
            
            if (firstInt == MMAP_MAGIC)
            {
                // Header MMAP Trinity: 20 bytes total
                // Offset 0-3: magic "MMAP" (déjà lu)
                // Offset 4-7: version
                // Offset 8-11: tileX
                // Offset 12-15: tileY  
                // Offset 16-19: dataSize
                br.ReadUInt32(); // version
                br.ReadUInt32(); // tileX
                br.ReadUInt32(); // tileY
                br.ReadUInt32(); // dataSize
                // Stream est maintenant à offset 20, début du dtMeshHeader
            }
            else
            {
                // Pas d'header MMAP, rewind pour relire le magic Detour
                fs.Position = 0;
            }
            
            // Lire dtMeshHeader (96 bytes)
            var header = ReadMeshHeader(br);
            
            // Valider
            if (!header.IsValid())
            {
                throw new InvalidDataException(
                    $"Invalid Detour magic: 0x{header.Magic:X8}, expected 0x{DETOUR_MAGIC_DNAV:X8} or 0x{DETOUR_MAGIC_VAND:X8}");
            }
            
            // Lire les données dans l'ORDRE EXACT de Detour
            var vertices = ReadVertices(br, header.VertCount);
            var polys = ReadPolys(br, header.PolyCount);
            var links = ReadLinks(br, header.MaxLinkCount);
            var detailMeshes = ReadDetailMeshes(br, header.DetailMeshCount);
            var detailVerts = ReadDetailVerts(br, header.DetailVertCount);
            var detailTris = ReadDetailTris(br, header.DetailTriCount);
            var bvTree = ReadBVNodes(br, header.BvNodeCount);
            var offMesh = ReadOffMeshConnections(br, header.OffMeshConCount);
            
            // Debug: compter les area types
            var areaCounts = new Dictionary<byte, int>();
            foreach (var poly in polys)
            {
                byte area = poly.Area;
                if (!areaCounts.ContainsKey(area))
                    areaCounts[area] = 0;
                areaCounts[area]++;
            }
            
            Console.WriteLine($"\nTile ({tileX},{tileY}) Area distribution:");
            foreach (var kvp in areaCounts.OrderBy(x => x.Key))
            {
                string name = Data.AreaTypeInfo.GetName(kvp.Key);
                Console.WriteLine($"  Area {kvp.Key} ({name}): {kvp.Value} polys");
            }
            
            // Créer NavMeshData
            var data = new NavMeshData
            {
                FilePath = filePath,
                MapId = mapId,
                TileX = tileX,
                TileY = tileY,
                Header = header,
                Vertices = vertices,
                Polys = polys,
                Links = links,
                DetailMeshes = detailMeshes,
                DetailVerts = detailVerts,
                DetailTris = detailTris,
                BVTree = bvTree,
                OffMeshConnections = offMesh
            };
            
            return data;
        }

        /// <summary>
        /// Lit le header Detour (96 bytes)
        /// </summary>
        private static MeshHeader ReadMeshHeader(BinaryReader br)
        {
            return new MeshHeader
            {
                Magic = br.ReadUInt32(),           // +0
                Version = br.ReadInt32(),          // +4
                TileX = br.ReadInt32(),            // +8
                TileY = br.ReadInt32(),            // +12
                Layer = br.ReadInt32(),            // +16
                UserId = br.ReadUInt32(),          // +20
                PolyCount = br.ReadInt32(),        // +24
                VertCount = br.ReadInt32(),        // +28
                MaxLinkCount = br.ReadInt32(),     // +32
                DetailMeshCount = br.ReadInt32(),  // +36
                DetailVertCount = br.ReadInt32(),  // +40
                DetailTriCount = br.ReadInt32(),   // +44
                BvNodeCount = br.ReadInt32(),      // +48
                OffMeshConCount = br.ReadInt32(),  // +52
                OffMeshBase = br.ReadInt32(),      // +56
                WalkableHeight = br.ReadSingle(),  // +60
                WalkableRadius = br.ReadSingle(),  // +64
                WalkableClimb = br.ReadSingle(),   // +68
                BMin = new Vector3(                // +72
                    br.ReadSingle(),
                    br.ReadSingle(),
                    br.ReadSingle()
                ),
                BMax = new Vector3(                // +84
                    br.ReadSingle(),
                    br.ReadSingle(),
                    br.ReadSingle()
                ),
                BvQuantFactor = br.ReadSingle()    // +96
            };
        }

        /// <summary>
        /// Lit les vertices (3 floats par vertex = 12 bytes)
        /// IMPORTANT: Les vertices sont en coordonnées Detour!
        /// </summary>
        private static Vector3[] ReadVertices(BinaryReader br, int count)
        {
            var verts = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                verts[i] = new Vector3(
                    br.ReadSingle(),  // X (Detour)
                    br.ReadSingle(),  // Y (Detour = hauteur!)
                    br.ReadSingle()   // Z (Detour)
                );
            }
            return verts;
        }

        /// <summary>
        /// Lit les polygones (32 bytes par poly)
        /// </summary>
        private static NavPoly[] ReadPolys(BinaryReader br, int count)
        {
            var polys = new NavPoly[count];
            for (int i = 0; i < count; i++)
            {
                polys[i] = NavPoly.Create();
                polys[i].FirstLink = br.ReadUInt32();
                
                // 6 vertex indices (12 bytes)
                for (int j = 0; j < NavPoly.MAX_VERTS; j++)
                    polys[i].Verts[j] = br.ReadUInt16();
                
                // 6 neighbor indices (12 bytes)
                for (int j = 0; j < NavPoly.MAX_VERTS; j++)
                    polys[i].Neis[j] = br.ReadUInt16();
                
                polys[i].Flags = br.ReadUInt16();
                polys[i].VertCount = br.ReadByte();
                polys[i].AreaAndType = br.ReadByte();
            }
            return polys;
        }

        /// <summary>
        /// Lit les liens entre polygones (16 bytes par link avec DT_POLYREF64)
        /// </summary>
        private static NavLink[] ReadLinks(BinaryReader br, int count)
        {
            var links = new NavLink[count];
            for (int i = 0; i < count; i++)
            {
                links[i] = new NavLink
                {
                    Ref = br.ReadUInt64(),
                    Next = br.ReadUInt32(),
                    Edge = br.ReadByte(),
                    Side = br.ReadByte(),
                    BMin = br.ReadByte(),
                    BMax = br.ReadByte()
                };
            }
            return links;
        }

        /// <summary>
        /// Lit les detail meshes (12 bytes par mesh avec padding)
        /// </summary>
        private static NavPolyDetail[] ReadDetailMeshes(BinaryReader br, int count)
        {
            var details = new NavPolyDetail[count];
            for (int i = 0; i < count; i++)
            {
                details[i] = new NavPolyDetail
                {
                    VertBase = br.ReadUInt32(),
                    TriBase = br.ReadUInt32(),
                    VertCount = br.ReadByte(),
                    TriCount = br.ReadByte()
                };
                br.ReadBytes(2); // Padding
            }
            return details;
        }

        /// <summary>
        /// Lit les detail vertices (3 floats = 12 bytes)
        /// </summary>
        private static Vector3[] ReadDetailVerts(BinaryReader br, int count)
        {
            var verts = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                verts[i] = new Vector3(
                    br.ReadSingle(),
                    br.ReadSingle(),
                    br.ReadSingle()
                );
            }
            return verts;
        }

        /// <summary>
        /// Lit les detail triangles (4 bytes par tri: 3 indices + flags)
        /// </summary>
        private static byte[] ReadDetailTris(BinaryReader br, int count)
        {
            // 4 bytes per triangle (3 vertex indices + flags byte)
            return br.ReadBytes(count * 4);
        }

        /// <summary>
        /// Lit les nœuds du BV tree (16 bytes par node)
        /// </summary>
        private static BVNode[] ReadBVNodes(BinaryReader br, int count)
        {
            var nodes = new BVNode[count];
            for (int i = 0; i < count; i++)
            {
                nodes[i] = BVNode.Create();
                
                // BMin (3 ushorts = 6 bytes)
                for (int j = 0; j < 3; j++)
                    nodes[i].BMin[j] = br.ReadUInt16();
                
                // BMax (3 ushorts = 6 bytes)
                for (int j = 0; j < 3; j++)
                    nodes[i].BMax[j] = br.ReadUInt16();
                
                nodes[i].Index = br.ReadInt32();
            }
            return nodes;
        }

        /// <summary>
        /// Lit les OffMesh connections (36 bytes par connection)
        /// </summary>
        private static OffMeshConnection[] ReadOffMeshConnections(BinaryReader br, int count)
        {
            var cons = new OffMeshConnection[count];
            for (int i = 0; i < count; i++)
            {
                cons[i] = new OffMeshConnection
                {
                    Start = new Vector3(
                        br.ReadSingle(),
                        br.ReadSingle(),
                        br.ReadSingle()
                    ),
                    End = new Vector3(
                        br.ReadSingle(),
                        br.ReadSingle(),
                        br.ReadSingle()
                    ),
                    Radius = br.ReadSingle(),
                    Poly = br.ReadUInt16(),
                    Flags = br.ReadByte(),
                    Side = br.ReadByte(),
                    UserId = br.ReadUInt32()
                };
            }
            return cons;
        }
    }
}
