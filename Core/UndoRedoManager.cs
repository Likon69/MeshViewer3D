using System;
using System.Collections.Generic;

namespace MeshViewer3D.Core
{
    /// <summary>
    /// Interface for an undoable editing command (Command Pattern).
    /// </summary>
    public interface IEditCommand
    {
        string Description { get; }
        void Execute();
        void Undo();
    }

    /// <summary>
    /// Manages undo/redo stacks for editing operations.
    /// Thread-safe for single-threaded UI usage.
    /// </summary>
    public class UndoRedoManager
    {
        private const int MaxUndoDepth = 100;
        private readonly Stack<IEditCommand> _undoStack = new();
        private readonly Stack<IEditCommand> _redoStack = new();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// Executes a command and pushes it onto the undo stack.
        /// Clears the redo stack (new action invalidates redo history).
        /// Evicts oldest entries when exceeding MaxUndoDepth.
        /// </summary>
        public void Execute(IEditCommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear();

            if (_undoStack.Count > MaxUndoDepth)
            {
                var temp = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = MaxUndoDepth - 1; i >= 0; i--)
                    _undoStack.Push(temp[i]);
            }
        }

        /// <summary>
        /// Undoes the last command.
        /// </summary>
        public IEditCommand? Undo()
        {
            if (_undoStack.Count == 0) return null;
            var cmd = _undoStack.Pop();
            cmd.Undo();
            _redoStack.Push(cmd);
            return cmd;
        }

        /// <summary>
        /// Redoes the last undone command.
        /// </summary>
        public IEditCommand? Redo()
        {
            if (_redoStack.Count == 0) return null;
            var cmd = _redoStack.Pop();
            cmd.Execute();
            _undoStack.Push(cmd);
            return cmd;
        }

        /// <summary>
        /// Clears all undo/redo history.
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }

        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;

        /// <summary>
        /// Peeks at the next undo command description without executing it.
        /// </summary>
        public string? PeekUndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;
        public string? PeekRedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;
    }
}
