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
                catch (MpqParserException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MPQ] MpqParserException for '{internalPath}' in archive {i}: {ex.Message}");
                }
                catch (InvalidDataException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MPQ] InvalidDataException for '{internalPath}' in archive {i}: {ex.Message}");
                }
                catch (NotImplementedException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MPQ] NotImplementedException for '{internalPath}' in archive {i}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MPQ] Exception for '{internalPath}' in archive {i}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Diagnostic: returns which archive index contains the file and what error occurs during read, if any.
        /// Returns a human-readable summary string.
        /// </summary>
        public string DiagnoseFile(string internalPath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MpqFileSystem));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"DiagnoseFile: '{internalPath}' across {_stack.Count} archive(s)");
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                bool exists = _stack[i].FileExists(internalPath);
                if (!exists) { sb.AppendLine($"  [{i}] FileExists=false"); continue; }
                sb.Append($"  [{i}] FileExists=true → ");
                try
                {
                    using var stream = _stack[i].OpenFile(internalPath);
                    using var ms = new MemoryStream((int)stream.Length);
                    stream.CopyTo(ms);
                    sb.AppendLine($"Read OK ({ms.Length} bytes)");
                    return sb.ToString();
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"Read FAILED: {ex.GetType().Name}: {ex.Message}");
                }
            }
            sb.AppendLine("  Not found in any archive.");
            return sb.ToString();
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
