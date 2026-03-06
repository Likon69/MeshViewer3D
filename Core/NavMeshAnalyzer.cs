using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using MeshViewer3D.Data;

namespace MeshViewer3D.Core
{
    /// <summary>
    /// NavMesh analysis tools: connected components (flood-fill), degenerate polygon detection,
    /// and coverage statistics.
    /// </summary>
    public static class NavMeshAnalyzer
    {
        /// <summary>
        /// Identifies connected components via flood-fill on the polygon adjacency graph.
        /// Returns an array where componentId[polyIdx] = component index (0-based).
        /// Isolated polygons each get their own component.
        /// </summary>
        public static int[] FindConnectedComponents(NavMeshData mesh, out int componentCount)
        {
            int polyCount = mesh.Polys.Length;
            var component = new int[polyCount];
            Array.Fill(component, -1);
            int nextId = 0;

            for (int i = 0; i < polyCount; i++)
            {
                if (component[i] != -1) continue;
                if (!mesh.Polys[i].IsWalkable())
                {
                    component[i] = -2; // Non-walkable, excluded
                    continue;
                }

                // BFS flood-fill
                var queue = new Queue<int>();
                queue.Enqueue(i);
                component[i] = nextId;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    var poly = mesh.Polys[cur];

                    for (int e = 0; e < poly.VertCount; e++)
                    {
                        ushort nei = poly.Neis[e];
                        if (nei == 0 || (nei & 0x8000) != 0) continue;
                        int neighborIdx = nei - 1;
                        if (neighborIdx < 0 || neighborIdx >= polyCount) continue;
                        if (component[neighborIdx] != -1) continue;
                        if (!mesh.Polys[neighborIdx].IsWalkable()) continue;

                        component[neighborIdx] = nextId;
                        queue.Enqueue(neighborIdx);
                    }
                }

                nextId++;
            }

            componentCount = nextId;
            return component;
        }

        /// <summary>
        /// Finds degenerate polygons: zero/near-zero area or inverted normals (Y pointing down).
        /// Returns list of (polyIndex, reason).
        /// </summary>
        public static List<(int polyIndex, string reason)> FindDegeneratePolygons(NavMeshData mesh)
        {
            var result = new List<(int, string)>();
            const float areaEpsilon = 0.001f;

            for (int i = 0; i < mesh.Polys.Length; i++)
            {
                var poly = mesh.Polys[i];
                if (poly.VertCount < 3)
                {
                    result.Add((i, "Less than 3 vertices"));
                    continue;
                }

                // Compute polygon area and normal using Newell's method
                var normal = Vector3.Zero;
                float area = 0f;

                for (int j = 0; j < poly.VertCount; j++)
                {
                    var v0 = mesh.Vertices[poly.Verts[j]];
                    var v1 = mesh.Vertices[poly.Verts[(j + 1) % poly.VertCount]];
                    normal.X += (v0.Y - v1.Y) * (v0.Z + v1.Z);
                    normal.Y += (v0.Z - v1.Z) * (v0.X + v1.X);
                    normal.Z += (v0.X - v1.X) * (v0.Y + v1.Y);
                }

                area = normal.Length * 0.5f;

                if (area < areaEpsilon)
                {
                    result.Add((i, $"Near-zero area ({area:F6})"));
                    continue;
                }

                // Check normal direction — Detour Y-up, normal should point up (Y > 0)
                if (normal.Y < 0)
                {
                    result.Add((i, "Inverted normal (facing down)"));
                }
            }

            return result;
        }

        /// <summary>
        /// Returns analysis statistics as a formatted string.
        /// </summary>
        public static string GetAnalysisReport(NavMeshData mesh)
        {
            var components = FindConnectedComponents(mesh, out int componentCount);
            var degenerate = FindDegeneratePolygons(mesh);

            // Component size distribution
            var componentSizes = new Dictionary<int, int>();
            int walkableCount = 0;
            int nonWalkableCount = 0;

            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == -2) { nonWalkableCount++; continue; }
                walkableCount++;
                if (!componentSizes.ContainsKey(components[i]))
                    componentSizes[components[i]] = 0;
                componentSizes[components[i]]++;
            }

            // Find largest component
            int largestSize = 0;
            int largestId = -1;
            foreach (var kv in componentSizes)
            {
                if (kv.Value > largestSize) { largestSize = kv.Value; largestId = kv.Key; }
            }

            int isolatedComponents = 0;
            foreach (var kv in componentSizes)
                if (kv.Value == 1) isolatedComponents++;

            var lines = new List<string>
            {
                "=== NavMesh Analysis ===",
                $"Total polys: {mesh.Polys.Length}",
                $"Walkable: {walkableCount} | Non-walkable: {nonWalkableCount}",
                $"Connected components: {componentCount}",
                walkableCount > 0
                    ? $"Largest component: #{largestId} ({largestSize} polys, {100f * largestSize / walkableCount:F1}%)"
                    : "Largest component: none (no walkable polys)",
                $"Isolated (single-poly) components: {isolatedComponents}",
                $"Degenerate polygons: {degenerate.Count}"
            };

            if (degenerate.Count > 0 && degenerate.Count <= 20)
            {
                foreach (var (pi, reason) in degenerate)
                    lines.Add($"  Poly #{pi}: {reason}");
            }
            else if (degenerate.Count > 20)
            {
                for (int i = 0; i < 10; i++)
                    lines.Add($"  Poly #{degenerate[i].polyIndex}: {degenerate[i].reason}");
                lines.Add($"  ... and {degenerate.Count - 10} more");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Generates per-polygon colors for connected component visualization.
        /// Each component gets a distinct color (up to 12 palette colors, then cycling).
        /// Non-walkable polygons are colored dark gray.
        /// Returns componentColors[polyIdx] = Color.
        /// </summary>
        public static System.Drawing.Color[] GenerateComponentColors(NavMeshData mesh, int[] components, int componentCount)
        {
            // 12 distinct colors for readability
            var palette = new System.Drawing.Color[]
            {
                System.Drawing.Color.FromArgb(46, 204, 113),   // Green
                System.Drawing.Color.FromArgb(52, 152, 219),   // Blue
                System.Drawing.Color.FromArgb(231, 76, 60),    // Red
                System.Drawing.Color.FromArgb(241, 196, 15),   // Yellow
                System.Drawing.Color.FromArgb(155, 89, 182),   // Purple
                System.Drawing.Color.FromArgb(230, 126, 34),   // Orange
                System.Drawing.Color.FromArgb(26, 188, 156),   // Teal
                System.Drawing.Color.FromArgb(236, 112, 99),   // Pink
                System.Drawing.Color.FromArgb(93, 173, 226),   // Light blue
                System.Drawing.Color.FromArgb(244, 208, 63),   // Gold
                System.Drawing.Color.FromArgb(175, 122, 197),  // Lavender
                System.Drawing.Color.FromArgb(245, 176, 65),   // Light orange
            };

            var colors = new System.Drawing.Color[mesh.Polys.Length];
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] < 0)
                    colors[i] = System.Drawing.Color.FromArgb(40, 40, 40); // Non-walkable
                else
                    colors[i] = palette[components[i] % palette.Length];
            }
            return colors;
        }
    }
}
