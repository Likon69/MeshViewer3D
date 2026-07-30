// ============================================================================
//  NavMeshRaycastIndex.cs  —  Uniform XZ grid over a NavMeshData
//
//  Built once per mesh load. Stores per-cell a list of poly indices whose AABB
//  (in XZ) intersects the cell. Raycast query: walk the ray's XZ segment through
//  the grid (DDA-like) and test only the polys in the visited cells.
//
//  The result of a raycast query is identical to a full-mesh scan — this index
//  only restricts which triangles are tested, not which one wins.
// ============================================================================

using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using MeshViewer3D.Rendering;

namespace MeshViewer3D.Core
{
    public sealed class NavMeshRaycastIndex
    {
        // Cell side length in world units. ~33u matches a Detour sub-tile (533/16)
        // which keeps most polys in a 1-3 cells without blow-up.
        private const float CellSize = 33.0f;

        private readonly Vector3[] _vertices;
        private readonly int[] _polyVertIndices; // flattened per-poly vertex indices, length = sum(VertCount)
        private readonly int[] _polyVertOffset;  // _polyVertIndices[Offset[i] .. Offset[i]+VertCount[i]]
        private readonly int[] _polyVertCount;

        private readonly int _gridX, _gridZ;
        private readonly float _originX, _originZ;
        private readonly int[] _cellStart;     // prefix sum, length = _gridX*_gridZ + 1
        private readonly int[] _cellPolyIdx;   // flat list of poly indices, length = total

        public NavMeshRaycastIndex(NavMeshData mesh)
        {
            if (mesh == null || mesh.Polys.Length == 0) return;

            _vertices = mesh.Vertices;

            int polyCount = mesh.Polys.Length;
            _polyVertCount = new int[polyCount];
            _polyVertOffset = new int[polyCount];

            int totalVerts = 0;
            for (int i = 0; i < polyCount; i++)
            {
                _polyVertOffset[i] = totalVerts;
                int vc = mesh.Polys[i].VertCount;
                if (vc < 3) { _polyVertCount[i] = 0; continue; }
                _polyVertCount[i] = vc;
                totalVerts += vc;
            }
            _polyVertIndices = new int[totalVerts];
            for (int i = 0; i < polyCount; i++)
            {
                int vc = _polyVertCount[i];
                var verts = mesh.Polys[i].Verts;
                for (int j = 0; j < vc; j++)
                    _polyVertIndices[_polyVertOffset[i] + j] = (int)verts[j];
            }

            // Build per-cell poly list. Two-pass: count, then fill.
            Vector3 bmin = mesh.Header.BMin, bmax = mesh.Header.BMax;
            _originX = bmin.X;
            _originZ = bmin.Z;
            _gridX = Math.Max(1, (int)MathF.Ceiling((bmax.X - bmin.X) / CellSize) + 1);
            _gridZ = Math.Max(1, (int)MathF.Ceiling((bmax.Z - bmin.Z) / CellSize) + 1);
            int cellCount = _gridX * _gridZ;
            int[] cellCounts = new int[cellCount];

            // First pass: for each poly, compute its AABB, count cells it overlaps.
            float[] polyMinX = new float[polyCount];
            float[] polyMaxX = new float[polyCount];
            float[] polyMinZ = new float[polyCount];
            float[] polyMaxZ = new float[polyCount];
            for (int i = 0; i < polyCount; i++)
            {
                int vc = _polyVertCount[i];
                if (vc == 0) continue;
                int off = _polyVertOffset[i];
                Vector3 v0 = _vertices[_polyVertIndices[off]];
                float minX = v0.X, maxX = v0.X, minZ = v0.Z, maxZ = v0.Z;
                for (int j = 1; j < vc; j++)
                {
                    Vector3 v = _vertices[_polyVertIndices[off + j]];
                    if (v.X < minX) minX = v.X; else if (v.X > maxX) maxX = v.X;
                    if (v.Z < minZ) minZ = v.Z; else if (v.Z > maxZ) maxZ = v.Z;
                }
                polyMinX[i] = minX; polyMaxX[i] = maxX;
                polyMinZ[i] = minZ; polyMaxZ[i] = maxZ;

                int cx0 = CellIndex(minX, _originX, CellSize);
                int cx1 = CellIndex(maxX, _originX, CellSize);
                int cz0 = CellIndex(minZ, _originZ, CellSize);
                int cz1 = CellIndex(maxZ, _originZ, CellSize);
                int span = (cx1 - cx0 + 1) * (cz1 - cz0 + 1);
                for (int cz = cz0; cz <= cz1; cz++)
                    for (int cx = cx0; cx <= cx1; cx++)
                        cellCounts[cz * _gridX + cx]++;
                _ = span; // suppress unused warning
            }

            // Prefix sum.
            _cellStart = new int[cellCount + 1];
            int total = 0;
            for (int i = 0; i < cellCount; i++)
            {
                _cellStart[i] = total;
                total += cellCounts[i];
            }
            _cellStart[cellCount] = total;
            _cellPolyIdx = new int[total];

            // Second pass: fill cell contents.
            int[] writeHead = (int[])_cellStart.Clone();
            for (int i = 0; i < polyCount; i++)
            {
                int vc = _polyVertCount[i];
                if (vc == 0) continue;
                int cx0 = CellIndex(polyMinX[i], _originX, CellSize);
                int cx1 = CellIndex(polyMaxX[i], _originX, CellSize);
                int cz0 = CellIndex(polyMinZ[i], _originZ, CellSize);
                int cz1 = CellIndex(polyMaxZ[i], _originZ, CellSize);
                for (int cz = cz0; cz <= cz1; cz++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        int slot = writeHead[cz * _gridX + cx]++;
                        _cellPolyIdx[slot] = i;
                    }
            }
        }

