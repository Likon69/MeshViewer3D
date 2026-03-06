using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using MeshViewer3D.Data;

namespace MeshViewer3D.Core
{
    /// <summary>
    /// A* pathfinder on the Detour navmesh polygon graph.
    /// Traverses polygon neighbors via Neis[] array (Detour encoding: 0=wall, ≥0x8000=external, else=polyIdx+1).
    /// Includes off-mesh connection traversal and Simple Stupid Funnel for path smoothing.
    /// </summary>
    public static class NavMeshPathfinder
    {
        /// <summary>
        /// Finds a path from startPos (on startPoly) to endPos (on endPoly).
        /// Returns a list of waypoints (in Detour coordinates) or null if no path exists.
        /// Integrates off-mesh connections and applies funnel string-pulling for smooth paths.
        /// </summary>
        public static List<Vector3>? FindPath(NavMeshData mesh, Vector3 startPos, int startPoly, Vector3 endPos, int endPoly)
        {
            if (startPoly == endPoly)
                return new List<Vector3> { startPos, endPos };

            int polyCount = mesh.Polys.Length;
            if (startPoly < 0 || startPoly >= polyCount || endPoly < 0 || endPoly >= polyCount)
                return null;

            // Build off-mesh lookup: for each polygon, list of (targetPoly, offMeshIndex)
            var offMeshByPoly = BuildOffMeshLookup(mesh);

            // A* using PriorityQueue (.NET 6)
            var openSet = new PriorityQueue<int, float>();
            var gScore = new float[polyCount];
            var cameFrom = new int[polyCount];
            var cameFromEdge = new int[polyCount]; // -1 = normal Neis edge, >=0 = offmesh index
            var visited = new bool[polyCount];

            Array.Fill(gScore, float.MaxValue);
            Array.Fill(cameFrom, -1);
            Array.Fill(cameFromEdge, -1);

            gScore[startPoly] = 0;
            openSet.Enqueue(startPoly, Heuristic(mesh, startPoly, endPoly));

            while (openSet.Count > 0)
            {
                int current = openSet.Dequeue();

                if (current == endPoly)
                    return ReconstructPath(mesh, cameFrom, cameFromEdge, startPos, endPos, startPoly, endPoly);

                if (visited[current]) continue;
                visited[current] = true;

                var poly = mesh.Polys[current];

                // Standard Neis[] neighbor traversal
                for (int e = 0; e < poly.VertCount; e++)
                {
                    ushort nei = poly.Neis[e];
                    if (nei == 0 || (nei & 0x8000) != 0) continue;

                    int neighborIdx = nei - 1; // Detour: neis stores polyIdx + 1
                    if (neighborIdx < 0 || neighborIdx >= polyCount) continue;
                    if (visited[neighborIdx]) continue;
                    if (!mesh.Polys[neighborIdx].IsWalkable()) continue;

                    float edgeCost = EdgeCost(mesh, current, neighborIdx);
                    float tentativeG = gScore[current] + edgeCost;

                    if (tentativeG < gScore[neighborIdx])
                    {
                        cameFrom[neighborIdx] = current;
                        cameFromEdge[neighborIdx] = -1; // normal edge
                        gScore[neighborIdx] = tentativeG;
                        openSet.Enqueue(neighborIdx, tentativeG + Heuristic(mesh, neighborIdx, endPoly));
                    }
                }

                // Off-mesh connection traversal
                if (offMeshByPoly.TryGetValue(current, out var offMeshEdges))
                {
                    foreach (var (targetPoly, offMeshIdx) in offMeshEdges)
                    {
                        if (targetPoly < 0 || targetPoly >= polyCount) continue;
                        if (visited[targetPoly]) continue;
                        if (!mesh.Polys[targetPoly].IsWalkable()) continue;

                        var conn = mesh.OffMeshConnections[offMeshIdx];
                        float cost = conn.GetDistance();
                        float tentativeG = gScore[current] + cost;

                        if (tentativeG < gScore[targetPoly])
                        {
                            cameFrom[targetPoly] = current;
                            cameFromEdge[targetPoly] = offMeshIdx;
                            gScore[targetPoly] = tentativeG;
                            openSet.Enqueue(targetPoly, tentativeG + Heuristic(mesh, targetPoly, endPoly));
                        }
                    }
                }
            }

            return null; // No path
        }

