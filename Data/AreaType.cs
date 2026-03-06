using System.Drawing;

namespace MeshViewer3D.Data
{
    /// <summary>
    /// Types d'area de navigation Trinity Core 3.3.5
    /// Basé sur l'analyse réelle des fichiers mmtile
    /// </summary>
    public enum AreaType : byte
    {
        // MaNGOS/HB NavTerrain flags (from MoveMapSharedDefines.h)
        Empty = 0,              // NAV_EMPTY - no geometry
        Ground = 1,             // NAV_GROUND - terrain walkable (VERT)
        Magma = 2,              // NAV_MAGMA - lave (ROUGE)
        Slime = 4,              // NAV_SLIME - slime (VERT TOXIQUE)
        Water = 8,              // NAV_WATER - eau (BLEU)
        
        Unwalkable = 63         // Complètement non-navigable (ROUGE VIF)
    }

    /// <summary>
    /// Informations complètes sur un type d'area
    /// Contient couleur, coût de pathfinding, description, etc.
    /// </summary>
    public class AreaTypeInfo
    {
        public AreaType Type { get; }
        public string Name { get; }
        public Color Color { get; }
        public bool Walkable { get; }
        public float Cost { get; }              // Coût de pathfinding (1.0 = normal, >1.0 = éviter)
        public string Description { get; }

        public AreaTypeInfo(AreaType type, string name, Color color, bool walkable, float cost = 1.0f, string description = "")
        {
            Type = type;
            Name = name;
            Color = color;
            Walkable = walkable;
            Cost = cost;
            Description = string.IsNullOrEmpty(description) ? name : description;
        }

        /// <summary>
        /// Catalogue des types d'area Trinity Core 3.3.5 avec couleurs exactes HB
        /// </summary>
        public static readonly AreaTypeInfo[] Catalog = new[]
        {
            // MaNGOS/HB NavTerrain - from MoveMapSharedDefines.h
            new AreaTypeInfo(AreaType.Empty,        "Empty",        Color.FromArgb(80, 80, 80),     false, 1000.0f, "No geometry"),
            new AreaTypeInfo(AreaType.Ground,       "Ground",       Color.FromArgb(50, 200, 50),    true,  1.0f,    "Terrain walkable"),
            new AreaTypeInfo(AreaType.Magma,        "Magma",        Color.FromArgb(200, 0, 0),      false, 10.0f,   "Lave dangereuse"),
            new AreaTypeInfo(AreaType.Slime,        "Slime",        Color.FromArgb(120, 200, 20),   false, 10.0f,   "Slime dangereux"),
            new AreaTypeInfo(AreaType.Water,        "Water",        Color.FromArgb(30, 100, 220),   true,  2.0f,    "Zone d'eau"),
            new AreaTypeInfo(AreaType.Unwalkable,   "Unwalkable",   Color.FromArgb(200, 0, 0),      false, 1000.0f, "Non-navigable")
        };

        /// <summary>
        /// Récupère les infos d'un type d'area par son ID
        /// </summary>
        public static AreaTypeInfo Get(byte areaId)
        {
            // Recherche dans le catalogue
            foreach (var info in Catalog)
            {
                if ((byte)info.Type == areaId)
                    return info;
            }
            
            // Area 63 = unwalkable
            if (areaId == 63)
                return Catalog[5]; // Unwalkable
            
            // Toutes les autres areas (4-62) sont considérées comme Ground walkable
            // avec une légère variation de couleur pour les distinguer
            return new AreaTypeInfo(
                (AreaType)areaId,
                $"Ground ({areaId})",
                Color.FromArgb(50 + (areaId * 3) % 50, 180 + (areaId * 5) % 40, 50 + (areaId * 2) % 50),
                true,
                1.0f,
                $"Terrain area #{areaId}"
            );
        }

        /// <summary>
        /// Récupère la couleur d'un type d'area
        /// </summary>
        public static Color GetColor(byte areaId)
        {
            return Get(areaId).Color;
        }

        /// <summary>
        /// Récupère le nom d'un type d'area
        /// </summary>
        public static string GetName(byte areaId)
        {
            return Get(areaId).Name;
        }
    }
}
