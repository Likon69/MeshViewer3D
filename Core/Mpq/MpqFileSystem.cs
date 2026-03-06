// Core/Mpq/MpqFileSystem.cs
using System;
using System.Collections.Generic;
using System.IO;
using MpqLib;

namespace MeshViewer3D.Core.Mpq
{
    /// <summary>
    /// Virtual file system over a priority-ordered stack of MPQ archives.
    /// Higher-index archives override lower-index ones (patch > base).
    /// </summary>
    public sealed class MpqFileSystem : IDisposable
    {
        private readonly List<MpqArchive> _stack = new();
        private bool _disposed;

        public int ArchiveCount => _stack.Count;

        public void AddArchive(MpqArchive archive) => _stack.Add(archive);

        /// <summary>
        /// Returns the raw bytes of a file by its internal MPQ path,
        /// searching from highest-priority archive downward.
        /// Returns null if not found in any archive.
        /// </summary>
        public byte[]? GetFileBytes(string internalPath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MpqFileSystem));
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (!_stack[i].FileExists(internalPath)) continue;
                    using var stream = _stack[i].OpenFile(internalPath);
                    using var ms = new MemoryStream((int)stream.Length);
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
                catch (FileNotFoundException) { }
                catch (MpqParserException) { }
                catch (InvalidDataException) { }
            }
            return null;
        }

        /// <summary>Returns true if the file exists in any archive.</summary>
        public bool FileExists(string internalPath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MpqFileSystem));
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (_stack[i].FileExists(internalPath)) return true;
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var archive in _stack) archive.Dispose();
            _stack.Clear();
        }
    }
}
