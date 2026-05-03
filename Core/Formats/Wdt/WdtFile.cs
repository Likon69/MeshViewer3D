// ============================================================================
//  WdtFile.cs  —  WDT (World Data Table) parser, WoW 3.3.5a
//
//  Reads the tile existence grid from a WDT file so the minimap and tile
//  loader know which ADT tiles actually exist for a map without probing
//  all 4096 combinations.
//
//  Chunk flow:
//    MVER  → version (must be 18)
//    MPHD  → flags (1 = global map object, rarely used)
//    MAIN  → 64×64 tile grid (8 bytes each: flags + asyncObject)
//
//  Source: https://wowdev.wiki/WDT
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using MeshViewer3D.Core.Formats;

namespace MeshViewer3D.Core.Formats.Wdt
{
    /// <summary>
    /// Parsed WDT file exposing the 64×64 tile existence grid.
    /// </summary>
    public sealed class WdtFile
    {
        /// <summary>
        /// 64×64 grid of tile flags.
        /// Index: tileGrid[y * 64 + x], where x=tileX (0=west), y=tileY (0=north).
        /// Bit 0 of each entry = tile ADT exists.
        /// </summary>
        public uint[] TileFlags { get; private set; } = new uint[64 * 64];

        /// <summary>MPHD flags. Bit 1 = global WMO (map is a single dungeon WMO).</summary>
        public uint MphdFlags { get; private set; }

        /// <summary>True if this map is a single global WMO dungeon (e.g. Naxxramas).</summary>
        public bool IsGlobalWmo => (MphdFlags & 0x01) != 0;

        /// <summary>Total number of tiles that exist in this map.</summary>
        public int TileCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < TileFlags.Length; i++)
                    if ((TileFlags[i] & 0x01) != 0) count++;
                return count;
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  Factory
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Parse a WDT file from raw bytes (from MPQ).</summary>
        public static WdtFile Load(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            var wdt = new WdtFile();
            wdt.Parse(data);
            return wdt;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Parser
        // ────────────────────────────────────────────────────────────────────

        private void Parse(byte[] data)
        {
            bool foundMain = false;

            foreach (var (tag, off, len) in ChunkReader.ReadChunks(data, 0, data.Length))
            {
                switch (tag)
                {
                    case "MVER":
                        if (len < 4) throw new InvalidDataException("MVER chunk too small.");
                        int version = BitConverter.ToInt32(data, off);
                        if (version != 18)
                            throw new InvalidDataException($"Expected WDT v18, got v{version}.");
                        break;

                    case "MPHD":
                        // MPHD is 32 bytes (8 uint32 fields), but we only need flags.
                        if (len >= 4)
                            MphdFlags = BitConverter.ToUInt32(data, off);
                        break;

                    case "MAIN":
                        // MAIN is exactly 64×64×8 = 32768 bytes.
                        // Each entry: uint32 flags, uint32 asyncObject.
                        // We only read the flags (first uint of each pair).
                        if (len < 64 * 64 * 8)
                            throw new InvalidDataException($"MAIN chunk too small: {len} bytes (need {64 * 64 * 8}).");

                        for (int i = 0; i < 64 * 64; i++)
                        {
                            int entryOffset = off + i * 8;
                            TileFlags[i] = BitConverter.ToUInt32(data, entryOffset);
                        }
                        foundMain = true;
                        break;
                }
            }

            if (!foundMain)
                throw new InvalidDataException("MAIN chunk not found — not a valid WDT file.");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Tile queries
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Returns true if the ADT tile at (tileX, tileY) exists.</summary>
        public bool TileExists(int tileX, int tileY)
        {
            if (tileX < 0 || tileX >= 64 || tileY < 0 || tileY >= 64)
                return false;
            return (TileFlags[tileY * 64 + tileX] & 0x01) != 0;
        }

        /// <summary>
        /// Enumerates all existing tiles as (tileX, tileY) pairs.
        /// </summary>
        public IEnumerable<(int tileX, int tileY)> GetExistingTiles()
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    if ((TileFlags[y * 64 + x] & 0x01) != 0)
                        yield return (x, y);
        }
    }
}
