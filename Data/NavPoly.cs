namespace MeshViewer3D.Data
{
    /// <summary>
    /// Structure dtPoly de Detour (32 bytes)
    /// Représente un polygone convexe de navigation (jusqu'à 6 vertices)
    /// Format exact tel qu'utilisé par Detour/Recast
    /// </summary>
    public struct NavPoly
    {
        // Link data (4 bytes)
        public uint FirstLink;          // Index du premier lien dans la liste de liens

        // Geometry (24 bytes)
        public ushort[] Verts;          // 6 indices de vertices (12 bytes)
        public ushort[] Neis;           // 6 indices de neighbors (12 bytes)

        // Metadata (4 bytes)
        public ushort Flags;            // Flags de polygone (walkable, swim, etc.)
        public byte VertCount;          // Nombre de vertices utilisés (3-6)
        public byte AreaAndType;        // Lower 6 bits = area, upper 2 bits = poly type

        // Constantes
        public const int MAX_VERTS = 6;

        /// <summary>
        /// Extrait l'AreaType (6 bits de poids faible)
        /// </summary>
        public readonly byte Area => (byte)(AreaAndType & 0x3F);

        /// <summary>
        /// Extrait le PolyType (2 bits de poids fort)
        /// 0 = ground, 1 = offmesh connection, 2 = unused, 3 = unused
        /// </summary>
        public readonly byte Type => (byte)(AreaAndType >> 6);

        /// <summary>
        /// Vérifie si le polygone est walkable
        /// Trinity Core: Area 0-62 = walkable, Area 63 = unwalkable
        /// Flag 0x8000 = peut indiquer obstacle/unwalkable
        /// </summary>
        public readonly bool IsWalkable()
        {
            byte area = Area;
            // Area 63 = unwalkable
            if (area == 63) return false;
            // Flag 0x8000+ peut indiquer unwalkable
            // if ((Flags & 0x8000) != 0) return false;
            return true;
        }
        
        /// <summary>
        /// Vérifie si c'est un obstacle (zone rouge dans HB)
        /// </summary>
        public readonly bool IsObstacle()
        {
            return Area == 63 || (Flags & 0x8000) != 0;
        }

        /// <summary>
        /// Crée une instance avec arrays initialisés
        /// </summary>
        public static NavPoly Create()
        {
            return new NavPoly
            {
                Verts = new ushort[MAX_VERTS],
                Neis = new ushort[MAX_VERTS]
            };
        }
    }

    /// <summary>
    /// Structure dtLink de Detour (8 bytes)
    /// Représente une connexion entre deux polygones adjacents
    /// </summary>
    public struct NavLink
    {
        public ulong Ref;               // Reference du polygon lié (dtPolyRef = uint64 with DT_POLYREF64)
        public uint Next;               // Index du prochain lien dans la liste
        public byte Edge;               // Index de l'edge du polygon
        public byte Side;               // Côté de la tile (0-7: interne, N, E, S, W, NE, SE, SW, NW)
        public byte BMin;               // Min de la région de connexion (quantifié)
        public byte BMax;               // Max de la région de connexion (quantifié)
    }

    /// <summary>
    /// Structure dtPolyDetail de Detour (12 bytes avec padding)
    /// Contient les détails de triangulation d'un polygone pour un rendu précis
    /// </summary>
    public struct NavPolyDetail
    {
        public uint VertBase;           // Base index dans le tableau de detail verts
        public uint TriBase;            // Base index dans le tableau de detail tris
        public byte VertCount;          // Nombre de vertices de detail
        public byte TriCount;           // Nombre de triangles de detail
        // 2 bytes de padding pour alignement
    }

    /// <summary>
    /// Nœud du BVH tree (Bounding Volume Hierarchy) pour l'accélération spatiale
    /// Utilisé pour le raycasting et les requêtes spatiales rapides
    /// </summary>
    public struct BVNode
    {
        public ushort[] BMin;           // 3 composantes (X,Y,Z) quantifiées
        public ushort[] BMax;           // 3 composantes (X,Y,Z) quantifiées
        public int Index;               // Index du poly (si leaf) ou enfant gauche (si branch)

        /// <summary>
        /// Crée une instance avec arrays initialisés
        /// </summary>
        public static BVNode Create()
        {
            return new BVNode
            {
                BMin = new ushort[3],
                BMax = new ushort[3]
            };
        }

        /// <summary>
        /// Vérifie si ce nœud est une feuille (contient un poly)
        /// </summary>
        public readonly bool IsLeaf()
        {
            return Index >= 0;
        }
    }
}
