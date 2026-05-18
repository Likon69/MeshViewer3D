using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using MeshViewer3D.Data;

namespace MeshViewer3D.Core
{
    /// <summary>
    /// Result of a bake operation.
    /// </summary>
    public sealed class BakeResult
    {
        public int PolysMarked { get; }
        public int PolysTotal { get; }
        public int TrianglesTested { get; }

        public BakeResult(int polysMarked, int polysTotal, int trianglesTested)
        {
            PolysMarked = polysMarked;
            PolysTotal = polysTotal;
            TrianglesTested = trianglesTested;
        }
    }

    /// <summary>
    /// Represents transformed object geometry in Recast coordinate space,
    /// ready for intersection testing against the navmesh.
    /// </summary>
    public sealed class ObjectGeometry
    {
        /// <summary>Display name for logging (e.g., "Stormwind.wmo").</summary>
        public string Name { get; }

        /// <summary>Triangle vertices in Recast space (3 Vector3 per triangle).</summary>
        public Vector3[] Triangles { get; }

        /// <summary>Number of triangles.</summary>
        public int TriangleCount => Triangles.Length / 3;

        /// <summary>Axis-aligned bounding box minimum (Recast space).</summary>
        public Vector3 BoundsMin { get; }

        /// <summary>Axis-aligned bounding box maximum (Recast space).</summary>
        public Vector3 BoundsMax { get; }

