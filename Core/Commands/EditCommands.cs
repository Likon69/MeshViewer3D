using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using MeshViewer3D.Data;

namespace MeshViewer3D.Core.Commands
{
    /// <summary>
    /// Command: Add a blackspot to the editable elements list.
    /// </summary>
    public class AddBlackspotCommand : IEditCommand
    {
        private readonly EditableElements _elements;
        private readonly Blackspot _blackspot;
        private readonly Action _onChanged;
        public string Description => $"Add blackspot '{_blackspot.Name}'";

        public AddBlackspotCommand(EditableElements elements, Blackspot blackspot, Action onChanged)
        {
            _elements = elements;
            _blackspot = blackspot;
            _onChanged = onChanged;
        }

        public void Execute()
        {
            _elements.Blackspots.Add(_blackspot);
            _onChanged();
        }

        public void Undo()
        {
            _elements.Blackspots.Remove(_blackspot);
            _elements.ClearSelection();
            _onChanged();
        }
    }

    /// <summary>
    /// Command: Remove a blackspot by index.
    /// </summary>
    public class RemoveBlackspotCommand : IEditCommand
    {
        private readonly EditableElements _elements;
        private readonly Blackspot _blackspot;
        private readonly int _index;
        private readonly Action _onChanged;
        public string Description => $"Remove blackspot '{_blackspot.Name}'";

        public RemoveBlackspotCommand(EditableElements elements, int index, Action onChanged)
        {
            _elements = elements;
            _index = index;
            _blackspot = elements.Blackspots[index];
            _onChanged = onChanged;
        }

        public void Execute()
        {
            _elements.Blackspots.Remove(_blackspot);
            _elements.ClearSelection();
            _onChanged();
        }

        public void Undo()
        {
            int insertAt = Math.Min(_index, _elements.Blackspots.Count);
            _elements.Blackspots.Insert(insertAt, _blackspot);
            _onChanged();
        }
    }

    /// <summary>
    /// Command: Move a blackspot to a new position.
    /// </summary>
    public class MoveBlackspotCommand : IEditCommand
    {
        private readonly EditableElements _elements;
        private readonly int _index;
        private readonly Vector3 _oldPosition;
        private readonly Vector3 _newPosition;
        private readonly Action _onChanged;
        public string Description => "Move blackspot";

        public MoveBlackspotCommand(EditableElements elements, int index, Vector3 oldPosition, Vector3 newPosition, Action onChanged)
        {
            _elements = elements;
            _index = index;
            _oldPosition = oldPosition;
            _newPosition = newPosition;
            _onChanged = onChanged;
        }

        public void Execute()
        {
            if (_index < _elements.Blackspots.Count)
            {
                var bs = _elements.Blackspots[_index];
                bs.Location = _newPosition;
                _elements.Blackspots[_index] = bs;
                _onChanged();
            }
        }

        public void Undo()
        {
            if (_index < _elements.Blackspots.Count)
            {
                var bs = _elements.Blackspots[_index];
                bs.Location = _oldPosition;
                _elements.Blackspots[_index] = bs;
                _onChanged();
            }
        }
    }

    /// <summary>
    /// Command: Add a convex volume.
    /// </summary>
    public class AddVolumeCommand : IEditCommand
    {
        private readonly EditableElements _elements;
        private readonly ConvexVolume _volume;
        private readonly Action _onChanged;
        public string Description => $"Add volume (area {_volume.AreaType})";

        public AddVolumeCommand(EditableElements elements, ConvexVolume volume, Action onChanged)
        {
            _elements = elements;
            _volume = volume;
            _onChanged = onChanged;
        }

        public void Execute()
        {
            _elements.ConvexVolumes.Add(_volume);
            _onChanged();
        }

        public void Undo()
        {
            _elements.ConvexVolumes.Remove(_volume);
            _elements.ClearSelection();
            _onChanged();
        }
    }

    /// <summary>
    /// Command: Remove a convex volume by index.
    /// </summary>
    public class RemoveVolumeCommand : IEditCommand
    {
        private readonly EditableElements _elements;
        private readonly ConvexVolume _volume;
        private readonly int _index;
        private readonly Action _onChanged;
        public string Description => $"Remove volume (area {_volume.AreaType})";

        public RemoveVolumeCommand(EditableElements elements, int index, Action onChanged)
        {
            _elements = elements;
            _index = index;
            _volume = elements.ConvexVolumes[index];
            _onChanged = onChanged;
        }

        public void Execute()
        {
            _elements.ConvexVolumes.Remove(_volume);
            _elements.ClearSelection();
            _onChanged();
        }

        public void Undo()
        {
            int insertAt = Math.Min(_index, _elements.ConvexVolumes.Count);
            _elements.ConvexVolumes.Insert(insertAt, _volume);
            _onChanged();
        }
    }

    /// <summary>
    /// Command: Add a custom off-mesh connection (jump link).
    /// </summary>
    public class AddOffMeshCommand : IEditCommand
    {
        private readonly EditableElements _elements;
        private readonly OffMeshConnection _connection;
        private readonly Action _onChanged;
        public string Description => $"Add jump link '{_connection.Name}'";

