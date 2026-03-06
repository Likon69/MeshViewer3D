using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;
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

                // Ajouter les vertices du polygon (one area per vertex)
                for (int j = 0; j < poly.VertCount; j++)
                {
                    verts.Add(Vertices[poly.Verts[j]]);
                    areas.Add(area);
                }

                // Créer triangles en éventail (fan)
                // Polygon convexe: v0-v1-v2, v0-v2-v3, v0-v3-v4, etc.
                for (int j = 1; j < poly.VertCount - 1; j++)
                {
                    indices.Add(baseIndex);           // v0
                    indices.Add(baseIndex + (uint)j);      // vJ
                    indices.Add(baseIndex + (uint)j + 1);  // vJ+1
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

                    // Ajouter vertices du detail mesh (one area per vertex)
                    for (int j = 0; j < detail.VertCount; j++)
                    {
                        uint vertIndex = detail.VertBase + (uint)j;
                        if (vertIndex < DetailVerts.Length)
                        {
                            verts.Add(DetailVerts[vertIndex]);
                            areas.Add(area);
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
                        areas.Add(area);
                    }

                    for (int j = 1; j < poly.VertCount - 1; j++)
                    {
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + (uint)j);
                        indices.Add(baseIndex + (uint)j + 1);
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

        /// <summary>
        /// Fusionne plusieurs tiles en un seul NavMeshData pour le rendu.
        /// Les indices de vertices sont réindexés pour éviter les collisions.
        /// ATTENTION: NavPoly.Verts est ushort[] => il faut cloner l'array avant modification.
        /// </summary>
        public static NavMeshData Merge(IEnumerable<NavMeshData> tiles)
        {
            var list = tiles.ToList();
            if (list.Count == 0) throw new ArgumentException("Cannot merge empty tile list", nameof(tiles));
            if (list.Count == 1) return list[0];

            // Safety check: ushort indices max 65535 — abort instead of silently producing corrupt geometry
            int totalVerts = 0;
            foreach (var t in list) totalVerts += t.Vertices.Length;
            if (totalVerts > 60000)
                throw new InvalidOperationException(
                    $"Cannot merge {list.Count} tiles: {totalVerts} total vertices exceed the ushort index limit (60000). " +
                    "Load fewer tiles at once.");

            var mergedVerts = new List<Vector3>(totalVerts);
            var mergedPolys = new List<NavPoly>();
            var mergedOffMesh = new List<OffMeshConnection>();

            // Track which edges were external (for cross-tile reconnection)
            // Key: merged poly index, Value: list of edge indices that were external
            var externalEdges = new List<(int polyIdx, int edgeIdx)>();

            // Compute merged bounding box
            var bmin = list[0].Header.BMin;
            var bmax = list[0].Header.BMax;

            foreach (var tile in list)
            {
                int vertOffset = mergedVerts.Count;
                int polyOffset = mergedPolys.Count;
                mergedVerts.AddRange(tile.Vertices);

                foreach (var poly in tile.Polys)
                {
                    int mergedPolyIdx = mergedPolys.Count;
                    // NavPoly.Verts and Neis are ushort[] (reference type) — must deep-copy before offsetting
                    var newPoly = poly;
                    newPoly.Verts = (ushort[])poly.Verts.Clone();
                    newPoly.Neis = (ushort[])poly.Neis.Clone();
                    for (int i = 0; i < newPoly.VertCount; i++)
                        newPoly.Verts[i] = (ushort)(newPoly.Verts[i] + vertOffset);
                    // Offset neighbor indices: Detour stores polyIdx+1 (0=no neighbor, ≥0x8000=external)
                    for (int i = 0; i < newPoly.VertCount; i++)
                    {
                        ushort nei = newPoly.Neis[i];
                        if (nei != 0 && (nei & 0x8000) == 0)
                        {
                            int offsetIdx = nei - 1 + polyOffset;
                            if (offsetIdx >= 0 && offsetIdx < mergedPolys.Count + tile.Polys.Length)
                                newPoly.Neis[i] = (ushort)(nei + polyOffset);
                            else
                                newPoly.Neis[i] = 0; // Invalid neighbor — block link
                        }
                        else if ((nei & 0x8000) != 0)
                        {
                            newPoly.Neis[i] = 0; // zero for now — will reconnect below
                            externalEdges.Add((mergedPolyIdx, i));
                        }
                    }
                    mergedPolys.Add(newPoly);
                }

                mergedOffMesh.AddRange(tile.OffMeshConnections);

                // Expand bounding box
                var h = tile.Header;
                if (h.BMin.X < bmin.X) bmin.X = h.BMin.X;
                if (h.BMin.Y < bmin.Y) bmin.Y = h.BMin.Y;
                if (h.BMin.Z < bmin.Z) bmin.Z = h.BMin.Z;
                if (h.BMax.X > bmax.X) bmax.X = h.BMax.X;
                if (h.BMax.Y > bmax.Y) bmax.Y = h.BMax.Y;
                if (h.BMax.Z > bmax.Z) bmax.Z = h.BMax.Z;
            }

            // Convert to array for mutation
            var polysArray = mergedPolys.ToArray();
            var vertsArray = mergedVerts.ToArray();

            // Cross-tile reconnection: for each external edge, find a matching polygon
            // sharing the same two border vertex positions in another tile.
            if (externalEdges.Count > 0)
                ReconnectCrossTileEdges(polysArray, vertsArray, externalEdges);

            var mergedHeader = list[0].Header;
            mergedHeader.BMin = bmin;
            mergedHeader.BMax = bmax;
            mergedHeader.PolyCount = polysArray.Length;
            mergedHeader.VertCount = vertsArray.Length;
            mergedHeader.OffMeshConCount = mergedOffMesh.Count;

            return new NavMeshData
            {
                FilePath = $"[Merged: {list.Count} tiles]",
                MapId = list[0].MapId,
                TileX = list[0].TileX,
                TileY = list[0].TileY,
                Header = mergedHeader,
                Vertices = vertsArray,
                Polys = polysArray,
                OffMeshConnections = mergedOffMesh.ToArray(),
                Links = Array.Empty<NavLink>(),
                DetailMeshes = Array.Empty<NavPolyDetail>(),
                DetailVerts = Array.Empty<Vector3>(),
                DetailTris = Array.Empty<byte>(),
                BVTree = Array.Empty<BVNode>(),
            };
        }

        /// <summary>
        /// Reconnects external cross-tile edges after merge by matching border vertex positions.
        /// For each edge that was an external link (Neis >= 0x8000, now zeroed), find another polygon
        /// that has an edge with matching vertex positions (within epsilon) and set them as neighbors.
        /// </summary>
        private static void ReconnectCrossTileEdges(NavPoly[] polys, Vector3[] verts,
            List<(int polyIdx, int edgeIdx)> externalEdges)
        {
            const float epsilon = 0.01f;
            const float epsSq = epsilon * epsilon;

            // Build spatial index: for each external edge, store its two vertex positions
            // Then match pairs with coincident vertices
            var edgeList = new List<(int polyIdx, int edgeIdx, Vector3 v0, Vector3 v1)>(externalEdges.Count);

            foreach (var (polyIdx, edgeIdx) in externalEdges)
            {
                var poly = polys[polyIdx];
                int nextEdge = (edgeIdx + 1) % poly.VertCount;
                var v0 = verts[poly.Verts[edgeIdx]];
                var v1 = verts[poly.Verts[nextEdge]];
                edgeList.Add((polyIdx, edgeIdx, v0, v1));
            }

            // Sort by v0.X to enable range-limited search
            edgeList.Sort((a, b) => a.v0.X.CompareTo(b.v0.X));

            // For each edge, find a matching opposite edge (v0↔v1 or v1↔v0)
            for (int i = 0; i < edgeList.Count; i++)
            {
                var (piA, eiA, v0a, v1a) = edgeList[i];
                if (polys[piA].Neis[eiA] != 0) continue; // Already reconnected

                for (int j = i + 1; j < edgeList.Count; j++)
                {
                    var (piB, eiB, v0b, v1b) = edgeList[j];

                    // Early exit: if v0b.X is too far from both v0a.X and v1a.X, stop
                    if (v0b.X - MathF.Max(v0a.X, v1a.X) > epsilon) break;

                    if (piA == piB) continue; // Same polygon
                    if (polys[piB].Neis[eiB] != 0) continue; // Already reconnected

                    // Check if edges match: (v0a≈v0b && v1a≈v1b) || (v0a≈v1b && v1a≈v0b)
                    bool matchDirect = (v0a - v0b).LengthSquared < epsSq && (v1a - v1b).LengthSquared < epsSq;
                    bool matchFlipped = (v0a - v1b).LengthSquared < epsSq && (v1a - v0b).LengthSquared < epsSq;

                    if (matchDirect || matchFlipped)
                    {
                        // Reconnect: Detour Neis stores polyIdx+1
                        polys[piA].Neis[eiA] = (ushort)(piB + 1);
                        polys[piB].Neis[eiB] = (ushort)(piA + 1);
                        break;
                    }
                }
            }
        }
    }
}