        public ObjectGeometry(string name, Vector3[] triangles)
        {
            Name = name;
            Triangles = triangles;

            // Compute AABB
            if (triangles.Length == 0)
            {
                BoundsMin = BoundsMax = Vector3.Zero;
                return;
            }

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < triangles.Length; i++)
            {
                min = Vector3.ComponentMin(min, triangles[i]);
                max = Vector3.ComponentMax(max, triangles[i]);
            }
            BoundsMin = min;
            BoundsMax = max;
        }
    }

    /// <summary>
    /// Bakes object geometry into a navmesh by marking overlapping polygons
    /// as Unwalkable (area 63). Uses 2D XZ-plane overlap testing with vertical
    /// range checking — simple, robust, and requires no Recast dependency.
    /// </summary>
    public static class MeshBaker
    {
        private const byte AREA_UNWALKABLE = 63;
        private const float VERTICAL_TOLERANCE = 2.0f; // yards

        /// <summary>
        /// Snapshot of original AreaAndType values, saved before the first bake.
        /// Used by Unbake() to restore the exact original area types (not just Ground).
        /// Null when no bake has been performed yet.
        /// </summary>
        private static byte[]? _savedAreaTypes;

        /// <summary>
        /// Bakes the given object geometries into the navmesh.
        /// Polygons whose centroid falls inside any object triangle's 2D XZ projection
        /// (within vertical tolerance) are marked as Unwalkable.
        /// </summary>
        /// <param name="mesh">The navmesh to modify in-place.</param>
        /// <param name="objects">Transformed object geometries to bake.</param>
        /// <returns>Result with statistics.</returns>
        public static BakeResult Bake(NavMeshData mesh, IReadOnlyList<ObjectGeometry> objects)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (objects == null || objects.Count == 0)
                return new BakeResult(0, mesh.Polys.Length, 0);

            // Save original area types before first bake (so Unbake restores correctly)
            if (_savedAreaTypes == null)
            {
                _savedAreaTypes = new byte[mesh.Polys.Length];
                for (int i = 0; i < mesh.Polys.Length; i++)
                    _savedAreaTypes[i] = mesh.Polys[i].AreaAndType;
            }

            int totalMarked = 0;
            int totalTriangles = 0;

            foreach (var obj in objects)
                totalTriangles += obj.TriangleCount;

            // For each polygon, compute its centroid and test against all object triangles
            for (int pi = 0; pi < mesh.Polys.Length; pi++)
            {
                var poly = mesh.Polys[pi];

                // Skip already unwalkable or off-mesh connection polys
                if (poly.Area == AREA_UNWALKABLE) continue;
                if (poly.Type != 0) continue; // Type 1 = off-mesh connection
                if (poly.VertCount < 3) continue;

                // Compute polygon centroid in Recast space
                var centroid = ComputeCentroid(mesh.Vertices, poly);

                // Quick AABB rejection per object, then test triangles
                bool hit = false;
                for (int oi = 0; oi < objects.Count && !hit; oi++)
                {
                    var obj = objects[oi];
                    if (obj.TriangleCount == 0) continue;

                    // AABB rejection (XZ plane + vertical tolerance)
                    if (centroid.X < obj.BoundsMin.X || centroid.X > obj.BoundsMax.X ||
                        centroid.Z < obj.BoundsMin.Z || centroid.Z > obj.BoundsMax.Z)
                        continue;

                    if (centroid.Y < obj.BoundsMin.Y - VERTICAL_TOLERANCE ||
                        centroid.Y > obj.BoundsMax.Y + VERTICAL_TOLERANCE)
                        continue;

                    // Test centroid against each triangle (XZ projection)
                    for (int ti = 0; ti < obj.TriangleCount && !hit; ti++)
                    {
                        int idx = ti * 3;
                        var v0 = obj.Triangles[idx];
                        var v1 = obj.Triangles[idx + 1];
                        var v2 = obj.Triangles[idx + 2];

                        // Vertical range check for this specific triangle
                        float triMinY = MathF.Min(v0.Y, MathF.Min(v1.Y, v2.Y));
                        float triMaxY = MathF.Max(v0.Y, MathF.Max(v1.Y, v2.Y));
                        if (centroid.Y < triMinY - VERTICAL_TOLERANCE ||
                            centroid.Y > triMaxY + VERTICAL_TOLERANCE)
                            continue;

                        if (PointInTriangleXZ(centroid, v0, v1, v2))
                            hit = true;
                    }
                }

                if (hit)
                {
                    // Mark polygon as unwalkable, preserving the poly type bits
                    mesh.Polys[pi].AreaAndType = (byte)((mesh.Polys[pi].AreaAndType & 0xC0) | AREA_UNWALKABLE);
                    totalMarked++;
                }
            }

            return new BakeResult(totalMarked, mesh.Polys.Length, totalTriangles);
        }

        /// <summary>
        /// Bakes blackspot markers into the navmesh by marking polys within each
        /// blackspot's cylinder as AREA_UNWALKABLE. Tests poly centroid against
        /// cylinder (XZ distance ≤ radius, Y within height range).
        /// </summary>
        public static BakeResult BakeBlackspots(NavMeshData mesh, IReadOnlyList<Data.Blackspot> blackspots)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (blackspots == null || blackspots.Count == 0)
                return new BakeResult(0, mesh.Polys.Length, 0);

            // Save original area types before first bake
            if (_savedAreaTypes == null)
            {
                _savedAreaTypes = new byte[mesh.Polys.Length];
                for (int i = 0; i < mesh.Polys.Length; i++)
                    _savedAreaTypes[i] = mesh.Polys[i].AreaAndType;
            }

            int totalMarked = 0;

            for (int pi = 0; pi < mesh.Polys.Length; pi++)
            {
                var poly = mesh.Polys[pi];
                if (poly.Area == AREA_UNWALKABLE) continue;
                if (poly.Type != 0) continue;
                if (poly.VertCount < 3) continue;

                var centroid = ComputeCentroid(mesh.Vertices, poly);

                for (int bi = 0; bi < blackspots.Count; bi++)
                {
                    var bs = blackspots[bi];
                    // XZ distance check (cylinder radius)
                    float dx = centroid.X - bs.Location.X;
                    float dz = centroid.Z - bs.Location.Z;
                    float distSq = dx * dx + dz * dz;
                    if (distSq > bs.Radius * bs.Radius) continue;

                    // Y range check (cylinder height centered on blackspot Y)
                    float halfH = bs.Height * 0.5f;
                    if (centroid.Y < bs.Location.Y - halfH - VERTICAL_TOLERANCE ||
                        centroid.Y > bs.Location.Y + halfH + VERTICAL_TOLERANCE)
                        continue;

                    mesh.Polys[pi].AreaAndType = (byte)((mesh.Polys[pi].AreaAndType & 0xC0) | AREA_UNWALKABLE);
                    totalMarked++;
                    break; // poly already marked, next poly
                }
            }

            return new BakeResult(totalMarked, mesh.Polys.Length, blackspots.Count);
        }

        /// <summary>
        /// Restores all polygons to their original area types from before the bake.
        /// If no bake was performed, does nothing.
        /// </summary>
        public static int Unbake(NavMeshData mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (_savedAreaTypes == null) return 0;

            int restored = 0;
            int count = Math.Min(mesh.Polys.Length, _savedAreaTypes.Length);
            for (int pi = 0; pi < count; pi++)
            {
                if (mesh.Polys[pi].AreaAndType != _savedAreaTypes[pi])
                {
                    mesh.Polys[pi].AreaAndType = _savedAreaTypes[pi];
                    restored++;
                }
            }

            _savedAreaTypes = null; // Clear snapshot after restore
            return restored;
        }

        /// <summary>
        /// Computes the centroid (average vertex position) of a navmesh polygon.
        /// </summary>
        private static Vector3 ComputeCentroid(Vector3[] vertices, NavPoly poly)
        {
            var sum = Vector3.Zero;
            for (int i = 0; i < poly.VertCount; i++)
                sum += vertices[poly.Verts[i]];
            return sum / poly.VertCount;
        }

        /// <summary>
        /// Tests if a point lies inside a triangle projected onto the XZ plane
        /// using the cross-product sign method (barycentric).
        /// </summary>
        private static bool PointInTriangleXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = CrossXZ(p, a, b);
            float d2 = CrossXZ(p, b, c);
            float d3 = CrossXZ(p, c, a);

            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(hasNeg && hasPos);
        }

        /// <summary>
        /// 2D cross product sign on XZ plane: (b-a) × (p-a).
        /// </summary>
        private static float CrossXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            return (b.X - a.X) * (p.Z - a.Z) - (b.Z - a.Z) * (p.X - a.X);
        }
        /// <summary>
        /// Bakes custom offmesh connections directly into the navmesh tile data.
        /// Equivalent to HB's baked dtOffMeshConnection — no re-extraction needed.
        ///
        /// For each connection, appends:
        ///   - 2 vertices (start + end, Detour coords)
        ///   - 1 NavPoly  (type=1 = DT_POLYTYPE_OFFMESH_CONNECTION, vertCount=2)
        ///   - 1 OffMeshConnection struct (radius, poly index, side=0xFF, userId=0xFFFFFFFF)
        ///   - 2 NavLink  slots (pre-allocated, filled at runtime by dtNavMesh::addTile)
        ///
        /// DetailMeshes is intentionally NOT extended — Detour only creates detail meshes
        /// for ground polys, not offmesh polys (detailMeshCount = OffMeshBase = ground count).
        /// </summary>
        /// <param name="mesh">Tile to modify in-place.</param>
        /// <param name="customs">Custom connections to bake (Start/End in Detour coords).</param>
        /// <returns>Number of connections baked.</returns>
        public static int BakeOffMeshConnections(NavMeshData mesh, IReadOnlyList<OffMeshConnection> customs)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (customs == null || customs.Count == 0) return 0;

            int n = customs.Count;
            const uint DT_NULL_LINK = 0xFFFFFFFF;

            // --- 1. Extend Vertices (+2 per connection) ---
            int oldVertCount = mesh.Vertices.Length;
            var newVerts = new Vector3[oldVertCount + 2 * n];
            Array.Copy(mesh.Vertices, newVerts, oldVertCount);
            for (int i = 0; i < n; i++)
            {
                newVerts[oldVertCount + 2 * i]     = customs[i].Start;
                newVerts[oldVertCount + 2 * i + 1] = customs[i].End;
            }
            mesh.Vertices = newVerts;

            // --- 2. Extend Polys (+1 per connection, appended after existing polys) ---
            int oldPolyCount = mesh.Polys.Length;
            var newPolys = new NavPoly[oldPolyCount + n];
            Array.Copy(mesh.Polys, newPolys, oldPolyCount);
            for (int i = 0; i < n; i++)
            {
                var p = NavPoly.Create();
                p.FirstLink  = DT_NULL_LINK;
                p.Verts[0]   = (ushort)(oldVertCount + 2 * i);
                p.Verts[1]   = (ushort)(oldVertCount + 2 * i + 1);
                p.VertCount  = 2;
                p.Flags      = 0x0001; // NavTerrainFlag.Walk
                // AreaAndType: upper 2 bits = poly type, lower 6 bits = area
                // DT_POLYTYPE_OFFMESH_CONNECTION = 1, area = NAV_GROUND = 1
                p.AreaAndType = (byte)((1 << 6) | 1);
                newPolys[oldPolyCount + i] = p;
            }
            mesh.Polys = newPolys;

            // --- 3. Extend OffMeshConnections (+1 per connection) ---
            int oldOMCount = mesh.OffMeshConnections.Length;
            var newOM = new OffMeshConnection[oldOMCount + n];
            Array.Copy(mesh.OffMeshConnections, newOM, oldOMCount);
            for (int i = 0; i < n; i++)
            {
                float radius = customs[i].Radius > 0f ? customs[i].Radius : mesh.Header.WalkableRadius;
                newOM[oldOMCount + i] = new OffMeshConnection
                {
                    Start  = customs[i].Start,
                    End    = customs[i].End,
                    Radius = radius,
                    Poly   = (ushort)(oldPolyCount + i), // index of the new poly
                    Flags  = customs[i].IsBidirectional ? OffMeshConnection.FLAG_BIDIRECTIONAL : (byte)0,
                    Side   = 0xFF,         // DT_CONN_SIDE: no preferred side (matches HB)
                    UserId = 0xFFFFFFFF    // matches HB binary extraction
                };
            }
            mesh.OffMeshConnections = newOM;

            // --- 4. Extend Links (+2 per connection: pre-allocated slots, Detour fills at runtime) ---
            int oldLinkCount = mesh.Links.Length;
            var newLinks = new NavLink[oldLinkCount + 2 * n];
            Array.Copy(mesh.Links, newLinks, oldLinkCount);
            for (int i = 0; i < 2 * n; i++)
            {
                newLinks[oldLinkCount + i] = new NavLink
                {
                    Ref  = 0,
                    Next = DT_NULL_LINK,
                    Edge = 0,
                    Side = 0,
                    BMin = 0,
                    BMax = 0
                };
            }
            mesh.Links = newLinks;

            // --- 5. Update header ---
            // - VertCount   += 2*n
            // - PolyCount   += n
            // - OffMeshConCount += n
            // - OffMeshBase  = PolyCount - OffMeshConCount  (= original ground poly count, unchanged)
            // - MaxLinkCount += 2*n
            // - DetailMeshCount unchanged (detail meshes exist for ground polys only)
            var h = mesh.Header;
            h.VertCount      += 2 * n;
            h.PolyCount      += n;
            h.OffMeshConCount += n;
            h.OffMeshBase     = h.PolyCount - h.OffMeshConCount;
            h.MaxLinkCount   += 2 * n;
            mesh.Header = h;

            return n;
        }    }
}