        /// <summary>
        /// Builds a lookup of off-mesh connections indexed by source polygon.
        /// For bidirectional connections, adds entries in both directions.
        /// </summary>
        private static Dictionary<int, List<(int targetPoly, int offMeshIdx)>> BuildOffMeshLookup(NavMeshData mesh)
        {
            var lookup = new Dictionary<int, List<(int, int)>>();
            if (mesh.OffMeshConnections.Length == 0) return lookup;

            for (int i = 0; i < mesh.OffMeshConnections.Length; i++)
            {
                var conn = mesh.OffMeshConnections[i];
                int startPoly = FindClosestPoly(mesh, conn.Start);
                int endPoly = FindClosestPoly(mesh, conn.End);
                if (startPoly < 0 || endPoly < 0 || startPoly == endPoly) continue;

                if (!lookup.ContainsKey(startPoly))
                    lookup[startPoly] = new List<(int, int)>();
                lookup[startPoly].Add((endPoly, i));

                if (conn.IsBidirectional)
                {
                    if (!lookup.ContainsKey(endPoly))
                        lookup[endPoly] = new List<(int, int)>();
                    lookup[endPoly].Add((startPoly, i));
                }
            }
            return lookup;
        }

        /// <summary>
        /// Finds the polygon containing a given position using point-in-polygon test with Y-bounds.
        /// First performs 2D containment (XZ) + Y-bounds check, then falls back to nearest centroid.
        /// Returns -1 if no walkable polygon is close enough.
        /// </summary>
        private static int FindClosestPoly(NavMeshData mesh, Vector3 pos)
        {
            // Pass 1: Point-in-polygon (XZ) + Y-bounds — definitive containment
            for (int i = 0; i < mesh.Polys.Length; i++)
            {
                if (!mesh.Polys[i].IsWalkable()) continue;
                if (PointInPolygonXZ(mesh, i, pos) && IsWithinYBounds(mesh, i, pos))
                    return i;
            }

            // Pass 2: Fallback to nearest centroid distance
            int bestPoly = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < mesh.Polys.Length; i++)
            {
                if (!mesh.Polys[i].IsWalkable()) continue;
                float dist = (GetPolyCentroid(mesh, i) - pos).LengthSquared;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPoly = i;
                }
            }
            // Only accept if reasonably close (within ~50 units)
            return bestDist < 2500f ? bestPoly : -1;
        }

        /// <summary>
        /// Ray-casting point-in-polygon test on XZ plane (ignores Y).
        /// </summary>
        private static bool PointInPolygonXZ(NavMeshData mesh, int polyIndex, Vector3 testPoint)
        {
            var poly = mesh.Polys[polyIndex];
            if (poly.VertCount < 3) return false;

            float px = testPoint.X;
            float pz = testPoint.Z;
            bool inside = false;

            for (int i = 0; i < poly.VertCount; i++)
            {
                int j = (i + 1) % poly.VertCount;
                var vi = mesh.Vertices[poly.Verts[i]];
                var vj = mesh.Vertices[poly.Verts[j]];

                bool intersects = ((vi.Z > pz) != (vj.Z > pz)) &&
                                 (px < (vj.X - vi.X) * (pz - vi.Z) / (vj.Z - vi.Z) + vi.X);
                if (intersects)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Checks if a position's Y coordinate is within the polygon's vertex Y range (±tolerance).
        /// Prevents selecting a bridge polygon when the point is at ground level.
        /// </summary>
        private static bool IsWithinYBounds(NavMeshData mesh, int polyIndex, Vector3 pos)
        {
            var poly = mesh.Polys[polyIndex];
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            for (int i = 0; i < poly.VertCount; i++)
            {
                float y = mesh.Vertices[poly.Verts[i]].Y;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            const float tolerance = 2.0f;
            return pos.Y >= minY - tolerance && pos.Y <= maxY + tolerance;
        }

        private static float Heuristic(NavMeshData mesh, int polyA, int polyB)
        {
            return (GetPolyCentroid(mesh, polyA) - GetPolyCentroid(mesh, polyB)).Length;
        }

        private static float EdgeCost(NavMeshData mesh, int fromPoly, int toPoly)
        {
            float dist = (GetPolyCentroid(mesh, fromPoly) - GetPolyCentroid(mesh, toPoly)).Length;
            byte area = mesh.Polys[toPoly].Area;
            if (area != 1) dist *= 1.5f; // Non-ground areas have slight penalty
            return dist;
        }

        public static Vector3 GetPolyCentroid(NavMeshData mesh, int polyIndex)
        {
            var poly = mesh.Polys[polyIndex];
            if (poly.VertCount == 0) return Vector3.Zero;
            var center = Vector3.Zero;
            for (int i = 0; i < poly.VertCount; i++)
                center += mesh.Vertices[poly.Verts[i]];
            return center / poly.VertCount;
        }

        private static List<Vector3> ReconstructPath(NavMeshData mesh, int[] cameFrom, int[] cameFromEdge,
            Vector3 startPos, Vector3 endPos, int startPoly, int endPoly)
        {
            // Build polygon corridor
            var polyPath = new List<int>();
            int current = endPoly;
            while (current != -1)
            {
                polyPath.Add(current);
                current = cameFrom[current];
            }
            polyPath.Reverse();

            if (polyPath.Count < 2)
                return new List<Vector3> { startPos, endPos };

            // Build portal list and apply funnel algorithm
            var portals = BuildPortals(mesh, polyPath, cameFromEdge, startPos, endPos);
            var waypoints = FunnelSmooth(portals, startPos, endPos);
            return waypoints;
        }

        /// <summary>
        /// Builds the portal (left/right edge endpoints) list for the funnel algorithm.
        /// For off-mesh connections, inserts the connection start/end as degenerate portals.
        /// </summary>
        private static List<(Vector3 left, Vector3 right)> BuildPortals(
            NavMeshData mesh, List<int> polyPath, int[] cameFromEdge,
            Vector3 startPos, Vector3 endPos)
        {
            var portals = new List<(Vector3 left, Vector3 right)>();

            // First portal: start point (degenerate)
            portals.Add((startPos, startPos));

            for (int i = 0; i < polyPath.Count - 1; i++)
            {
                int fromPoly = polyPath[i];
                int toPoly = polyPath[i + 1];
                int edgeType = cameFromEdge[toPoly];

                if (edgeType >= 0)
                {
                    // Off-mesh connection: determine direction by comparing distances to BOTH corridor endpoints
                    var conn = mesh.OffMeshConnections[edgeType];
                    // Orientation A (Start→End) vs B (End→Start): minimize total corridor alignment distance
                    float distA = (GetPolyCentroid(mesh, fromPoly) - conn.Start).LengthSquared +
                                  (GetPolyCentroid(mesh, toPoly) - conn.End).LengthSquared;
                    float distB = (GetPolyCentroid(mesh, fromPoly) - conn.End).LengthSquared +
                                  (GetPolyCentroid(mesh, toPoly) - conn.Start).LengthSquared;

                    if (distA <= distB)
                    {
                        portals.Add((conn.Start, conn.Start));
                        portals.Add((conn.End, conn.End));
                    }
                    else
                    {
                        portals.Add((conn.End, conn.End));
                        portals.Add((conn.Start, conn.Start));
                    }
                }
                else
                {
                    // Normal edge: find the shared edge between fromPoly and toPoly
                    var edge = GetSharedEdge(mesh, fromPoly, toPoly);
                    if (edge.HasValue)
                    {
                        portals.Add((edge.Value.left, edge.Value.right));
                    }
                    else
                    {
                        // Fallback: degenerate portal at centroid
                        var c = GetPolyCentroid(mesh, toPoly);
                        portals.Add((c, c));
                    }
                }
            }

            // Last portal: end point (degenerate)
            portals.Add((endPos, endPos));

            return portals;
        }

        /// <summary>
        /// Simple Stupid Funnel Algorithm (Mikko Mononen).
        /// Takes portals (left/right edge points) and produces the shortest path through them.
        /// </summary>
        private static List<Vector3> FunnelSmooth(List<(Vector3 left, Vector3 right)> portals, Vector3 startPos, Vector3 endPos)
        {
            var path = new List<Vector3> { startPos };

            if (portals.Count <= 2)
            {
                path.Add(endPos);
                return path;
            }

            Vector3 portalApex = startPos;
            Vector3 portalLeft = startPos;
            Vector3 portalRight = startPos;
            int apexIndex = 0;
            int leftIndex = 0;
            int rightIndex = 0;

            for (int i = 1; i < portals.Count; i++)
            {
                var (left, right) = portals[i];

                // Update right vertex
                if (TriArea2D(portalApex, portalRight, right) <= 0f)
                {
                    if (VEqual(portalApex, portalRight) || TriArea2D(portalApex, portalLeft, right) > 0f)
                    {
                        // Tighten the funnel
                        portalRight = right;
                        rightIndex = i;
                    }
                    else
                    {
                        // Right over left, insert left to path and restart from left
                        if (!VEqual(path[path.Count - 1], portalLeft))
                            path.Add(portalLeft);

                        portalApex = portalLeft;
                        apexIndex = leftIndex;

                        portalLeft = portalApex;
                        portalRight = portalApex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;

                        i = apexIndex;
                        continue;
                    }
                }

                // Update left vertex
                if (TriArea2D(portalApex, portalLeft, left) >= 0f)
                {
                    if (VEqual(portalApex, portalLeft) || TriArea2D(portalApex, portalRight, left) < 0f)
                    {
                        // Tighten the funnel
                        portalLeft = left;
                        leftIndex = i;
                    }
                    else
                    {
                        // Left over right, insert right to path and restart from right
                        if (!VEqual(path[path.Count - 1], portalRight))
                            path.Add(portalRight);

                        portalApex = portalRight;
                        apexIndex = rightIndex;

                        portalLeft = portalApex;
                        portalRight = portalApex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;

                        i = apexIndex;
                        continue;
                    }
                }
            }

            // Append endpoint if not already there
            if (path.Count == 0 || !VEqual(path[path.Count - 1], endPos))
                path.Add(endPos);

            return path;
        }

        /// <summary>
        /// Signed 2D triangle area (XZ plane) — used by funnel to determine left/right side.
        /// </summary>
        private static float TriArea2D(Vector3 a, Vector3 b, Vector3 c)
        {
            float ax = b.X - a.X;
            float az = b.Z - a.Z;
            float bx = c.X - a.X;
            float bz = c.Z - a.Z;
            return bx * az - ax * bz;
        }

        private static bool VEqual(Vector3 a, Vector3 b)
        {
            const float eps = 0.001f;
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return (dx * dx + dy * dy + dz * dz) < eps * eps;
        }

        /// <summary>
        /// Returns the shared edge between two adjacent polygons as (left, right) endpoints.
        /// Left/right is determined by winding order of polyA.
        /// </summary>
        private static (Vector3 left, Vector3 right)? GetSharedEdge(NavMeshData mesh, int polyA, int polyB)
        {
            var pA = mesh.Polys[polyA];
            var pB = mesh.Polys[polyB];

            for (int ea = 0; ea < pA.VertCount; ea++)
            {
                int ea2 = (ea + 1) % pA.VertCount;
                ushort va0 = pA.Verts[ea];
                ushort va1 = pA.Verts[ea2];

                for (int eb = 0; eb < pB.VertCount; eb++)
                {
                    int eb2 = (eb + 1) % pB.VertCount;
                    ushort vb0 = pB.Verts[eb];
                    ushort vb1 = pB.Verts[eb2];

                    if ((va0 == vb0 && va1 == vb1) || (va0 == vb1 && va1 == vb0))
                    {
                        // Return in polyA winding order: va0=left, va1=right
                        return (mesh.Vertices[va0], mesh.Vertices[va1]);
                    }
                }
            }

            return null;
        }

        private static Vector3? GetSharedEdgeMidpoint(NavMeshData mesh, int polyA, int polyB)
        {
            var edge = GetSharedEdge(mesh, polyA, polyB);
            if (edge.HasValue)
                return (edge.Value.left + edge.Value.right) * 0.5f;
            return null;
        }
    }
}
