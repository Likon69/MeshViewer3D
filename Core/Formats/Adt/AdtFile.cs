// ============================================================================
//  AdtFile.cs  —  ADT v18 tile parser, WoW 3.3.5a
//
//  Parses: WMO placements, M2 placements, terrain heightmap (MCNK), and
//  texture filenames (MTEX).
//
//  Chunk flow:
//    MWMO ─┐                    MMDX ─┐
//    MWID ─┼─► WmoNames[]       MMID ─┼─► M2Names[]
//    MODF ─┘─► WmoInstances[]   MDDF ─┘─► M2Instances[]
//
//    MTEX ──► TextureNames[]    (null-terminated blob)
//    MCNK × 256 (16×16)         (terrain chunks with MCVT/MCNR/MCLY sub-chunks)
//
//  Indexing:
//    WmoNames[modf.mwidEntry]  → WMO filename for that placement
//    M2Names[mddf.mmidEntry]   → M2  filename for that placement
//
//  Source: https://wowdev.wiki/ADT/v18
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MeshViewer3D.Core.Formats;

namespace MeshViewer3D.Core.Formats.Adt
{
    /// <summary>
    /// Parses a single ADT v18 tile file including terrain heightmap data.
    /// </summary>
    public sealed class AdtFile
    {
        /// <summary>
        /// WMO filenames indexed by <see cref="MODF.mwidEntry"/>.
        /// Length = number of uint entries in the MWID chunk.
        /// </summary>
        public string[] WmoNames { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// M2 filenames indexed by <see cref="MDDF.mmidEntry"/>.
        /// Length = number of uint entries in the MMID chunk.
        /// </summary>
        public string[] M2Names { get; private set; } = Array.Empty<string>();

        /// <summary>Raw WMO placement records from the MODF chunk.</summary>
        public MODF[] WmoInstances { get; private set; } = Array.Empty<MODF>();

        /// <summary>Raw M2 placement records from the MDDF chunk.</summary>
        public MDDF[] M2Instances { get; private set; } = Array.Empty<MDDF>();

        /// <summary>
        /// Terrain chunks parsed from MCNK sub-chunks. 16×16 = 256 entries.
        /// Null entries mean the chunk has no heightmap data (should not happen in valid ADTs).
        /// </summary>
        public TerrainChunk?[] TerrainChunks { get; private set; } = new TerrainChunk?[256];

        /// <summary>
        /// Texture filenames from the MTEX chunk (null-terminated blob → split).
        /// Indexed by MclyEntry.textureId.
        /// </summary>
        public string[] TextureNames { get; private set; } = Array.Empty<string>();

        /// <summary>Tile X coordinate (from ADT filename, e.g. "World/Maps/Kalimdor/Kalimdor_31_45.adt" → 31)</summary>
        public int TileX { get; set; } = 31;

        /// <summary>Tile Y coordinate (from ADT filename, e.g. "World/Maps/Kalimdor/Kalimdor_31_45.adt" → 45)</summary>
        public int TileY { get; set; } = 31;

        // ────────────────────────────────────────────────────────────────────
        //  Factory
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Parse an ADT v18 file from raw bytes.</summary>
        /// <param name="data">Complete file content from the MPQ.</param>
        public static AdtFile Load(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            var adt = new AdtFile();
            adt.Parse(data);
            return adt;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Top-level parser
        // ────────────────────────────────────────────────────────────────────

        private void Parse(byte[] data)
        {
            // Collect all top-level chunk positions in a single pass
            var chunks = new Dictionary<string, List<(int offset, int length)>>();

            foreach (var (tag, off, len) in ChunkReader.ReadChunks(data, 0, data.Length))
            {
                if (!chunks.TryGetValue(tag, out var list))
                {
                    list = new List<(int, int)>();
                    chunks[tag] = list;
                }
                // MCNK appears 256 times — store all occurrences
                list.Add((off, len));
            }

            // Verify ADT version
            if (chunks.TryGetValue("MVER", out var mverList) && mverList.Count > 0)
            {
                var mver = mverList[0];
                if (mver.length < 4)
                    throw new InvalidDataException("MVER chunk too small.");
                int version = BitConverter.ToInt32(data, mver.offset);
                if (version != 18)
                    throw new InvalidDataException($"Expected ADT v18, got v{version}.");
            }

            // Parse name blobs first so instances can resolve filenames
            WmoNames = ParseNameBlob(data, chunks, "MWMO", "MWID");
            M2Names  = ParseNameBlob(data, chunks, "MMDX", "MMID");

            WmoInstances = ParseModf(data, chunks);
            M2Instances  = ParseMddf(data, chunks);

            // Parse texture filenames
            TextureNames = ParseMtex(data, chunks);

            // Parse terrain chunks (MCNK)
            ParseTerrainChunks(data, chunks);
        }

        // ────────────────────────────────────────────────────────────────────
        //  MTEX parser (texture filename blob)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses the MTEX chunk: a blob of null-terminated texture filenames.
        /// Split on null terminators into a string array.
        /// </summary>
        private static string[] ParseMtex(
            byte[] data,
            Dictionary<string, List<(int offset, int length)>> chunks)
        {
            if (!chunks.TryGetValue("MTEX", out var mtexList) || mtexList.Count == 0)
                return Array.Empty<string>();

            var mtex = mtexList[0];
            if (mtex.length == 0) return Array.Empty<string>();

            var blob = new byte[mtex.length];
            Buffer.BlockCopy(data, mtex.offset, blob, 0, mtex.length);

            // Split null-terminated strings
            var names = new List<string>();
            int start = 0;
            for (int i = 0; i < blob.Length; i++)
            {
                if (blob[i] == 0)
                {
                    if (i > start)
                    {
                        string name = Encoding.Latin1.GetString(blob, start, i - start);
                        names.Add(name);
                    }
                    start = i + 1;
                }
            }
            // Handle last string if not null-terminated
            if (start < blob.Length)
            {
                string name = Encoding.Latin1.GetString(blob, start, blob.Length - start);
                names.Add(name);
            }

            return names.ToArray();
        }

        // ────────────────────────────────────────────────────────────────────
        //  MCNK terrain chunk parser
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses all 256 MCNK chunks from the ADT file.
        /// Each MCNK has a 128-byte header followed by sub-chunks (MCVT, MCNR, MCLY, etc.)
        ///
        /// MCNK header layout (128 bytes, source: wowdev.wiki/ADT/v18#MCNK_chunk):
        /// +0x00  (4)  uint    flags
        /// +0x04  (4)  uint    indexX         Chunk X index (0–15)
        /// +0x08  (4)  uint    indexY         Chunk Y index (0–15)
        /// +0x0C  (4)  uint    nLayers        Number of texture layers
        /// +0x10  (4)  uint    nDoodadRefs
        /// +0x14  (4)  uint    mcvtOffset     Offset to MCVT sub-chunk data (from MCNK start)
        /// +0x18  (4)  uint    mcnrOffset     Offset to MCNR sub-chunk data
        /// +0x1C  (4)  uint    mclyOffset     Offset to MCLY sub-chunk data
        /// +0x20  (4)  uint    mcrfOffset     Offset to MCRF sub-chunk data
        /// +0x24  (4)  uint    mcalOffset     Offset to MCAL sub-chunk data
        /// +0x28  (1)  byte    mcalType       Alpha map format (0 = uncompressed, 1 = compressed)
        /// +0x29  (1)  byte    holeCompression
        /// +0x2A  (2)  ushort  padding1
        /// +0x2C  (4)  uint    mcshOffset     Offset to MCSH shadow map
        /// +0x30  (4)  float   minHeight      Bounding box min height
        /// +0x34  (4)  float   maxHeight      Bounding box max height
        /// +0x38  (4)  uint    holes          Hole bitfield (16×16 sub-chunks, each bit = 1 hole)
        /// +0x3C  (4)  uint    padding2
        /// +0x40  (12) float[3] position       World-space position (X, Y, Z) of chunk corner
        /// +0x4C  (4)  float   gap            Gap between terrain vertex rows (always 0.0?)
        /// WotLK MCNK header layout (128 bytes, source: wowdev.wiki/ADT/v18#MCNK_chunk):
        /// +0x00 (4) flags
        /// +0x04 (4) IndexX
        /// +0x08 (4) IndexY
        /// +0x0C (4) nLayers
        /// +0x10 (4) nDoodadRefs
        /// +0x14 (4) ofsHeight  → offset to MCVT IFF sub-chunk from MCNK data start
        /// +0x18 (4) ofsNormal  → offset to MCNR
        /// +0x1C (4) ofsLayer   → offset to MCLY IFF sub-chunk from MCNK data start
        /// +0x20 (4) ofsRefs
        /// +0x24 (4) ofsAlpha
        /// +0x28 (4) sizeAlpha
        /// +0x2C (4) ofsShadow
        /// +0x30 (4) areaid
        /// +0x34 (4) nMapObjRefs
        /// +0x38 (2) holes_low_res + (2) pad
        /// +0x3C (8) ReallyLowQualityTextureMap[4] (uint16 × 4)
        /// +0x44 (4) predTex
        /// +0x48 (4) noEffectDoodad
        /// +0x4C (4) ofsSndEmitters
        /// +0x50 (4) nSndEmitters
        /// +0x54 (4) ofsLiquid
        /// +0x58 (4) sizeLiquid
        /// +0x5C (4) position.x  (WoW world X)
        /// +0x60 (4) position.y  (WoW world Y)
        /// +0x64 (4) position.z  (WoW world Z = base height for MCVT)
        /// +0x68 (4) ofsMCCV  (WotLK)
        /// +0x6C (4) ofsMCLV  (WotLK)
        /// +0x70..0x7F padding
        /// Total: 128 bytes ✓
        /// </summary>
        private void ParseTerrainChunks(
            byte[] data,
            Dictionary<string, List<(int offset, int length)>> chunks)
        {
            if (!chunks.TryGetValue("MCNK", out var mcnkList))
                return;

            int chunkCount = Math.Min(mcnkList.Count, 256);

            // We read up to +0x70 (zpos/xpos/ypos) in the MCNK payload layout.
            const int HeaderReadSize = 116;

            for (int i = 0; i < chunkCount; i++)
            {
                var (mcnkOff, mcnkLen) = mcnkList[i];
                if (mcnkLen < 128 || mcnkOff + HeaderReadSize > data.Length) continue;

                // Read MCNK header fields we need
                using var ms = new MemoryStream(data, mcnkOff, HeaderReadSize, writable: false);
                using var br = new BinaryReader(ms);

                br.ReadUInt32();                          // +0x00 flags
                uint idxX      = br.ReadUInt32();          // +0x04
                uint idxY      = br.ReadUInt32();          // +0x08
                br.ReadUInt32();                          // +0x0C nLayers
                br.ReadUInt32();                          // +0x10 nDoodadRefs
                uint ofsHeight = br.ReadUInt32();          // +0x14 → MCVT
                br.ReadUInt32();                          // +0x18 ofsNormal → MCNR (unused)
                uint ofsLayer  = br.ReadUInt32();          // +0x1C → MCLY
                br.ReadUInt32();                          // +0x20 ofsRefs
                br.ReadUInt32();                          // +0x24 ofsAlpha
                br.ReadUInt32();                          // +0x28 sizeAlpha
                br.ReadUInt32();                          // +0x2C ofsShadow
                br.ReadUInt32();                          // +0x30 sizeMCSH
                br.ReadUInt32();                          // +0x34 areaid
                br.ReadUInt32();                          // +0x38 nMapObjRefs
                br.ReadUInt32();                          // +0x3C holes(2) + pad(2)
                br.ReadUInt32();                          // +0x40 s[2]
                br.ReadUInt32();                          // +0x44 data1
                br.ReadUInt32();                          // +0x48 data2
                br.ReadUInt32();                          // +0x4C data3
                br.ReadUInt32();                          // +0x50 predTex
                br.ReadUInt32();                          // +0x54 nEffectDoodad
                br.ReadUInt32();                          // +0x58 offsMCSE
                br.ReadUInt32();                          // +0x5C nSndEmitters
                br.ReadUInt32();                          // +0x60 offsMCLQ
                br.ReadUInt32();                          // +0x64 sizeMCLQ
                float zpos     = br.ReadSingle();          // +0x68 zpos (world Z/depth)
                float xpos     = br.ReadSingle();          // +0x6C xpos (world X)
                float ypos     = br.ReadSingle();          // +0x70 ypos (base height)

                var chunk = new TerrainChunk
                {
                    IndexX     = (int)idxX,
                    IndexY     = (int)idxY,
                    PositionX  = xpos,
                    PositionY  = zpos,
                    BaseHeight = ypos,
                };

                // MaNGOS loadlib::adt_MCNK::getMCVT() uses (this + offsMCVT), where
                // "this" points to the full MCNK chunk start including the 8-byte IFF
                // header. ChunkReader gives us the payload start, so convert back once.
                int mcnkChunkStart = mcnkOff - 8;

                // ── MCVT (heights) — use header offset to avoid MCNR 13-byte gap issue ──
                // MCNR stores 435 bytes of normals but may have 13 extra bytes after the
                // IFF chunk (not counted in its size field), which desynchronises a sequential
                // scanner.  Header offsets are immune to this.
                if (ofsHeight >= 8 && mcnkChunkStart >= 0)
                {
                    int sub = mcnkChunkStart + (int)ofsHeight;
                    if (sub + 8 <= data.Length)
                    {
                        int len = BitConverter.ToInt32(data, sub + 4);
                        if (len > 0 && sub + 8 + len <= data.Length)
                            ParseMcvt(data, sub + 8, len, chunk);
                    }
                }

                // ── MCLY (texture layers) — use header offset to bypass MCNR misalignment ──
                if (ofsLayer >= 8 && mcnkChunkStart >= 0)
                {
                    int sub = mcnkChunkStart + (int)ofsLayer;
                    if (sub + 8 <= data.Length)
                    {
                        int len = BitConverter.ToInt32(data, sub + 4);
                        if (len >= 16 && sub + 8 + len <= data.Length)
                            ParseMcly(data, sub + 8, len, chunk);
                    }
                }

                // Store in array indexed by y*16 + x
                int arrIdx = (int)idxY * 16 + (int)idxX;
                if (arrIdx >= 0 && arrIdx < 256)
                    TerrainChunks[arrIdx] = chunk;
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  MCVT parser (heightmap vertices)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses the MCVT sub-chunk: 145 float values forming the heightmap.
        ///
        /// Layout (17 rows, alternating 9 and 8 vertices):
        ///   Row 0: 9 vertices (outer)
        ///   Row 1: 8 vertices (inner, offset between outer rows)
        ///   Row 2: 9 vertices (outer)
        ///   ...
        ///   Row 16: 9 vertices (outer)
        ///
        /// Total: 9×9 + 8×8 = 81 + 64 = 145 floats.
        /// Values are relative to BaseHeight (MCNK header posZ field).
        /// </summary>
        private static void ParseMcvt(byte[] data, int offset, int length, TerrainChunk chunk)
        {
            const int ExpectedFloats = 145;
            const int ExpectedBytes = ExpectedFloats * 4; // 580
            if (length < ExpectedBytes) return;
            if (offset < 0 || offset + ExpectedBytes > data.Length) return;

            chunk.Heights = new float[ExpectedFloats];
            Buffer.BlockCopy(data, offset, chunk.Heights, 0, ExpectedBytes);
        }

        // ────────────────────────────────────────────────────────────────────
        //  MCNR parser (per-vertex normals)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses the MCNR sub-chunk: 145 × 3 signed bytes (X, Y, Z per vertex).
        /// Values are in range [-127, 127] and represent a normalized direction.
        /// Source: wowdev.wiki/ADT/v18#MCNR_chunk
        /// </summary>
        private static void ParseMcnr(byte[] data, int offset, int length, TerrainChunk chunk)
        {
            const int ExpectedBytes = 145 * 3; // 435
            if (length < ExpectedBytes) return;
            if (offset < 0 || offset + ExpectedBytes > data.Length) return;

            chunk.Normals = new sbyte[ExpectedBytes];
            Buffer.BlockCopy(data, offset, chunk.Normals, 0, ExpectedBytes);
        }

        // ────────────────────────────────────────────────────────────────────
        //  MCLY parser (texture layer definitions)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses the MCLY sub-chunk: array of 16-byte texture layer entries.
        /// Each entry describes one texture layer applied to this chunk.
        /// Layer 0 = base texture, layers 1–3 = blended with alpha maps.
        /// </summary>
        private static void ParseMcly(byte[] data, int offset, int length, TerrainChunk chunk)
        {
            if (length < 16) return;
            if (offset < 0 || offset + length > data.Length) return;

            int count = length / 16;
            var layers = new MclyEntry[count];

            using var ms = new MemoryStream(data, offset, length, writable: false);
            using var br = new BinaryReader(ms);

            for (int i = 0; i < count; i++)
            {
                layers[i] = new MclyEntry
                {
                    textureId   = br.ReadUInt32(),  // +0x00
                    flags       = br.ReadUInt32(),  // +0x04
                    alphaOffset = br.ReadUInt32(),  // +0x08
                    effectId    = br.ReadUInt32(),  // +0x0C
                };
            }

            chunk.TextureLayers = layers;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Name-blob parser  (shared by MWMO/MWID and MMDX/MMID)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves a name table from a filename blob chunk + index chunk.
        ///
        /// blobChunk  (e.g. MWMO) — concatenated null-terminated filenames.
        /// indexChunk (e.g. MWID) — uint[] of byte offsets into the blob.
        ///
        /// Returns string[] where result[i] = filename for index i.
        /// </summary>
        private static string[] ParseNameBlob(
            byte[] data,
            Dictionary<string, List<(int offset, int length)>> chunks,
            string blobChunkTag,
            string indexChunkTag)
        {
            // Build the raw blob (may be empty if no objects of this type)
            byte[] blob = Array.Empty<byte>();
            if (chunks.TryGetValue(blobChunkTag, out var blobList) && blobList.Count > 0)
            {
                var blobChunk = blobList[0];
                if (blobChunk.length > 0)
                {
                    blob = new byte[blobChunk.length];
                    Buffer.BlockCopy(data, blobChunk.offset, blob, 0, blobChunk.length);
                }
            }

            // Parse the uint[] index
            if (!chunks.TryGetValue(indexChunkTag, out var idxList) || idxList.Count == 0)
                return Array.Empty<string>();

            var idxChunk = idxList[0];
            if (idxChunk.length == 0) return Array.Empty<string>();

            int count = idxChunk.length / 4;
            var offsets = new uint[count];
            Buffer.BlockCopy(data, idxChunk.offset, offsets, 0, idxChunk.length);

            // Resolve each entry
            var names = new string[count];
            for (int i = 0; i < count; i++)
                names[i] = ReadNullTerminatedString(blob, (int)offsets[i]);

            return names;
        }

        // ────────────────────────────────────────────────────────────────────
        //  MODF parser  (WMO placements, 64 bytes each)
        // ────────────────────────────────────────────────────────────────────

        private static MODF[] ParseModf(
            byte[] data,
            Dictionary<string, List<(int offset, int length)>> chunks)
        {
            if (!chunks.TryGetValue("MODF", out var modfList) || modfList.Count == 0)
                return Array.Empty<MODF>();

            var modfChunk = modfList[0];
            if (modfChunk.length == 0) return Array.Empty<MODF>();

            int count = modfChunk.length / 64;
            var result = new MODF[count];

            using var ms = new MemoryStream(data, modfChunk.offset, modfChunk.length, writable: false);
            using var br = new BinaryReader(ms);

            for (int i = 0; i < count; i++)
                result[i] = ReadModf(br);

            return result;
        }

        /// <summary>Read one MODF record (64 bytes) field-by-field.</summary>
        private static MODF ReadModf(BinaryReader br) => new MODF
        {
            mwidEntry  = br.ReadUInt32(),   // +0x00
            uniqueId   = br.ReadUInt32(),   // +0x04
            posX       = br.ReadSingle(),   // +0x08
            posY       = br.ReadSingle(),   // +0x0C
            posZ       = br.ReadSingle(),   // +0x10
            rotX       = br.ReadSingle(),   // +0x14
            rotY       = br.ReadSingle(),   // +0x18
            rotZ       = br.ReadSingle(),   // +0x1C
            boundsMinX = br.ReadSingle(),   // +0x20
            boundsMinY = br.ReadSingle(),   // +0x24
            boundsMinZ = br.ReadSingle(),   // +0x28
            boundsMaxX = br.ReadSingle(),   // +0x2C
            boundsMaxY = br.ReadSingle(),   // +0x30
            boundsMaxZ = br.ReadSingle(),   // +0x34
            flags      = br.ReadUInt16(),   // +0x38
            doodadSet  = br.ReadUInt16(),   // +0x3A
            nameSet    = br.ReadUInt16(),   // +0x3C
            scale      = br.ReadUInt16(),   // +0x3E
        };

        // ────────────────────────────────────────────────────────────────────
        //  MDDF parser  (M2 placements, 36 bytes each)
        // ────────────────────────────────────────────────────────────────────

        private static MDDF[] ParseMddf(
            byte[] data,
            Dictionary<string, List<(int offset, int length)>> chunks)
        {
            if (!chunks.TryGetValue("MDDF", out var mddfList) || mddfList.Count == 0)
                return Array.Empty<MDDF>();

            var mddfChunk = mddfList[0];
            if (mddfChunk.length == 0) return Array.Empty<MDDF>();

            int count = mddfChunk.length / 36;
            var result = new MDDF[count];

            using var ms = new MemoryStream(data, mddfChunk.offset, mddfChunk.length, writable: false);
            using var br = new BinaryReader(ms);

            for (int i = 0; i < count; i++)
                result[i] = ReadMddf(br);

            return result;
        }

        /// <summary>Read one MDDF record (36 bytes) field-by-field.</summary>
        private static MDDF ReadMddf(BinaryReader br) => new MDDF
        {
            mmidEntry = br.ReadUInt32(),    // +0x00
            uniqueId  = br.ReadUInt32(),    // +0x04
            posX      = br.ReadSingle(),    // +0x08
            posY      = br.ReadSingle(),    // +0x0C
            posZ      = br.ReadSingle(),    // +0x10
            rotX      = br.ReadSingle(),    // +0x14
            rotY      = br.ReadSingle(),    // +0x18
            rotZ      = br.ReadSingle(),    // +0x1C
            scale     = br.ReadUInt16(),    // +0x20
            flags     = br.ReadUInt16(),    // +0x22
        };

        // ────────────────────────────────────────────────────────────────────
        //  String helper
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Read a null-terminated ASCII/Latin-1 string from <paramref name="blob"/>
        /// starting at <paramref name="offset"/>.
        /// Returns <see cref="string.Empty"/> for out-of-range or empty strings.
        /// </summary>
        private static string ReadNullTerminatedString(byte[] blob, int offset)
        {
            if (offset < 0 || offset >= blob.Length)
                return string.Empty;

            int end = offset;
            while (end < blob.Length && blob[end] != 0)
                end++;

            int length = end - offset;
            return length > 0
                ? Encoding.Latin1.GetString(blob, offset, length)
                : string.Empty;
        }
    }
}
