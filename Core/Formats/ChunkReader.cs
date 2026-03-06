// ============================================================================
//  ChunkReader.cs  —  Generic IFF chunk walker for all WoW binary formats
//
//  Why a shared static class instead of duplicating inside WmoFile/WmoGroup:
//    The existing WmoGroup.cs has its own private ReadChunks (line ~265).
//    WmoFile.cs has another. Sharing one walker removes that duplication.
//    ADT, M2, and WDT parsers added later get it for free.
//
//  Chunk layout used by every WoW IFF file:
//      [4-byte ASCII tag][4-byte LE uint32 dataSize][dataSize bytes of data]
//
//  Tag as string, not uint:
//    MemoryMarshal.Read<uint> gives "MOHD" as 0x44484F4D — opaque in a
//    switch/debugger.  Encoding.Latin1.GetString gives the readable string
//    "MOHD" — matches the WoW wiki table directly, zero confusion.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace MeshViewer3D.Core.Formats
{
    /// <summary>
    /// Stateless IFF chunk enumerator for WoW binary files (WMO, ADT, WDT, M2).
    /// </summary>
    internal static class ChunkReader
    {
        /// <summary>
        /// Enumerate all IFF chunks in <paramref name="data"/>[<paramref name="start"/>..<paramref name="end"/>).
        /// Returns <c>(tag, dataOffset, dataLength)</c> for each chunk found.
        /// Stops silently on truncated data (corrupt-safe: no exception on short file).
        /// </summary>
        public static IEnumerable<(string Tag, int DataOffset, int DataLength)>
            ReadChunks(byte[] data, int start, int end)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            int pos = start;
            while (pos + 8 <= end)
            {
                // 4-byte FourCC tag — WoW IFF files store tags in reversed byte order.
                // Raw bytes on disk for "MVER" are: 'R','E','V','M' (0x52,0x45,0x56,0x4D).
                // MaNGOS confirms this with its flipcc() helper that swaps [0]↔[3] and [1]↔[2].
                // We reverse here so tag strings match wowdev.wiki names directly.
                string tag = new string(new char[] {
                    (char)data[pos + 3], (char)data[pos + 2],
                    (char)data[pos + 1], (char)data[pos]
                });
                pos += 4;

                // LE uint32 size — BitConverter on x86/x64 Windows is always LE-correct.
                int size = BitConverter.ToInt32(data, pos);
                pos += 4;

                if (size < 0 || pos + size > end) yield break; // truncated / corrupt

                yield return (tag, pos, size);
                pos += size;
            }
        }
    }
}
