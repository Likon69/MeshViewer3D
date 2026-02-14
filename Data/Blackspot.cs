using OpenTK.Mathematics;
using MeshViewer3D.Core;
using System;

namespace MeshViewer3D.Data
{
    /// <summary>
    /// Structure Blackspot (zone cylindrique à éviter)
    /// Correspond à la structure Blackspot de Honorbuddy
    /// Visualisé comme cylindre rouge semi-transparent dans Tripper.Renderer
    /// </summary>
    public struct Blackspot
    {
        /// <summary>
        /// Centre du blackspot en coordonnées Detour (X, Y, Z)
        /// </summary>
        public Vector3 Location;

        /// <summary>
        /// Rayon horizontal du cylindre (en yards/unités WoW)
        /// </summary>
        public float Radius;

        /// <summary>
        /// Hauteur du cylindre (en yards/unités WoW)
        /// </summary>
        public float Height;

        /// <summary>
        /// Nom optionnel du blackspot (pour identification)
        /// </summary>
        public string Name;

        public Blackspot(Vector3 location, float radius, float height, string name = "")
        {
            Location = location;
            Radius = radius;
            Height = height;
            Name = name;
        }

        /// <summary>
        /// Crée un blackspot depuis coordonnées WoW
        /// </summary>
        public static Blackspot FromWoW(float x, float y, float z, float radius, float height, string name = "")
        {
            return new Blackspot(
                MeshViewer3D.Core.CoordinateSystem.WowToDetour(new Vector3(x, y, z)),
                radius,
                height,
                name
            );
        }

        /// <summary>
        /// Convertit la position en coordonnées WoW
        /// </summary>
        public readonly Vector3 ToWoWCoords()
        {
            return MeshViewer3D.Core.CoordinateSystem.DetourToWow(Location);
        }

        /// <summary>
        /// Vérifie si un point (en coordonnées Detour) est dans le blackspot
        /// </summary>
        public readonly bool Contains(Vector3 point)
        {
            // Vérifier distance horizontale (cylindre)
            float dx = point.X - Location.X;
            float dz = point.Z - Location.Z;
            float distSq = dx * dx + dz * dz;

            if (distSq > Radius * Radius)
                return false;

            // Vérifier hauteur
            float dy = point.Y - Location.Y;
            return dy >= 0 && dy <= Height;
        }

        /// <summary>
        /// Calcule la distance horizontale d'un point au centre du blackspot
        /// </summary>
        public readonly float DistanceTo(Vector3 point)
        {
            float dx = point.X - Location.X;
            float dz = point.Z - Location.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        
        /// <summary>
        /// Obtient la boîte englobante du blackspot
        /// </summary>
        public readonly void GetBounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(
                Location.X - Radius,
                Location.Y,
                Location.Z - Radius
            );
            max = new Vector3(
                Location.X + Radius,
                Location.Y + Height,
                Location.Z + Radius
            );
        }

        public override readonly string ToString()
        {
            var wow = ToWoWCoords();
            return string.IsNullOrEmpty(Name)
                ? $"Blackspot [{wow.X:F1}, {wow.Y:F1}, {wow.Z:F1}] R={Radius:F1} H={Height:F1}"
                : $"{Name} [{wow.X:F1}, {wow.Y:F1}, {wow.Z:F1}] R={Radius:F1} H={Height:F1}";
        }
    }
}
