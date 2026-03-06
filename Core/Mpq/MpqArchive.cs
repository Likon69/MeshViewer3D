using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MpqLib
{
    // ─────────────────────────────────────────────────────────────────────────
    // MpqParserException
    // ─────────────────────────────────────────────────────────────────────────
    public class MpqParserException : Exception
    {
        public MpqParserException() { }
        public MpqParserException(string message) : base(message) { }
        public MpqParserException(string message, Exception inner) : base(message, inner) { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MpqFileFlags
    // ─────────────────────────────────────────────────────────────────────────
    [Flags]
    public enum MpqFileFlags : uint
    {
        CompressedPK           = 0x100,
        CompressedMulti        = 0x200,
        Compressed             = 0xff00,
        Encrypted              = 0x10000,
        BlockOffsetAdjustedKey = 0x020000,
        SingleUnit             = 0x1000000,
        FileHasMetadata        = 0x04000000,
        Exists                 = 0x80000000
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MpqEntry  (one entry in the block table)
    // ─────────────────────────────────────────────────────────────────────────
    public class MpqEntry
    {
        private uint   _fileOffset;   // relative to start of MPQ
        private string _filename;

        public uint         FilePos        { get; private set; }   // absolute in stream
        public uint         CompressedSize { get; private set; }
        public uint         FileSize       { get; private set; }
        public MpqFileFlags Flags          { get; private set; }
        public uint         EncryptionSeed { get; internal set; }

        public const int Size = 16;

        public string Filename
        {
            get => _filename;
            set
            {
                _filename      = value?.Replace('/', '\\');
                EncryptionSeed = CalculateEncryptionSeed();
            }
        }

        public MpqEntry(BinaryReader br, uint headerOffset)
        {
            _fileOffset    = br.ReadUInt32();
            FilePos        = headerOffset + _fileOffset;
            CompressedSize = br.ReadUInt32();
            FileSize       = br.ReadUInt32();
            Flags          = (MpqFileFlags)br.ReadUInt32();
        }

        private uint CalculateEncryptionSeed()
        {
            if (_filename == null) return 0;
            uint seed = MpqArchive.HashString(Path.GetFileName(_filename), 0x300);
            if ((Flags & MpqFileFlags.BlockOffsetAdjustedKey) != 0)
                seed = (seed + _fileOffset) ^ FileSize;
            return seed;
        }

        public bool IsEncrypted  => (Flags & MpqFileFlags.Encrypted)  != 0;
        public bool IsCompressed => (Flags & MpqFileFlags.Compressed) != 0;
        public bool Exists       => (Flags & MpqFileFlags.Exists)     != 0;
        public bool IsSingleUnit => (Flags & MpqFileFlags.SingleUnit) != 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MpqHash  (one entry in the hash table)
    // ─────────────────────────────────────────────────────────────────────────
    internal struct MpqHash
    {
        public const int Size = 16;

        public uint Name1      { get; }
        public uint Name2      { get; }
        public uint Locale     { get; }
        public uint BlockIndex { get; }

        public MpqHash(BinaryReader br)
        {
            Name1      = br.ReadUInt32();
            Name2      = br.ReadUInt32();
            Locale     = br.ReadUInt32();
            BlockIndex = br.ReadUInt32();
        }

        public bool IsEmpty   => BlockIndex == 0xFFFFFFFF;
        public bool IsDeleted => BlockIndex == 0xFFFFFFFE;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MpqHeader
    // ─────────────────────────────────────────────────────────────────────────
    public class MpqHeader
    {
        public const uint MpqId = 0x1a51504d;  // 'MPQ\x1a'
        public const uint Size  = 32;           // v1 header size

        public uint  ID;
        public uint  DataOffset;
        public uint  ArchiveSize;
        public ushort MpqVersion;
        public ushort BlockSize;
        public uint  HashTablePos;
        public uint  BlockTablePos;
        public uint  HashTableSize;
        public uint  BlockTableSize;
        // v2 extension
        public long  ExtendedBlockTableOffset;
        public short HashTableOffsetHigh;
        public short BlockTableOffsetHigh;

        public static MpqHeader FromReader(BinaryReader br)
        {
            var h = new MpqHeader
            {
                ID             = br.ReadUInt32(),
                DataOffset     = br.ReadUInt32(),
                ArchiveSize    = br.ReadUInt32(),
                MpqVersion     = br.ReadUInt16(),
                BlockSize      = br.ReadUInt16(),
                HashTablePos   = br.ReadUInt32(),
                BlockTablePos  = br.ReadUInt32(),
                HashTableSize  = br.ReadUInt32(),
                BlockTableSize = br.ReadUInt32()
            };

            if (h.MpqVersion >= 2 && br.BaseStream.Position + 12 <= br.BaseStream.Length)
            {
                h.ExtendedBlockTableOffset = br.ReadInt64();
                h.HashTableOffsetHigh      = br.ReadInt16();
                h.BlockTableOffsetHigh     = br.ReadInt16();
            }

            return h;
        }

        /// <summary>
        /// Adjusts all table positions from MPQ-relative to absolute stream positions.
        /// </summary>
        public void SetHeaderOffset(long headerOffset)
        {
            HashTablePos  += (uint)headerOffset;
            BlockTablePos += (uint)headerOffset;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MpqArchive
    // ─────────────────────────────────────────────────────────────────────────
    public class MpqArchive : IDisposable, IEnumerable<MpqEntry>
    {
        private static readonly uint[] sStormBuffer = BuildStormBuffer();

        internal Stream    BaseStream { get; private set; }
        internal int       BlockSize  { get; private set; }  // 0x200 << header.BlockSize
        private MpqHeader  _header;
        private MpqHash[]  _hashes;
        private MpqEntry[] _entries;
        private uint       _hashMask;
        private long       _headerOffset;
        private bool       _disposed;

        private const uint HASH_ENTRY_EMPTY   = 0xFFFFFFFF;
        private const uint HASH_ENTRY_DELETED = 0xFFFFFFFE;

        // ── Constructors ──────────────────────────────────────────────────────

        public MpqArchive(string filename)
        {
            BaseStream = File.Open(filename, FileMode.Open, FileAccess.Read);
            Init();
        }

        public MpqArchive(Stream sourceStream)
        {
            BaseStream = sourceStream;
            Init();
        }

        // ── Public properties ─────────────────────────────────────────────────

        public int Count => _entries.Length;

        // ── Initialisation ────────────────────────────────────────────────────

        private void Init()
        {
            if (!LocateMpqHeader())
                throw new MpqParserException("Unable to find MPQ header in stream.");

            BlockSize = 0x200 << _header.BlockSize;

            using var br = new BinaryReader(BaseStream, Encoding.UTF8, leaveOpen: true);

            // Hash table
            BaseStream.Seek(_header.HashTablePos, SeekOrigin.Begin);
            var hashData = br.ReadBytes((int)(_header.HashTableSize * MpqHash.Size));
            DecryptTable(hashData, "(hash table)");

            using (var ms = new MemoryStream(hashData, false))
            using (var hr = new BinaryReader(ms))
            {
                _hashes = new MpqHash[_header.HashTableSize];
                for (int i = 0; i < _hashes.Length; i++)
                    _hashes[i] = new MpqHash(hr);
            }

            _hashMask = (uint)(_hashes.Length - 1);

            // Block table
            BaseStream.Seek(_header.BlockTablePos, SeekOrigin.Begin);
            var blockData = br.ReadBytes((int)(_header.BlockTableSize * MpqEntry.Size));
            DecryptTable(blockData, "(block table)");

            using (var ms = new MemoryStream(blockData, false))
            using (var blr = new BinaryReader(ms))
            {
                _entries = new MpqEntry[_header.BlockTableSize];
                for (int i = 0; i < _entries.Length; i++)
                    _entries[i] = new MpqEntry(blr, (uint)_headerOffset);
            }

            AddListfileFilenames();
        }

        private bool LocateMpqHeader()
        {
            using var br      = new BinaryReader(BaseStream, Encoding.UTF8, leaveOpen: true);
            long      fileSize = BaseStream.Length;

            for (long offset = 0; offset + MpqHeader.Size <= fileSize; offset += 0x200)
            {
                BaseStream.Seek(offset, SeekOrigin.Begin);
                if (br.ReadUInt32() == MpqHeader.MpqId)
                {
                    BaseStream.Seek(offset, SeekOrigin.Begin);
                    _header       = MpqHeader.FromReader(br);
                    _headerOffset = offset;
                    _header.SetHeaderOffset(offset);
                    return true;
                }
            }

            return false;
        }

        // ── File access ───────────────────────────────────────────────────────

        public MpqStream OpenFile(string filename)
        {
            filename = filename?.Replace('/', '\\')
                ?? throw new ArgumentNullException(nameof(filename));

            if (!TryGetHashEntry(filename, out var entry))
                throw new FileNotFoundException($"File not found in MPQ: {filename}");

            if (entry.Filename == null)
                entry.Filename = filename;

            return new MpqStream(this, entry);
        }

        public MpqStream OpenFile(MpqEntry entry)
        {
            return new MpqStream(this, entry);
        }

        public bool FileExists(string filename)
        {
            filename = filename?.Replace('/', '\\')
                ?? throw new ArgumentNullException(nameof(filename));
            return TryGetHashEntry(filename, out _);
        }

        // ── Listfile / filename population ────────────────────────────────────

        private void AddListfileFilenames()
        {
            if (!TryGetHashEntry("(listfile)", out var entry))
                return;

            using var s = new MpqStream(this, entry);
            AddFilenames(s);
        }

        private void AddFilenames(Stream stream)
        {
            using var sr = new StreamReader(stream);
            string line;
            while ((line = sr.ReadLine()) != null)
                AddFilename(line);
        }

        private void AddFilename(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return;
            if (!TryGetHashEntry(filename, out var entry))
                return;
            entry.Filename = filename;
        }

        // ── Hash table probe (double-loop) ────────────────────────────────────

        private bool TryGetHashEntry(string filename, out MpqEntry entry)
        {
            int  startIndex = (int)(HashString(filename, 0) & _hashMask);
            uint name1      = HashString(filename, 0x100);
            uint name2      = HashString(filename, 0x200);

            for (int i = startIndex; i < _hashes.Length; i++)
            {
                var hash = _hashes[i];
                if (hash.BlockIndex == HASH_ENTRY_EMPTY)
                    break;
                if (hash.BlockIndex != HASH_ENTRY_DELETED
                    && hash.Name1 == name1 && hash.Name2 == name2)
                {
                    entry = _entries[(int)hash.BlockIndex];
                    return true;
                }
            }

            for (int i = 0; i < startIndex; i++)
            {
                var hash = _hashes[i];
                if (hash.BlockIndex == HASH_ENTRY_EMPTY)
                    break;
                if (hash.BlockIndex != HASH_ENTRY_DELETED
                    && hash.Name1 == name1 && hash.Name2 == name2)
                {
                    entry = _entries[(int)hash.BlockIndex];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        // ── Crypto helpers ────────────────────────────────────────────────────

        public static uint HashString(string fileName, uint hashType)
        {
            uint seed1 = 0x7FED7FED;
            uint seed2 = 0xEEEEEEEE;

            foreach (char c in fileName.ToUpperInvariant())
            {
                uint ch = (uint)c;
                seed1   = sStormBuffer[hashType + ch] ^ (seed1 + seed2);
                seed2   = ch + seed1 + seed2 + (seed2 << 5) + 3;
            }

            return seed1;
        }

        private static void DecryptTable(byte[] data, string key)
        {
            DecryptBlock(data, HashString(key, 0x300));
        }

        internal static void DecryptBlock(byte[] data, uint seed)
        {
            uint seed2 = 0xEEEEEEEE;
            int  count = data.Length >> 2;

            for (int i = 0; i < count; i++)
            {
                seed2 += sStormBuffer[0x400 + (seed & 0xFF)];

                uint value = BitConverter.ToUInt32(data, i * 4);
                value ^= seed + seed2;

                int off = i * 4;
                data[off]     = (byte)(value);
                data[off + 1] = (byte)(value >>  8);
                data[off + 2] = (byte)(value >> 16);
                data[off + 3] = (byte)(value >> 24);

                seed  = ((~seed << 21) + 0x11111111) | (seed >> 11);
                seed2 = value + seed2 + (seed2 << 5) + 3;
            }
        }

        internal static void DecryptBlock(uint[] data, uint seed)
        {
            uint seed2 = 0xEEEEEEEE;

            for (int i = 0; i < data.Length; i++)
            {
                seed2   += sStormBuffer[0x400 + (seed & 0xFF)];
                data[i] ^= seed + seed2;
                seed     = ((~seed << 21) + 0x11111111) | (seed >> 11);
                seed2    = data[i] + seed2 + (seed2 << 5) + 3;
            }
        }

        /// <summary>
        /// Recovers the encryption seed from two known-plaintext uint values.
        /// </summary>
        internal static uint DetectFileSeed(uint value0, uint value1, uint decrypted)
        {
            uint temp = (value0 ^ decrypted) - 0xeeeeeeee;

            for (int i = 0; i < 0x100; i++)
            {
                uint seed1 = temp - sStormBuffer[0x400 + i];
                uint seed2 = 0xeeeeeeee + sStormBuffer[0x400 + (seed1 & 0xff)];
                uint result = value0 ^ (seed1 + seed2);

                if (result != decrypted) continue;

                uint saveseed1 = seed1;
                seed1 = ((~seed1 << 21) + 0x11111111) | (seed1 >> 11);
                seed2 = result + seed2 + (seed2 << 5) + 3;
                seed2 += sStormBuffer[0x400 + (seed1 & 0xff)];
                result = value1 ^ (seed1 + seed2);

                if ((result & 0xfffc0000) == 0) return saveseed1;
            }

            return 0;
        }

        // ── Storm buffer ──────────────────────────────────────────────────────

        private static uint[] BuildStormBuffer()
        {
            uint   seed   = 0x00100001;
            uint[] result = new uint[0x500];

            for (uint i = 0; i < 0x100; i++)
            {
                uint index = i;
                for (int j = 0; j < 5; j++, index += 0x100)
                {
                    seed          = (seed * 125 + 3) % 0x2AAAAB;
                    uint temp1    = (seed & 0xFFFF) << 0x10;
                    seed          = (seed * 125 + 3) % 0x2AAAAB;
                    result[index] = temp1 | (seed & 0xFFFF);
                }
            }

            return result;
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (!_disposed)
            {
                BaseStream?.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        // ── IEnumerable<MpqEntry> ─────────────────────────────────────────────

        public IEnumerator<MpqEntry> GetEnumerator()
            => ((IEnumerable<MpqEntry>)_entries).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => _entries.GetEnumerator();
    }
}