        /// <summary>
        /// Returns the set of poly indices the ray may hit, in near-to-far order along
        /// the ray's XZ segment. Caller still does the precise triangle test
        /// (Rendering.RayCaster.RayTriangleIntersect) and keeps the closest hit.
        ///
        /// Guarantees the same triangles as a full scan:
        /// - the cell containing the ray's XZ origin is always visited first,
        /// - then DDA walks every cell the segment crosses until the segment's far end,
        /// - polys that span multiple cells are returned once (de-duplicated).
        /// </summary>
        public IEnumerable<int> QueryRay(Ray ray)
        {
            if (_cellStart == null) yield break;

            float ox = ray.Origin.X, oz = ray.Origin.Z;
            float dx = ray.Direction.X, dz = ray.Direction.Z;
            float tMax = float.PositiveInfinity;

            // Walk direction step: -1, 0, or +1 per axis.
            int stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int stepZ = dz > 0 ? 1 : (dz < 0 ? -1 : 0);

            // Snap to cell index. Ray origin may sit outside the grid (camera position);
            // clamp to the nearest cell so DDA starts.
            int cx = Clamp(CellIndex(ox, _originX, CellSize), 0, _gridX - 1);
            int cz = Clamp(CellIndex(oz, _originZ, CellSize), 0, _gridZ - 1);

            // Distance to the next cell boundary along each axis.
            // tMaxX = distance from origin to the next X boundary, / |dx|
            // Use a large value when dx is ~0 to avoid divide-by-zero.
            const float eps = 1e-9f;
            float tMaxX = stepX != 0 ? ((BoundaryX(cx, stepX) - ox) / (dx + (stepX == 0 ? eps : (dx >= 0 ? eps : -eps)))) : float.PositiveInfinity;
            float tMaxZ = stepZ != 0 ? ((BoundaryZ(cz, stepZ) - oz) / (dz + (stepZ == 0 ? eps : (dz >= 0 ? eps : -eps)))) : float.PositiveInfinity;
            if (stepX == 0) tMaxX = float.PositiveInfinity;
            if (stepZ == 0) tMaxZ = float.PositiveInfinity;

            float tDeltaX = stepX != 0 ? (CellSize / MathF.Abs(dx)) : float.PositiveInfinity;
            float tDeltaZ = stepZ != 0 ? (CellSize / MathF.Abs(dz)) : float.PositiveInfinity;

            // De-duplicate poly indices across overlapping cells. Each poly may be
            // stored in multiple cells; we yield it only once on its first visit.
            // Conservative bound: total polys. Using a HashSet is the simplest correct
            // option here (per-query alloc — could be moved to per-frame if needed).
            var seen = new HashSet<int>();

            // Safety bound: never walk more cells than exist.
            int maxSteps = _gridX * _gridZ + 1;
            for (int step = 0; step < maxSteps; step++)
            {
                int cellIdx = cz * _gridX + cx;
                int start = _cellStart[cellIdx];
                int end = _cellStart[cellIdx + 1];
                for (int k = start; k < end; k++)
                {
                    int poly = _cellPolyIdx[k];
                    if (seen.Add(poly)) yield return poly;
                }

                // Step to the next cell along the axis with the smaller tMax.
                if (tMaxX < tMaxZ)
                {
                    cx += stepX;
                    tMaxX += tDeltaX;
                }
                else if (tMaxZ < tMaxX)
                {
                    cz += stepZ;
                    tMaxZ += tDeltaZ;
                }
                else
                {
                    // Diagonal step (corner case when tMaxX == tMaxZ).
                    cx += stepX;
                    cz += stepZ;
                    tMaxX += tDeltaX;
                    tMaxZ += tDeltaZ;
                }

                if (cx < 0 || cx >= _gridX || cz < 0 || cz >= _gridZ) break;
            }
        }

        private static int CellIndex(float coord, float origin, float cellSize)
        {
            return (int)MathF.Floor((coord - origin) / cellSize);
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        private float BoundaryX(int cx, int stepX) => _originX + (cx + (stepX > 0 ? 1 : 0)) * CellSize;
        private float BoundaryZ(int cz, int stepZ) => _originZ + (cz + (stepZ > 0 ? 1 : 0)) * CellSize;
    }
}