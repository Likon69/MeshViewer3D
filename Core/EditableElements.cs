using System.Collections.Generic;
using MeshViewer3D.Data;

namespace MeshViewer3D.Core
{
    /// <summary>
    /// Gestionnaire des éléments éditables du navmesh
    /// Contient les blackspots, jump links, convex volumes, etc.
    /// </summary>
    public class EditableElements
    {
        /// <summary>
        /// Liste des blackspots (zones à éviter)
        /// </summary>
        public List<Blackspot> Blackspots { get; set; } = new();

        /// <summary>
        /// Liste des convex volumes (zones polygonales avec AreaType custom)
        /// </summary>
        public List<ConvexVolume> ConvexVolumes { get; set; } = new();

        /// <summary>
        /// Liste des connexions OffMesh additionnelles (créées manuellement)
        /// Distinctes des OffMesh connections du navmesh lui-même
        /// </summary>
        public List<OffMeshConnection> CustomOffMeshConnections { get; set; } = new();

        /// <summary>
        /// Liste des WMO à ignorer (blacklist)
        /// </summary>
        public List<uint> WmoBlacklist { get; set; } = new();

        /// <summary>
        /// Index de l'élément actuellement sélectionné
        /// -1 si aucune sélection
        /// </summary>
        public int SelectedBlackspotIndex { get; set; } = -1;
        public int SelectedVolumeIndex { get; set; } = -1;
        public int SelectedOffMeshIndex { get; set; } = -1;

        /// <summary>
        /// Type d'élément sélectionné
        /// </summary>
        public EditableElementType SelectedType { get; set; } = EditableElementType.None;

        public bool HasSelection => SelectedType != EditableElementType.None;

        /// <summary>
        /// Efface la sélection courante
        /// </summary>
        public void ClearSelection()
        {
            SelectedType = EditableElementType.None;
            SelectedBlackspotIndex = -1;
            SelectedVolumeIndex = -1;
            SelectedOffMeshIndex = -1;
        }

        /// <summary>
        /// Efface tous les éléments éditables
        /// </summary>
        public void Clear()
        {
            Blackspots.Clear();
            ConvexVolumes.Clear();
            CustomOffMeshConnections.Clear();
            WmoBlacklist.Clear();
            ClearSelection();
        }

        /// <summary>
        /// Compte le nombre total d'éléments éditables
        /// </summary>
        public int TotalCount => Blackspots.Count + ConvexVolumes.Count + CustomOffMeshConnections.Count;
    }

    /// <summary>
    /// Type d'élément éditable
    /// </summary>
    public enum EditableElementType
    {
        None,
        Blackspot,
        ConvexVolume,
        OffMeshConnection
    }
}
