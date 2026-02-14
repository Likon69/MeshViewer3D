using System.Drawing;

namespace MeshViewer3D.Data
{
    /// <summary>
    /// Types d'area de navigation Trinity Core 3.3.5
    /// Basé sur l'analyse réelle des fichiers mmtile
    /// </summary>
    public enum AreaType : byte
    {
        // Trinity Core NavArea types (fichiers mmtile réels)
        Ground = 0,             // Terrain standard walkable (VERT - majorité des polys)
        Water = 1,              // Zone d'eau (BLEU)
        MagmaSlime = 2,         // Lave/Slime dangereuse (ROUGE)
        GroundSteep = 3,        // Terrain pentu (VERT FONCÉ)
        
        // Areas 4-62: custom/unused dans Trinity 3.3.5
        Unused4 = 4,
        Unused5 = 5,
        Unused6 = 6,
        Unused7 = 7,
        Unused8 = 8,
        Unused9 = 9,
        Unused10 = 10,
        Unused11 = 11,
        Unused12 = 12,
        Unused13 = 13,
        Unused14 = 14,
        Unused15 = 15,
        
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
            // Trinity Core NavArea - couleurs exactes comme screenshots HB
            new AreaTypeInfo(AreaType.Ground,       "Ground",       Color.FromArgb(50, 200, 50),    true,  1.0f,    "Terrain walkable"),
            new AreaTypeInfo(AreaType.Water,        "Water",        Color.FromArgb(30, 100, 220),   true,  2.0f,    "Zone d'eau"),
            new AreaTypeInfo(AreaType.MagmaSlime,   "Magma/Slime",  Color.FromArgb(200, 0, 0),      false, 10.0f,   "Lave/Slime dangereux"),
            new AreaTypeInfo(AreaType.GroundSteep,  "Steep Ground", Color.FromArgb(30, 120, 30),    true,  1.5f,    "Terrain pentu"),
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
                return Catalog[4]; // Unwalkable
            
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
