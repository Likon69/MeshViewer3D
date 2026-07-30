using System;
using OpenTK.Mathematics;

namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// View frustum extracted from a view*projection matrix.
    /// Six planes (left, right, bottom, top, near, far), each as a normalized
    /// Vector4 (XYZ = plane normal, W = signed distance from origin to plane).
    /// A point is INSIDE the frustum iff its signed distance to every plane
    /// is >= 0.
    /// </summary>
    internal readonly struct Frustum
    {
        private readonly Vector4 _left, _right, _bottom, _top, _near, _far;

        private Frustum(Vector4 l, Vector4 r, Vector4 b, Vector4 t, Vector4 n, Vector4 f)
        {
            _left = l; _right = r; _bottom = b; _top = t; _near = n; _far = f;
        }

        public static Frustum FromViewProjection(in Matrix4 m)
        {
            // OpenTK Matrix4 stores row-major; m.Mij = row i col j.
            // Standard Gribb-Hartmann plane extraction for column-major storage, but
            // here we transpose: plane_i = row3 + sign*row_i (for row-major m).
            return new Frustum(
                Normalize(new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41)), // left
                Normalize(new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41)), // right
                Normalize(new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42)), // bottom
                Normalize(new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42)), // top
                Normalize(new Vector4(m.M14 + m.M13, m.M24 + m.M23, m.M34 + m.M33, m.M44 + m.M43)), // near
                Normalize(new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43))  // far
            );
        }

        /// <summary>
        /// True if the AABB intersects (or touches) the frustum. Conservative — tiles
        /// that just barely poke in are kept visible. A small Y-axis inflation accommodates
        /// off-mesh arc geometry which can extend slightly above the navmesh bounds.
        /// </summary>
        public bool IntersectsAabb(Vector3 bmin, Vector3 bmax)
        {
            // Defensive: swap reversed bounds
            var min = Vector3.ComponentMin(bmin, bmax);
            var max = Vector3.ComponentMax(bmin, bmax);

            // Inflate vertically to cover off-mesh arc endpoints that sit above the navmesh
            max.Y += 2.0f;

            // Conservative: smallest signed distance to plane must be >= 0 (or just barely negative)
            return !Outside(_left,   min, max) &&
                   !Outside(_right,  min, max) &&
                   !Outside(_bottom, min, max) &&
                   !Outside(_top,    min, max) &&
                   !Outside(_near,   min, max) &&
                   !Outside(_far,    min, max);
        }

        private static bool Outside(in Vector4 p, in Vector3 min, in Vector3 max)
        {
            // Pick the box vertex farthest along the plane normal.
            float x = p.X >= 0f ? max.X : min.X;
            float y = p.Y >= 0f ? max.Y : min.Y;
            float z = p.Z >= 0f ? max.Z : min.Z;
            return (p.X * x + p.Y * y + p.Z * z + p.W) < 0f;
        }

        private static Vector4 Normalize(Vector4 plane)
        {
            float len = MathF.Sqrt(plane.X * plane.X + plane.Y * plane.Y + plane.Z * plane.Z);
            if (len < 1e-6f) return Vector4.Zero; // degenerate — reject nothing
            return new Vector4(plane.X / len, plane.Y / len, plane.Z / len, plane.W / len);
        }
    }
}