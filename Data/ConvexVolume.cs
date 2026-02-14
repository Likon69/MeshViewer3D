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
        /// Génère les triangles pour le rendu du polygone (fan triangulation)
        /// </summary>
        public List<(Vector3, Vector3, Vector3)> Triangulate()
        {
            var triangles = new List<(Vector3, Vector3, Vector3)>();
            if (!IsValid())
                return triangles;

            // Simple fan triangulation (suppose que le polygone est convexe)
            var center = GetCenter();
            center.Y = MinHeight; // Base du volume

            for (int i = 0; i < Vertices.Count; i++)
            {
                int next = (i + 1) % Vertices.Count;
                
                var v1 = Vertices[i];
                var v2 = Vertices[next];
                v1.Y = MinHeight;
                v2.Y = MinHeight;

                triangles.Add((center, v1, v2));
            }

            return triangles;
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
