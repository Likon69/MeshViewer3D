using System;
using OpenTK.Mathematics;
using System.Collections.Generic;
using MeshViewer3D.Data;

namespace MeshViewer3D.Core
{
    /// <summary>
    /// Conteneur de données navmesh complètes pour une tile
    /// Inclut toutes les structures Detour + méthodes de génération de rendu
    /// </summary>
    public class NavMeshData
    {
        // Identification
        public string FilePath { get; set; } = string.Empty;
        public int MapId { get; set; }
        public int TileX { get; set; }
        public int TileY { get; set; }

        // Header Detour
        public MeshHeader Header { get; set; }

        // Géométrie principale
        public Vector3[] Vertices { get; set; } = Array.Empty<Vector3>();
        public NavPoly[] Polys { get; set; } = Array.Empty<NavPoly>();
        public NavLink[] Links { get; set; } = Array.Empty<NavLink>();

        // Détail de mesh (pour rendu haute précision)
        public NavPolyDetail[] DetailMeshes { get; set; } = Array.Empty<NavPolyDetail>();
        public Vector3[] DetailVerts { get; set; } = Array.Empty<Vector3>();
        public byte[] DetailTris { get; set; } = Array.Empty<byte>();

        // Structures d'accélération
        public BVNode[] BVTree { get; set; } = Array.Empty<BVNode>();

        // Connexions spéciales
        public OffMeshConnection[] OffMeshConnections { get; set; } = Array.Empty<OffMeshConnection>();

        /// <summary>
        /// Génère les données de rendu pour OpenGL
        /// Triangularise les polygones convexes en triangles
        /// </summary>
        public (List<Vector3> vertices, List<uint> indices, List<byte> areas) GenerateRenderData()
        {
            var verts = new List<Vector3>();
            var indices = new List<uint>();
            var areas = new List<byte>();

            for (int i = 0; i < Polys.Length; i++)
            {
                var poly = Polys[i];
                if (poly.VertCount < 3) continue;  // Invalide

                byte area = poly.Area;

                // Fan triangulation du polygon convexe
                uint baseIndex = (uint)verts.Count;

                // Ajouter les vertices du polygon
                for (int j = 0; j < poly.VertCount; j++)
                {
                    verts.Add(Vertices[poly.Verts[j]]);
                }

                // Créer triangles en éventail (fan)
                // Polygon convexe: v0-v1-v2, v0-v2-v3, v0-v3-v4, etc.
                for (int j = 1; j < poly.VertCount - 1; j++)
                {
                    indices.Add(baseIndex);           // v0
                    indices.Add(baseIndex + (uint)j);      // vJ
                    indices.Add(baseIndex + (uint)j + 1);  // vJ+1
                    areas.Add(area);
                }
            }

            return (verts, indices, areas);
        }

        /// <summary>
        /// Génère les données de rendu avec detail meshes (meilleure précision)
        /// </summary>
        public (List<Vector3> vertices, List<uint> indices, List<byte> areas) GenerateDetailRenderData()
        {
            var verts = new List<Vector3>();
            var indices = new List<uint>();
            var areas = new List<byte>();

            for (int i = 0; i < Polys.Length; i++)
            {
                var poly = Polys[i];
                byte area = poly.Area;

                // Si le poly a un detail mesh, l'utiliser
                if (i < DetailMeshes.Length)
                {
                    var detail = DetailMeshes[i];
                    uint baseIndex = (uint)verts.Count;

                    // Ajouter vertices du detail mesh
                    for (int j = 0; j < detail.VertCount; j++)
                    {
                        uint vertIndex = detail.VertBase + (uint)j;
                        if (vertIndex < DetailVerts.Length)
                        {
                            verts.Add(DetailVerts[vertIndex]);
                        }
                    }

                    // Ajouter triangles du detail mesh
                    for (int j = 0; j < detail.TriCount; j++)
                    {
                        uint triBase = (detail.TriBase + (uint)j) * 4;
                        if (triBase + 2 < DetailTris.Length)
                        {
                            indices.Add(baseIndex + DetailTris[triBase]);
                            indices.Add(baseIndex + DetailTris[triBase + 1]);
                            indices.Add(baseIndex + DetailTris[triBase + 2]);
                            areas.Add(area);
                        }
                    }
                }
                else
                {
                    // Fallback: fan triangulation normale
                    if (poly.VertCount < 3) continue;

                    uint baseIndex = (uint)verts.Count;
                    for (int j = 0; j < poly.VertCount; j++)
                    {
                        verts.Add(Vertices[poly.Verts[j]]);
                    }

                    for (int j = 1; j < poly.VertCount - 1; j++)
                    {
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + (uint)j);
                        indices.Add(baseIndex + (uint)j + 1);
                        areas.Add(area);
                    }
                }
            }

            return (verts, indices, areas);
        }

        /// <summary>
        /// Génère les edges pour le wireframe
        /// </summary>
        public (List<Vector3> edgeVerts, List<uint> edgeIndices) GenerateWireframeData()
        {
            var edgeVerts = new List<Vector3>();
            var edgeIndices = new List<uint>();
            var processedEdges = new HashSet<(int, int)>();

            for (int i = 0; i < Polys.Length; i++)
            {
                var poly = Polys[i];
                if (poly.VertCount < 3) continue;

                // Pour chaque edge du polygon
                for (int j = 0; j < poly.VertCount; j++)
                {
                    int v0 = poly.Verts[j];
                    int v1 = poly.Verts[(j + 1) % poly.VertCount];

                    // Éviter les doublons (edge A-B = edge B-A)
                    var edge = v0 < v1 ? (v0, v1) : (v1, v0);
                    if (processedEdges.Contains(edge))
                        continue;

                    processedEdges.Add(edge);

                    uint baseIndex = (uint)edgeVerts.Count;
                    edgeVerts.Add(Vertices[v0]);
                    edgeVerts.Add(Vertices[v1]);
                    edgeIndices.Add(baseIndex);
                    edgeIndices.Add(baseIndex + 1);
                }
            }

            return (edgeVerts, edgeIndices);
        }

        /// <summary>
        /// Calcule les statistiques de la tile
        /// </summary>
        public string GetStats()
        {
            int walkablePolys = 0;
            foreach (var poly in Polys)
            {
                if (poly.IsWalkable())
                    walkablePolys++;
            }

            return $"Tile ({TileX},{TileY})\n" +
                   $"Polys: {Polys.Length} ({walkablePolys} walkable)\n" +
                   $"Verts: {Vertices.Length}\n" +
                   $"OffMesh: {OffMeshConnections.Length}\n" +
                   $"Links: {Links.Length}";
        }

        /// <summary>
        /// Retourne le centre de la tile en coordonnées WoW
        /// </summary>
        public Vector3 GetCenterWow()
        {
            return CoordinateSystem.DetourToWow(Header.GetCenter());
        }

        /// <summary>
        /// Retourne le centre de la tile en coordonnées Detour/OpenGL
        /// </summary>
        public Vector3 GetCenterDetour()
        {
            return Header.GetCenter();
        }
    }
}
