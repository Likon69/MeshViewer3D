using OpenTK.Mathematics;

namespace MeshViewer3D.Data
{
    /// <summary>
    /// Structure dtMeshHeader de Detour/Recast (96 bytes)
    /// Contient toutes les informations de header d'une tile navmesh
    /// Format exact tel qu'utilisé par Trinity Core 3.3.5a et Honorbuddy
    /// </summary>
    public struct MeshHeader
    {
        // Identification (8 bytes)
        public uint Magic;              // +0   "DNAV" = 0x444E4156 (ou 0x5641564E little-endian "VAND")
        public int Version;             // +4   Version 7 pour Trinity 3.3.5

        // Coordonnées tile (12 bytes)
        public int TileX;               // +8   Coordonnée X de la tile (0-63)
        public int TileY;               // +12  Coordonnée Y de la tile (0-63)
        public int Layer;               // +16  Index de layer (phases WoW)

        // Metadata (4 bytes)
        public uint UserId;             // +20  User data custom

        // Counts (36 bytes)
        public int PolyCount;           // +24  Nombre de polygones
        public int VertCount;           // +28  Nombre de vertices
        public int MaxLinkCount;        // +32  Liens max entre polygones
        public int DetailMeshCount;     // +36  Nombre de detail meshes
        public int DetailVertCount;     // +40  Vertices de detail
        public int DetailTriCount;      // +44  Triangles de detail
        public int BvNodeCount;         // +48  Nœuds du BV tree (spatial acceleration)
        public int OffMeshConCount;     // +52  Nombre de connections OffMesh
        public int OffMeshBase;         // +56  Index de base OffMesh

        // Paramètres navigation (12 bytes)
        public float WalkableHeight;    // +60  Hauteur du personnage (yards)
        public float WalkableRadius;    // +64  Rayon du personnage (yards)
        public float WalkableClimb;     // +68  Hauteur max d'escalade (yards)

        // Bounding box (24 bytes)
        public Vector3 BMin;            // +72  Bounding box minimum (X,Y,Z) en coordonnées Detour
        public Vector3 BMax;            // +84  Bounding box maximum (X,Y,Z) en coordonnées Detour

        // Quantization (4 bytes)
        public float BvQuantFactor;     // +96  Facteur de quantization du BV tree

        // Constantes de validation (little-endian comme lu par BinaryReader)
        public const uint DETOUR_MAGIC_DNAV = 0x444E4156;  // 'D'<<24|'N'<<16|'A'<<8|'V' = Detour NAV
        public const uint DETOUR_MAGIC_VAND = 0x5641564E;  // Alternate magic found in some tile variants
        public const int DETOUR_VERSION = 7;

        /// <summary>
        /// Valide que le header est correct (magic + version)
        /// </summary>
        public readonly bool IsValid()
        {
            return (Magic == DETOUR_MAGIC_VAND || Magic == DETOUR_MAGIC_DNAV) && Version == DETOUR_VERSION;
        }

        /// <summary>
        /// Calcule la taille de la bounding box
        /// </summary>
        public readonly Vector3 GetSize()
        {
            return BMax - BMin;
        }

        /// <summary>
        /// Calcule le centre de la bounding box (en coordonnées Detour)
        /// </summary>
        public readonly Vector3 GetCenter()
        {
            return (BMin + BMax) * 0.5f;
        }

        /// <summary>
        /// Retourne une description textuelle du header
        /// </summary>
        public override readonly string ToString()
        {
            return $"NavMesh Tile ({TileX},{TileY}) - {PolyCount} polys, {VertCount} verts, {OffMeshConCount} offmesh";
        }
    }
}
