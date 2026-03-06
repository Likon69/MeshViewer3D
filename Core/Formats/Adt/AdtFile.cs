// ============================================================================
//  AdtFile.cs  —  ADT v18 tile parser, WoW 3.3.5a
//
//  Extracts WMO and M2 object placements only.
//  Terrain (MCNK/heightmap), liquid, and textures are intentionally ignored.
//
//  Chunk flow:
//    MWMO ─┐                    MMDX ─┐
//    MWID ─┼─► WmoNames[]       MMID ─┼─► M2Names[]
//    MODF ─┘─► WmoInstances[]   MDDF ─┘─► M2Instances[]
//
//  Indexing:
//    WmoNames[modf.mwidEntry]  → WMO filename for that placement
//    M2Names[mddf.mmidEntry]   → M2  filename for that placement
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MeshViewer3D.Core.Formats;

namespace MeshViewer3D.Core.Formats.Adt
{
    /// <summary>
    /// Parses a single ADT v18 tile file.
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
            // Collect all relevant chunk positions in a single pass
            var chunks = new Dictionary<string, (int offset, int length)>();

            foreach (var (tag, off, len) in ChunkReader.ReadChunks(data, 0, data.Length))
            {
                // Last chunk with a given tag wins (consistent with patch priority)
                chunks[tag] = (off, len);
            }

            // Debug: log all found chunks
            Console.WriteLine($"  ADT chunks found: {chunks.Count} unique tags");
            foreach (var kvp in chunks)
                Console.WriteLine($"    {kvp.Key}: offset={kvp.Value.offset}, length={kvp.Value.length}");
            Console.WriteLine($"  Has MWMO={chunks.ContainsKey("MWMO")}, MWID={chunks.ContainsKey("MWID")}, MODF={chunks.ContainsKey("MODF")}");

            // Verify ADT version
            if (chunks.TryGetValue("MVER", out var mver))
            {
                if (mver.length < 4)
                    throw new InvalidDataException("MVER chunk too small.");
                int version = BitConverter.ToInt32(data, mver.offset);
                if (version != 18)
                    throw new InvalidDataException($"Expected ADT v18, got v{version}.");
            }
            // MVER missing → tolerate (some tool-generated ADTs omit it)

            // Parse name blobs first so instances can resolve filenames
            WmoNames = ParseNameBlob(data, chunks, "MWMO", "MWID");
            M2Names  = ParseNameBlob(data, chunks, "MMDX", "MMID");

            WmoInstances = ParseModf(data, chunks);
            M2Instances  = ParseMddf(data, chunks);
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
            Dictionary<string, (int offset, int length)> chunks,
            string blobChunkTag,
            string indexChunkTag)
        {
            // Build the raw blob (may be empty if no objects of this type)
            byte[] blob = Array.Empty<byte>();
            if (chunks.TryGetValue(blobChunkTag, out var blobChunk) && blobChunk.length > 0)
            {
                blob = new byte[blobChunk.length];
                Buffer.BlockCopy(data, blobChunk.offset, blob, 0, blobChunk.length);
            }

            // Parse the uint[] index
            if (!chunks.TryGetValue(indexChunkTag, out var idxChunk) || idxChunk.length == 0)
                return Array.Empty<string>();

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
            Dictionary<string, (int offset, int length)> chunks)
        {
            if (!chunks.TryGetValue("MODF", out var modfChunk) || modfChunk.length == 0)
                return Array.Empty<MODF>();

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
            Dictionary<string, (int offset, int length)> chunks)
        {
            if (!chunks.TryGetValue("MDDF", out var mddfChunk) || mddfChunk.length == 0)
                return Array.Empty<MDDF>();

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
