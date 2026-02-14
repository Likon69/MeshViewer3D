using System;
using System.Drawing;

namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// Modes de coloration du navmesh
    /// </summary>
    public enum ColorMode
    {
        ByAreaType,     // Couleur fixe par type d'area (HB default)
        ByHeight,       // Gradient vert→jaune par altitude (HB height mode)
        ByPolygon,      // Couleur unique par polygon (debug)
        Flat            // Couleur uniforme (debug)
    }

    /// <summary>
    /// Schémas de couleurs pour le rendu navmesh
    /// Basé sur l'analyse des screenshots Honorbuddy Tripper Renderer
    /// </summary>
    public static class ColorScheme
    {
        // Couleur de fond (bleu-gris comme HB)
        public static readonly Color Background = Color.FromArgb(100, 120, 140);

        // Wireframe (blanc semi-transparent comme HB)
        public static readonly Color Wireframe = Color.FromArgb(60, 255, 255, 255);

        // OffMesh connections
        public static readonly Color OffMeshBidirectional = Color.FromArgb(255, 0, 255, 255);  // Cyan
        public static readonly Color OffMeshUnidirectional = Color.FromArgb(255, 255, 100, 0); // Orange

        // Grid de référence
        public static readonly Color GridMajor = Color.FromArgb(80, 255, 255, 255);
        public static readonly Color GridMinor = Color.FromArgb(40, 255, 255, 255);

        /// <summary>
        /// Récupère la couleur d'un area par son ID
        /// Correspond exactement aux couleurs des screenshots HB
        /// </summary>
        public static Color GetAreaColor(byte areaId)
        {
            return Data.AreaTypeInfo.GetColor(areaId);
        }

        /// <summary>
        /// Gradient de couleur par hauteur (mode height de HB)
        /// Vert foncé (bas) → Vert clair → Jaune (haut)
        /// </summary>
        public static Color GetHeightColor(float height, float minHeight, float maxHeight)
        {
            // Éviter division par zéro
            float range = maxHeight - minHeight;
            if (range < 0.001f) range = 1f;

            // Normaliser hauteur entre 0 et 1
            float t = Math.Clamp((height - minHeight) / range, 0f, 1f);

            // Gradient vert → jaune
            // Bas:  RGB(50, 200, 50)   - Vert foncé
            // Haut: RGB(230, 230, 0)   - Jaune vif
            int r = (int)(50 + 180 * t);    // 50 → 230
            int g = (int)(200 + 30 * t);    // 200 → 230
            int b = (int)(50 - 50 * t);     // 50 → 0

            return Color.FromArgb(r, g, b);
        }

        /// <summary>
        /// Gradient de couleur par hauteur (variante orange pour obstacles)
        /// Utilisé pour les zones non-walkable dans le mode height
        /// </summary>
        public static Color GetHeightColorObstacle(float height, float minHeight, float maxHeight)
        {
            float range = maxHeight - minHeight;
            if (range < 0.001f) range = 1f;

            float t = Math.Clamp((height - minHeight) / range, 0f, 1f);

            // Gradient orange → rouge
            int r = (int)(255 - 55 * t);    // 255 → 200
            int g = (int)(100 - 100 * t);   // 100 → 0
            int b = 0;

            return Color.FromArgb(r, g, b);
        }

        /// <summary>
        /// Couleur aléatoire pour debug polygon
        /// </summary>
        public static Color GetPolygonDebugColor(int polyIndex)
        {
            // Hash simple pour couleurs stables par poly
            long hash = (long)polyIndex * 2654435761L;
            int r = (int)(hash & 0xFF);
            int g = (int)((hash >> 8) & 0xFF);
            int b = (int)((hash >> 16) & 0xFF);

            // Assurer luminosité minimale
            if (r + g + b < 200)
            {
                r = (r + 100) % 256;
                g = (g + 100) % 256;
                b = (b + 100) % 256;
            }

            return Color.FromArgb(r, g, b);
        }

        /// <summary>
        /// Convertit System.Drawing.Color vers Vector3 normalisé (0-1)
        /// </summary>
        public static System.Numerics.Vector3 ColorToVector3(Color color)
        {
            return new System.Numerics.Vector3(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f
            );
        }

        /// <summary>
        /// Convertit System.Drawing.Color vers Vector4 normalisé avec alpha
        /// </summary>
        public static System.Numerics.Vector4 ColorToVector4(Color color)
        {
            return new System.Numerics.Vector4(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f,
                color.A / 255f
            );
        }

        /// <summary>
        /// Interpole entre deux couleurs
        /// </summary>
        public static Color Lerp(Color from, Color to, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * t),
                (int)(from.G + (to.G - from.G) * t),
                (int)(from.B + (to.B - from.B) * t)
            );
        }
    }
}