        public AddOffMeshCommand(EditableElements elements, OffMeshConnection connection, Action onChanged)
        {
            _elements = elements;
            _connection = connection;
            _onChanged = onChanged;
        }

        public void Execute()
        {
            _elements.CustomOffMeshConnections.Add(_connection);
            _onChanged();
        }

        public void Undo()
        {
            _elements.CustomOffMeshConnections.Remove(_connection);
            _elements.ClearSelection();
            _onChanged();
        }
    }

    /// <summary>
    /// Command: Remove a custom off-mesh connection by index.
    /// </summary>
    public class RemoveOffMeshCommand : IEditCommand
    {
        private readonly EditableElements _elements;
        private readonly OffMeshConnection _connection;
        private readonly int _index;
        private readonly Action _onChanged;
        public string Description => $"Remove jump link '{_connection.Name}'";

        public RemoveOffMeshCommand(EditableElements elements, int index, Action onChanged)
        {
            _elements = elements;
            _index = index;
            _connection = elements.CustomOffMeshConnections[index];
            _onChanged = onChanged;
        }

        public void Execute()
        {
            _elements.CustomOffMeshConnections.Remove(_connection);
            _elements.ClearSelection();
            _onChanged();
        }

        public void Undo()
        {
            int insertAt = Math.Min(_index, _elements.CustomOffMeshConnections.Count);
            _elements.CustomOffMeshConnections.Insert(insertAt, _connection);
            _onChanged();
        }
    }

    /// <summary>
    /// Command: Resize a blackspot (radius and/or height).
    /// </summary>
    public class ResizeBlackspotCommand : IEditCommand
    {
        private readonly EditableElements _elements;
        private readonly int _index;
        private readonly float _oldRadius;
        private readonly float _newRadius;
        private readonly float _oldHeight;
        private readonly float _newHeight;
        private readonly Action _onChanged;
        public string Description => "Resize blackspot";

        public ResizeBlackspotCommand(EditableElements elements, int index,
            float oldRadius, float newRadius, float oldHeight, float newHeight, Action onChanged)
        {
            _elements = elements;
            _index = index;
            _oldRadius = oldRadius;
            _newRadius = newRadius;
            _oldHeight = oldHeight;
            _newHeight = newHeight;
            _onChanged = onChanged;
        }

        public void Execute()
        {
            if (_index < _elements.Blackspots.Count)
            {
                var bs = _elements.Blackspots[_index];
                bs.Radius = _newRadius;
                bs.Height = _newHeight;
                _elements.Blackspots[_index] = bs;
                _onChanged();
            }
        }

        public void Undo()
        {
            if (_index < _elements.Blackspots.Count)
            {
                var bs = _elements.Blackspots[_index];
                bs.Radius = _oldRadius;
                bs.Height = _oldHeight;
                _elements.Blackspots[_index] = bs;
                _onChanged();
            }
        }
    }

    /// <summary>
    /// Command: Edit convex volume properties (AreaType, MinHeight, MaxHeight).
    /// </summary>
    public class EditVolumePropertiesCommand : IEditCommand
    {
        private readonly EditableElements _elements;
        private readonly int _index;
        private readonly Data.AreaType _oldAreaType;
        private readonly Data.AreaType _newAreaType;
        private readonly float _oldMinHeight;
        private readonly float _newMinHeight;
        private readonly float _oldMaxHeight;
        private readonly float _newMaxHeight;
        private readonly Action _onChanged;
        public string Description => "Edit volume properties";

        public EditVolumePropertiesCommand(
            EditableElements elements, int index,
            Data.AreaType oldAreaType, Data.AreaType newAreaType,
            float oldMinHeight, float newMinHeight,
            float oldMaxHeight, float newMaxHeight,
            Action onChanged)
        {
            _elements = elements;
            _index = index;
            _oldAreaType = oldAreaType;
            _newAreaType = newAreaType;
            _oldMinHeight = oldMinHeight;
            _newMinHeight = newMinHeight;
            _oldMaxHeight = oldMaxHeight;
            _newMaxHeight = newMaxHeight;
            _onChanged = onChanged;
        }

        public void Execute()
        {
            if (_index < _elements.ConvexVolumes.Count)
            {
                var vol = _elements.ConvexVolumes[_index];
                vol.AreaType = _newAreaType;
                vol.MinHeight = _newMinHeight;
                vol.MaxHeight = _newMaxHeight;
                _elements.ConvexVolumes[_index] = vol;
                _onChanged();
            }
        }

        public void Undo()
        {
            if (_index < _elements.ConvexVolumes.Count)
            {
                var vol = _elements.ConvexVolumes[_index];
                vol.AreaType = _oldAreaType;
                vol.MinHeight = _oldMinHeight;
                vol.MaxHeight = _oldMaxHeight;
                _elements.ConvexVolumes[_index] = vol;
                _onChanged();
            }
        }
    }
}
