using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using MeshViewer3D.Core;

namespace MeshViewer3D.Data
{
    /// <summary>
    /// Volume convexe (zone polygonale) avec AreaType custom
    /// Permet de redéfinir des zones entières avec un type de terrain différent
    /// Visualisé comme polygone extrudé avec couleur de l'AreaType
    /// </summary>
    public class ConvexVolume
    {
        /// <summary>
        /// Points du polygone convexe (en coordonnées Detour)
        /// IMPORTANT: Doivent être dans le plan XZ (Y = hauteur)
        /// </summary>
        public List<Vector3> Vertices { get; set; } = new();

        /// <summary>
        /// Hauteur minimum du volume (Y min)
        /// </summary>
        public float MinHeight { get; set; }

        /// <summary>
        /// Hauteur maximum du volume (Y max)
        /// </summary>
        public float MaxHeight { get; set; }

        /// <summary>
        /// Type d'area à appliquer dans cette zone
        /// </summary>
        public AreaType AreaType { get; set; }

        /// <summary>
        /// Nom optionnel du volume
        /// </summary>
        public string Name { get; set; } = "";

        public ConvexVolume()
        {
            AreaType = AreaType.Ground;
            MinHeight = 0;
            MaxHeight = 100;
        }

        /// <summary>
        /// Vérifie si le polygone est valide (au moins 3 points)
        /// </summary>
        public bool IsValid()
        {
            return Vertices.Count >= 3;
        }

        /// <summary>
        /// Vérifie si un point (en 3D) est à l'intérieur du volume
        /// </summary>
        public bool Contains(Vector3 point)
        {
            if (!IsValid())
                return false;

            // Vérifier d'abord la hauteur
            if (point.Y < MinHeight || point.Y > MaxHeight)
                return false;

            // Test point-in-polygon 2D (projection XZ)
            return IsPointInPolygon(point.X, point.Z);
        }

        /// <summary>
        /// Test point-in-polygon classique (ray casting algorithm)
        /// </summary>
        private bool IsPointInPolygon(float x, float z)
        {
            bool inside = false;
            int count = Vertices.Count;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                float xi = Vertices[i].X;
                float zi = Vertices[i].Z;
                float xj = Vertices[j].X;
                float zj = Vertices[j].Z;

                bool intersect = ((zi > z) != (zj > z))
                    && (x < (xj - xi) * (z - zi) / (zj - zi) + xi);

                if (intersect)
                    inside = !inside;
            }

            return inside;
        }

        /// <summary>
        /// Calcule le centre du polygone (centroid)
        /// </summary>
        public Vector3 GetCenter()
        {
            if (!IsValid())
                return Vector3.Zero;

            float sumX = 0, sumZ = 0;
            foreach (var v in Vertices)
            {
                sumX += v.X;
                sumZ += v.Z;
            }

            float centerY = (MinHeight + MaxHeight) / 2f;
            return new Vector3(sumX / Vertices.Count, centerY, sumZ / Vertices.Count);
        }

        /// <summary>
        /// Calcule la bounding box 2D du polygone
        /// </summary>
        public (float minX, float maxX, float minZ, float maxZ) GetBounds2D()
        {
            if (!IsValid())
                return (0, 0, 0, 0);

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;

            foreach (var v in Vertices)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Z < minZ) minZ = v.Z;
                if (v.Z > maxZ) maxZ = v.Z;
            }

            return (minX, maxX, minZ, maxZ);
        }

        /// <summary>
        /// Computes the convex hull of 3D points projected onto XZ plane (Graham scan).
        /// Returns vertices ordered counter-clockwise. Preserves Y from original points.
        /// Returns empty list if fewer than 3 unique points.
        /// </summary>
        public static List<Vector3> ComputeConvexHull(List<Vector3> points)
        {
            if (points == null || points.Count < 3)
                return new List<Vector3>();

            // Remove duplicate XZ positions
            var unique = new List<Vector3>();
            var seen = new HashSet<(int, int)>();
            foreach (var p in points)
            {
                var key = ((int)(p.X * 100), (int)(p.Z * 100));
                if (seen.Add(key))
                    unique.Add(p);
            }
            if (unique.Count < 3)
                return new List<Vector3>();

            // Find lowest Z (then leftmost X) as pivot
            int minIdx = 0;
            for (int i = 1; i < unique.Count; i++)
            {
                if (unique[i].Z < unique[minIdx].Z ||
                   (unique[i].Z == unique[minIdx].Z && unique[i].X < unique[minIdx].X))
                    minIdx = i;
            }
            var pivot = unique[minIdx];

            // Sort remaining by polar angle from pivot
            var sorted = new List<Vector3>(unique);
            sorted.RemoveAt(minIdx);
            sorted.Sort((a, b) =>
            {
                float angleA = MathF.Atan2(a.Z - pivot.Z, a.X - pivot.X);
                float angleB = MathF.Atan2(b.Z - pivot.Z, b.X - pivot.X);
                int cmp = angleA.CompareTo(angleB);
                if (cmp != 0) return cmp;
                float distA = (a.X - pivot.X) * (a.X - pivot.X) + (a.Z - pivot.Z) * (a.Z - pivot.Z);
                float distB = (b.X - pivot.X) * (b.X - pivot.X) + (b.Z - pivot.Z) * (b.Z - pivot.Z);
                return distA.CompareTo(distB);
            });

            // Graham scan
            var hull = new List<Vector3> { pivot };
            foreach (var p in sorted)
            {
                while (hull.Count > 1)
                {
                    var o = hull[hull.Count - 2];
                    var a = hull[hull.Count - 1];
                    float cross = (a.X - o.X) * (p.Z - o.Z) - (a.Z - o.Z) * (p.X - o.X);
                    if (cross > 0) break; // CCW turn — keep
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(p);
            }

            return hull.Count >= 3 ? hull : new List<Vector3>();
        }

        public override string ToString()
        {
            var center = GetCenter();
            var wow = MeshViewer3D.Core.CoordinateSystem.DetourToWow(center);
            return string.IsNullOrEmpty(Name)
                ? $"Volume ({Vertices.Count} pts) Area={AreaType} [{wow.X:F0},{wow.Y:F0},{wow.Z:F0}]"
                : $"{Name} ({Vertices.Count} pts) Area={AreaType}";
        }
    }
}
