using System;
using OpenTK.Mathematics;

namespace MeshViewer3D.Data
{
    /// <summary>
    /// Type de connexion OffMesh
    /// </summary>
    public enum OffMeshConnectionType : byte
    {
        Jump = 0,       // Saut standard
        Elevator = 1,   // Ascenseur
        Portal = 2,     // Portail / téléport
        Boat = 3,       // Transport (bateau, zeppelin)
        Ladder = 4,     // Échelle
        Custom = 255    // Personnalisé
    }

    /// <summary>
    /// Structure dtOffMeshConnection de Detour (36 bytes)
    /// Représente une connexion hors-mesh (saut, téléport, ascenseur, etc.)
    /// Visible dans les screenshots HB comme lignes rouges/cyan
    /// </summary>
    public struct OffMeshConnection
    {
        // Géométrie (24 bytes)
        public Vector3 Start;           // Point de départ (X,Y,Z) en coordonnées Detour
        public Vector3 End;             // Point d'arrivée (X,Y,Z) en coordonnées Detour

        // Paramètres (8 bytes)
        public float Radius;            // Rayon de validation (tolérance pour considérer qu'on est "sur" la connexion)

        // Metadata (4 bytes)
        public ushort Poly;             // Index du polygone associé
        public byte Flags;              // Flags de connexion (bit 0 = bidirectional)
        public byte Side;               // Côté de la tile (0-7)

        // Extended properties (non-Detour, pour édition)
        public string Name;             // Nom optionnel pour référence
        public OffMeshConnectionType ConnectionType;  // Type de connexion

        // Constantes de flags
        public const byte FLAG_BIDIRECTIONAL = 1 << 0;

        /// <summary>
        /// Vérifie si la connexion est bidirectionnelle (aller-retour)
        /// Les connexions bidirectionnelles sont souvent en cyan dans HB
        /// </summary>
        public readonly bool IsBidirectional => (Flags & FLAG_BIDIRECTIONAL) != 0;

        /// <summary>
        /// Calcule la direction normalisée du vecteur Start→End
        /// </summary>
        public readonly Vector3 GetDirection()
        {
            Vector3 dir = End - Start;
            float length = dir.Length;
            return length > 0.0001f ? dir / length : Vector3.UnitX;
        }

        /// <summary>
        /// Calcule la distance de la connexion (longueur du saut/téléport)
        /// </summary>
        public readonly float GetDistance()
        {
            return Vector3.Distance(Start, End);
        }

        /// <summary>
        /// Vérifie si un point est dans le rayon de départ
        /// </summary>
        public readonly bool IsNearStart(Vector3 point, float tolerance = 0.0f)
        {
            return Vector3.Distance(point, Start) <= Radius + tolerance;
        }

        /// <summary>
        /// Vérifie si un point est dans le rayon d'arrivée
        /// </summary>
        public readonly bool IsNearEnd(Vector3 point, float tolerance = 0.0f)
        {
            return Vector3.Distance(point, End) <= Radius + tolerance;
        }

        /// <summary>
        /// Retourne une description textuelle de la connexion
        /// </summary>
        public override readonly string ToString()
        {
            string dir = IsBidirectional ? "↔" : "→";
            return $"OffMesh {dir} {GetDistance():F1}yd";
        }
    }
}
