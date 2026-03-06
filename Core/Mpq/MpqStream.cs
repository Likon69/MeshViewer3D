//
// MpqStream.cs (consolidated from MpqLib)
//
// Authors:
//		Foole (fooleau@gmail.com)
//
// (C) 2006 Foole (fooleau@gmail.com)
// Based on code from StormLib by Ladislav Zezula and ShadowFlare
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.IO;
using System.IO.Compression;
using System.Collections;

namespace MpqLib
{
	// ─────────────────────────────────────────────────────────────────────────
	// 1. BitStream
	// ─────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// A utility class for reading groups of bits from a stream
	/// </summary>
	internal class BitStream
	{
        private Stream _baseStream;
        private int _current;
		private int _bitCount;

		public BitStream(Stream sourceStream)
		{
            _baseStream = sourceStream;
		}

		public int ReadBits(int bitCount)
		{
			if (bitCount > 16)
				throw new ArgumentOutOfRangeException("BitCount", "Maximum BitCount is 16");
			if (EnsureBits(bitCount) == false) return -1;
            int result = _current & (0xffff >> (16 - bitCount));
			WasteBits(bitCount);
			return result;
		}

		public int PeekByte()
		{
			if (EnsureBits(8) == false) return -1;
            return _current & 0xff;
		}

		public bool EnsureBits(int bitCount)
		{
			if (bitCount <= _bitCount) return true;

            if (_baseStream.Position >= _baseStream.Length) return false;
            int nextvalue = _baseStream.ReadByte();
            _current |= nextvalue << _bitCount;
			_bitCount += 8;
			return true;
		}

		private bool WasteBits(int bitCount)
		{
            _current >>= bitCount;
			_bitCount -= bitCount;
			return true;
		}
	}

	// ─────────────────────────────────────────────────────────────────────────
	// 2. LinkedNode
	// ─────────────────────────────────────────────────────────────────────────

	// A node which is both hierachcical (parent/child) and doubly linked (next/prev)
	internal class LinkedNode
	{
		public int DecompressedValue;
		public int Weight;
		public LinkedNode Parent;
		public LinkedNode Child0;

		public LinkedNode Child1
		{ get { return Child0.Prev; } }

		public LinkedNode Next;
		public LinkedNode Prev;

		public LinkedNode(int decompVal, int weight)
		{
			DecompressedValue = decompVal;
			this.Weight = weight;
		}

		// TODO: This would be more efficient as a member of the other class
		// ie avoid the recursion
		public LinkedNode Insert(LinkedNode other)
		{
			// 'Next' should have a lower weight
			// we should return the lower weight
			if (other.Weight <= Weight)
			{
				// insert before
				if (Next != null)
				{
					Next.Prev = other;
					other.Next = Next;
				}
				Next = other;
				other.Prev = this;
				return other;
			}
			else
			{
				if (Prev == null)
				{
					// Insert after
					other.Prev = null;
					Prev = other;
					other.Next = this;
				}
				else
				{
					Prev.Insert(other);
				}
			}
			return this;
		}
	}

	// ─────────────────────────────────────────────────────────────────────────
	// 3. MpqHuffman
	// ─────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// A decompressor for MPQ's huffman compression
	/// </summary>
	internal static class MpqHuffman
	{
		private static readonly byte[][] sPrime =
		{
			// Compression type 0
			new byte[]
			{
				0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02,
			},
			// Compression type 1
			new byte[]
			{
				0x54, 0x16, 0x16, 0x0D, 0x0C, 0x08, 0x06, 0x05, 0x06, 0x05, 0x06, 0x03, 0x04, 0x04, 0x03, 0x05,
				0x0E, 0x0B, 0x14, 0x13, 0x13, 0x09, 0x0B, 0x06, 0x05, 0x04, 0x03, 0x02, 0x03, 0x02, 0x02, 0x02,
				0x0D, 0x07, 0x09, 0x06, 0x06, 0x04, 0x03, 0x02, 0x04, 0x03, 0x03, 0x03, 0x03, 0x03, 0x02, 0x02,
				0x09, 0x06, 0x04, 0x04, 0x04, 0x04, 0x03, 0x02, 0x03, 0x02, 0x02, 0x02, 0x02, 0x03, 0x02, 0x04,
				0x08, 0x03, 0x04, 0x07, 0x09, 0x05, 0x03, 0x03, 0x03, 0x03, 0x02, 0x02, 0x02, 0x03, 0x02, 0x02,
				0x03, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x01, 0x01, 0x01, 0x02, 0x01, 0x02, 0x02,
				0x06, 0x0A, 0x08, 0x08, 0x06, 0x07, 0x04, 0x03, 0x04, 0x04, 0x02, 0x02, 0x04, 0x02, 0x03, 0x03,
				0x04, 0x03, 0x07, 0x07, 0x09, 0x06, 0x04, 0x03, 0x03, 0x02, 0x01, 0x02, 0x02, 0x02, 0x02, 0x02,
				0x0A, 0x02, 0x02, 0x03, 0x02, 0x02, 0x01, 0x01, 0x02, 0x02, 0x02, 0x06, 0x03, 0x05, 0x02, 0x03,
				0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x03, 0x01, 0x01, 0x01,
				0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x04, 0x04, 0x04, 0x07, 0x09, 0x08, 0x0C, 0x02,
				0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x01, 0x01, 0x03,
				0x04, 0x01, 0x02, 0x04, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x01, 0x01, 0x01,
				0x04, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
				0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x03, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
				0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x02, 0x01, 0x01, 0x02, 0x02, 0x02, 0x06, 0x4B,
			},
			// Compression type 2
			new byte[]
			{
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x27, 0x00, 0x00, 0x23, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0xFF, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x02, 0x01, 0x01, 0x06, 0x0E, 0x10, 0x04,
				0x06, 0x08, 0x05, 0x04, 0x04, 0x03, 0x03, 0x02, 0x02, 0x03, 0x03, 0x01, 0x01, 0x02, 0x01, 0x01,
				0x01, 0x04, 0x02, 0x04, 0x02, 0x02, 0x02, 0x01, 0x01, 0x04, 0x01, 0x01, 0x02, 0x03, 0x03, 0x02,
				0x03, 0x01, 0x03, 0x06, 0x04, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x01, 0x02, 0x01, 0x01,
				0x01, 0x29, 0x07, 0x16, 0x12, 0x40, 0x0A, 0x0A, 0x11, 0x25, 0x01, 0x03, 0x17, 0x10, 0x26, 0x2A,
				0x10, 0x01, 0x23, 0x23, 0x2F, 0x10, 0x06, 0x07, 0x02, 0x09, 0x01, 0x01, 0x01, 0x01, 0x01
			},
			// Compression type 3
			new byte[]
			{
				0xFF, 0x0B, 0x07, 0x05, 0x0B, 0x02, 0x02, 0x02, 0x06, 0x02, 0x02, 0x01, 0x04, 0x02, 0x01, 0x03,
				0x09, 0x01, 0x01, 0x01, 0x03, 0x04, 0x01, 0x01, 0x02, 0x01, 0x01, 0x01, 0x02, 0x01, 0x01, 0x01,
				0x05, 0x01, 0x01, 0x01, 0x0D, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
				0x02, 0x01, 0x01, 0x03, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x01, 0x01, 0x01, 0x01,
				0x0A, 0x04, 0x02, 0x01, 0x06, 0x03, 0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x03, 0x01, 0x01, 0x01,
				0x05, 0x02, 0x03, 0x04, 0x03, 0x03, 0x03, 0x02, 0x01, 0x01, 0x01, 0x02, 0x01, 0x02, 0x03, 0x03,
				0x01, 0x03, 0x01, 0x01, 0x02, 0x05, 0x01, 0x01, 0x04, 0x03, 0x05, 0x01, 0x03, 0x01, 0x03, 0x03,
				0x02, 0x01, 0x04, 0x03, 0x0A, 0x06, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
				0x02, 0x02, 0x01, 0x0A, 0x02, 0x05, 0x01, 0x01, 0x02, 0x07, 0x02, 0x17, 0x01, 0x05, 0x01, 0x01,
				0x0E, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
				0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
				0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
				0x06, 0x02, 0x01, 0x04, 0x05, 0x01, 0x01, 0x02, 0x01, 0x01, 0x01, 0x01, 0x02, 0x01, 0x01, 0x01,
				0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
				0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x07, 0x01, 0x01, 0x02, 0x01, 0x01, 0x01, 0x01,
				0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x11,
			},
			// Compression type 4
			new byte[]
			{
				0xFF, 0xFB, 0x98, 0x9A, 0x84, 0x85, 0x63, 0x64, 0x3E, 0x3E, 0x22, 0x22, 0x13, 0x13, 0x18, 0x17,
			},
			// Compression type 5
			new byte[]
			{
				0xFF, 0xF1, 0x9D, 0x9E, 0x9A, 0x9B, 0x9A, 0x97, 0x93, 0x93, 0x8C, 0x8E, 0x86, 0x88, 0x80, 0x82,
				0x7C, 0x7C, 0x72, 0x73, 0x69, 0x6B, 0x5F, 0x60, 0x55, 0x56, 0x4A, 0x4B, 0x40, 0x41, 0x37, 0x37,
				0x2F, 0x2F, 0x27, 0x27, 0x21, 0x21, 0x1B, 0x1C, 0x17, 0x17, 0x13, 0x13, 0x10, 0x10, 0x0D, 0x0D,
				0x0B, 0x0B, 0x09, 0x09, 0x08, 0x08, 0x07, 0x07, 0x06, 0x05, 0x05, 0x04, 0x04, 0x04, 0x19, 0x18
			},
			// Compression type 6
			new byte[]
			{
				0xC3, 0xCB, 0xF5, 0x41, 0xFF, 0x7B, 0xF7, 0x21, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0xBF, 0xCC, 0xF2, 0x40, 0xFD, 0x7C, 0xF7, 0x22, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x7A, 0x46
			},
			// Compression type 7
			new byte[]
			{
				0xC3, 0xD9, 0xEF, 0x3D, 0xF9, 0x7C, 0xE9, 0x1E, 0xFD, 0xAB, 0xF1, 0x2C, 0xFC, 0x5B, 0xFE, 0x17,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0xBD, 0xD9, 0xEC, 0x3D, 0xF5, 0x7D, 0xE8, 0x1D, 0xFB, 0xAE, 0xF0, 0x2C, 0xFB, 0x5C, 0xFF, 0x18,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x70, 0x6C
			},
			// Compression type 8
			new byte[]
			{
				0xBA, 0xC5, 0xDA, 0x33, 0xE3, 0x6D, 0xD8, 0x18, 0xE5, 0x94, 0xDA, 0x23, 0xDF, 0x4A, 0xD1, 0x10,
				0xEE, 0xAF, 0xE4, 0x2C, 0xEA, 0x5A, 0xDE, 0x15, 0xF4, 0x87, 0xE9, 0x21, 0xF6, 0x43, 0xFC, 0x12,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0xB0, 0xC7, 0xD8, 0x33, 0xE3, 0x6B, 0xD6, 0x18, 0xE7, 0x95, 0xD8, 0x23, 0xDB, 0x49, 0xD0, 0x11,
				0xE9, 0xB2, 0xE2, 0x2B, 0xE8, 0x5C, 0xDD, 0x15, 0xF1, 0x87, 0xE7, 0x20, 0xF7, 0x44, 0xFF, 0x13,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x5F, 0x9E
			}
		};

		public static MemoryStream Decompress(Stream data)
		{
			int comptype = data.ReadByte();

			if (comptype == 0)
				throw new NotImplementedException("Compression type 0 is not currently supported");

			LinkedNode tail = BuildList(sPrime[comptype]);
			LinkedNode head = BuildTree(tail);

			MemoryStream outputstream = new MemoryStream();
			BitStream bitstream = new BitStream(data);
			int decoded;
			do
			{
				LinkedNode node = Decode(bitstream, head);
				decoded = node.DecompressedValue;
				switch (decoded)
				{
					case 256:
						break;
					case 257:
						int newvalue = bitstream.ReadBits(8);
						outputstream.WriteByte((byte)newvalue);
						tail = InsertNode(tail, newvalue);
						break;
					default:
						outputstream.WriteByte((byte)decoded);
						break;
				}
			} while (decoded != 256);

            outputstream.Seek(0, SeekOrigin.Begin);
			return outputstream;
		}

		private static LinkedNode Decode(BitStream input, LinkedNode head)
		{
			LinkedNode node = head;

			while (node.Child0 != null)
			{
				int bit = input.ReadBits(1);
				if (bit == -1)
					throw new Exception("Unexpected end of file");

				node = bit == 0 ? node.Child0 : node.Child1;
			}
			return node;
		}

		private static LinkedNode BuildList(byte[] primeData)
		{
			LinkedNode root;

			root = new LinkedNode(256, 1);
			root = root.Insert(new LinkedNode(257, 1));

			for (int i = 0; i < primeData.Length; i++)
			{
				if (primeData[i] != 0)
					root = root.Insert(new LinkedNode(i, primeData[i]));
			}
			return root;
		}

		private static LinkedNode BuildTree(LinkedNode tail)
		{
			LinkedNode current = tail;

			while (current != null)
			{
				LinkedNode child0 = current;
				LinkedNode child1 = current.Prev;
				if (child1 == null) break;

				LinkedNode parent = new LinkedNode(0, child0.Weight + child1.Weight);
				parent.Child0 = child0;
				child0.Parent = parent;
				child1.Parent = parent;

				current.Insert(parent);
				current = current.Prev.Prev;
			}
			return current;
		}

		private static LinkedNode InsertNode(LinkedNode tail, int decomp)
		{
			LinkedNode parent = tail;
			LinkedNode result = tail.Prev; // This will be the new tail after the tree is updated

			LinkedNode temp = new LinkedNode(parent.DecompressedValue, parent.Weight);
			temp.Parent = parent;

			LinkedNode newnode = new LinkedNode(decomp, 0);
			newnode.Parent = parent;

			parent.Child0 = newnode;

			tail.Next = temp;
			temp.Prev = tail;
			newnode.Prev = temp;
			temp.Next = newnode;

			AdjustTree(newnode);
			// TODO: For compression type 0, AdjustTree should be called
			// once for every value written and only once here
			AdjustTree(newnode);
			return result;
		}

		// This increases the weight of the new node and its antecendants
		// and adjusts the tree if needed
		private static void AdjustTree(LinkedNode newNode)
		{
			LinkedNode current = newNode;

			while (current != null)
			{
				current.Weight++;
				LinkedNode insertpoint;
				LinkedNode prev;
				// Go backwards thru the list looking for the insertion point
				insertpoint = current;
				while (true)
				{
					prev = insertpoint.Prev;
					if (prev == null) break;
					if (prev.Weight >= current.Weight) break;
					insertpoint = prev;
				}

				// No insertion point found
				if (insertpoint == current)
				{
					current = current.Parent;
					continue;
				}

				// The following code basicly swaps insertpoint with current

				// remove insert point
				if (insertpoint.Prev != null) insertpoint.Prev.Next = insertpoint.Next;
				insertpoint.Next.Prev = insertpoint.Prev;

				// Insert insertpoint after current
				insertpoint.Next = current.Next;
				insertpoint.Prev = current;
				if (current.Next != null) current.Next.Prev = insertpoint;
				current.Next = insertpoint;

				// remove current
				current.Prev.Next = current.Next;
				current.Next.Prev = current.Prev;

				// insert current after prev
				LinkedNode temp = prev.Next;
				current.Next = temp;
				current.Prev = prev;
				temp.Prev = current;
				prev.Next = current;

				// Set up parent/child links
				LinkedNode currentparent = current.Parent;
				LinkedNode insertparent = insertpoint.Parent;

				if (currentparent.Child0 == current)
					currentparent.Child0 = insertpoint;

				if (currentparent != insertparent && insertparent.Child0 == insertpoint)
					insertparent.Child0 = current;

				current.Parent = insertparent;
				insertpoint.Parent = currentparent;

				current = current.Parent;
			}
		}
	}

	// ─────────────────────────────────────────────────────────────────────────
	// 4. MpqWavCompression
	// ─────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// An IMA ADPCM decompress for Mpq files
	/// </summary>
	internal static class MpqWavCompression
	{
		private static readonly int[] sLookup =
		{
			0x0007, 0x0008, 0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x000E,
			0x0010, 0x0011, 0x0013, 0x0015, 0x0017, 0x0019, 0x001C, 0x001F,
			0x0022, 0x0025, 0x0029, 0x002D, 0x0032, 0x0037, 0x003C, 0x0042,
			0x0049, 0x0050, 0x0058, 0x0061, 0x006B, 0x0076, 0x0082, 0x008F,
			0x009D, 0x00AD, 0x00BE, 0x00D1, 0x00E6, 0x00FD, 0x0117, 0x0133,
			0x0151, 0x0173, 0x0198, 0x01C1, 0x01EE, 0x0220, 0x0256, 0x0292,
			0x02D4, 0x031C, 0x036C, 0x03C3, 0x0424, 0x048E, 0x0502, 0x0583,
			0x0610, 0x06AB, 0x0756, 0x0812, 0x08E0, 0x09C3, 0x0ABD, 0x0BD0,
			0x0CFF, 0x0E4C, 0x0FBA, 0x114C, 0x1307, 0x14EE, 0x1706, 0x1954,
			0x1BDC, 0x1EA5, 0x21B6, 0x2515, 0x28CA, 0x2CDF, 0x315B, 0x364B,
			0x3BB9, 0x41B2, 0x4844, 0x4F7E, 0x5771, 0x602F, 0x69CE, 0x7462,
			0x7FFF
		};

		private static readonly int[] sLookup2 =
		{
		    -1, 0, -1, 4, -1, 2, -1, 6,
		    -1, 1, -1, 5, -1, 3, -1, 7,
		    -1, 1, -1, 5, -1, 3, -1, 7,
		    -1, 2, -1, 4, -1, 6, -1, 8
		};

		public static byte[] Decompress(Stream data, int channelCount)
		{
			int[] Array1 = new int[] { 0x2c, 0x2c };
			int[] Array2 = new int[channelCount];

			BinaryReader input = new BinaryReader(data);
			MemoryStream outputstream = new MemoryStream();
			BinaryWriter output = new BinaryWriter(outputstream);

			input.ReadByte();
			byte shift = input.ReadByte();

			for (int i = 0; i < channelCount; i++)
			{
				short temp = input.ReadInt16();
				Array2[i] = temp;
				output.Write(temp);
			}

			int channel = channelCount - 1;
			while (data.Position < data.Length)
			{
				byte value = input.ReadByte();

				if (channelCount == 2) channel = 1 - channel;

				if ((value & 0x80) != 0)
				{
					switch (value & 0x7f)
					{
						case 0:
							if (Array1[channel] != 0) Array1[channel]--;
							output.Write((short)Array2[channel]);
							break;
						case 1:
							Array1[channel] += 8;
							if (Array1[channel] > 0x58) Array1[channel] = 0x58;
							if (channelCount == 2) channel = 1 - channel;
							break;
						case 2:
							break;
						default:
							Array1[channel] -= 8;
							if (Array1[channel] < 0) Array1[channel] = 0;
							if (channelCount == 2) channel = 1 - channel;
							break;
					}
				}
				else
				{
					int temp1 = sLookup[Array1[channel]];
					int temp2 = temp1 >> shift;

					if ((value & 1) != 0)
						temp2 += (temp1 >> 0);
					if ((value & 2) != 0)
						temp2 += (temp1 >> 1);
					if ((value & 4) != 0)
						temp2 += (temp1 >> 2);
					if ((value & 8) != 0)
						temp2 += (temp1 >> 3);
					if ((value & 0x10) != 0)
						temp2 += (temp1 >> 4);
					if ((value & 0x20) != 0)
						temp2 += (temp1 >> 5);

					int temp3 = Array2[channel];
					if ((value & 0x40) != 0)
					{
						temp3 -= temp2;
						if (temp3 <= short.MinValue) temp3 = short.MinValue;
					}
					else
					{
						temp3 += temp2;
						if (temp3 >= short.MaxValue) temp3 = short.MaxValue;
					}
					Array2[channel] = temp3;
					output.Write((short)temp3);

					Array1[channel] += sLookup2[value & 0x1f];

					if (Array1[channel] < 0)
						Array1[channel] = 0;
					else
						if (Array1[channel] > 0x58) Array1[channel] = 0x58;
				}
			}
			return outputstream.ToArray();
		}
	}

	// ─────────────────────────────────────────────────────────────────────────
	// 5. CompressionType enum
	// ─────────────────────────────────────────────────────────────────────────

	internal enum CompressionType
	{
		Binary = 0,
		Ascii = 1
	}

	// ─────────────────────────────────────────────────────────────────────────
	// 6. PKLibDecompress
	// ─────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// A decompressor for PKLib implode/explode
	/// </summary>
	public class PKLibDecompress
	{
		private BitStream _bitstream;
		private CompressionType _compressionType;
		private int _dictSizeBits;	// Dictionary size in bits

		private static byte[] sPosition1;
		private static byte[] sPosition2;

		private static readonly byte[] sLenBits =
		{
			3, 2, 3, 3, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 7, 7
		};

		private static readonly byte[] sLenCode =
		{
			5, 3, 1, 6, 10, 2, 12, 20, 4, 24, 8, 48, 16, 32, 64, 0
		};

		private static readonly byte[] sExLenBits =
		{
			0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8
		};

		private static readonly UInt16[] sLenBase =
		{
			0x0000, 0x0001, 0x0002, 0x0003, 0x0004, 0x0005, 0x0006, 0x0007,
			0x0008, 0x000A, 0x000E, 0x0016, 0x0026, 0x0046, 0x0086, 0x0106
		};

		private static readonly byte[] sDistBits =
		{
			2, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6,
			6, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
			7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
			8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8
		};

		private static readonly byte[] sDistCode =
		{
		    0x03, 0x0D, 0x05, 0x19, 0x09, 0x11, 0x01, 0x3E, 0x1E, 0x2E, 0x0E, 0x36, 0x16, 0x26, 0x06, 0x3A,
		    0x1A, 0x2A, 0x0A, 0x32, 0x12, 0x22, 0x42, 0x02, 0x7C, 0x3C, 0x5C, 0x1C, 0x6C, 0x2C, 0x4C, 0x0C,
		    0x74, 0x34, 0x54, 0x14, 0x64, 0x24, 0x44, 0x04, 0x78, 0x38, 0x58, 0x18, 0x68, 0x28, 0x48, 0x08,
		    0xF0, 0x70, 0xB0, 0x30, 0xD0, 0x50, 0x90, 0x10, 0xE0, 0x60, 0xA0, 0x20, 0xC0, 0x40, 0x80, 0x00
		};

		static PKLibDecompress()
		{
			sPosition1 = GenerateDecodeTable(sDistBits, sDistCode);
			sPosition2 = GenerateDecodeTable(sLenBits, sLenCode);
		}

		public PKLibDecompress(Stream input)
		{
			_bitstream = new BitStream(input);

			_compressionType = (CompressionType)input.ReadByte();
			if (_compressionType != CompressionType.Binary && _compressionType != CompressionType.Ascii)
				throw new InvalidDataException("Invalid compression type: " + _compressionType);

			_dictSizeBits = input.ReadByte();
			// This is 6 in test cases
			if (4 > _dictSizeBits || _dictSizeBits > 6)
                throw new InvalidDataException("Invalid dictionary size: " + _dictSizeBits);
		}

		public byte[] Explode(int expectedSize)
		{
			byte[] outputbuffer = new byte[expectedSize];
			Stream outputstream = new MemoryStream(outputbuffer);

			int instruction;
			while ((instruction = DecodeLit()) != -1)
			{
				if (instruction < 0x100)
				{
					outputstream.WriteByte((byte)instruction);
				}
				else
				{
					// If instruction is greater than 0x100, it means "Repeat n - 0xFE bytes"
					int copylength = instruction - 0xFE;
					int moveback = DecodeDist(copylength);
					if (moveback == 0) break;

					int source = (int)outputstream.Position - moveback;
					// We can't just outputstream.Write the section of the array
					// because it might overlap with what is currently being written
					while (copylength-- > 0)
						outputstream.WriteByte(outputbuffer[source++]);
				}
			}

			if (outputstream.Position == expectedSize)
			{
				return outputbuffer;
			}
			else
			{
				// Resize the array
				byte[] result = new byte[outputstream.Position];
				Array.Copy(outputbuffer, 0, result, 0, result.Length);
				return result;
			}
		}

		// Return values:
		// 0x000 - 0x0FF : One byte from compressed file.
		// 0x100 - 0x305 : Copy previous block (0x100 = 1 byte)
		// -1            : EOF
		private int DecodeLit()
		{
			switch (_bitstream.ReadBits(1))
			{
				case -1:
					return -1;

				case 1:
					// The next bits are position in buffers
					int pos = sPosition2[_bitstream.PeekByte()];

					// Skip the bits we just used
					if (_bitstream.ReadBits(sLenBits[pos]) == -1) return -1;

					int nbits = sExLenBits[pos];
					if (nbits != 0)
					{
						// TODO: Verify this conversion
						int val2 = _bitstream.ReadBits(nbits);
						if (val2 == -1 && (pos + val2 != 0x10e)) return -1;

						pos = sLenBase[pos] + val2;
					}
					return pos + 0x100; // Return number of bytes to repeat

				case 0:
					if (_compressionType == CompressionType.Binary)
						return _bitstream.ReadBits(8);

					// TODO: Text mode
					throw new NotImplementedException("Text mode is not yet implemented");
				default:
					return 0;
			}
		}

		private int DecodeDist(int length)
		{
			if (_bitstream.EnsureBits(8) == false) return 0;
			int pos = sPosition1[_bitstream.PeekByte()];
			byte skip = sDistBits[pos];     // Number of bits to skip

			// Skip the appropriate number of bits
			if (_bitstream.ReadBits(skip) == -1) return 0;

			if (length == 2)
			{
				if (_bitstream.EnsureBits(2) == false) return 0;
				pos = (pos << 2) | _bitstream.ReadBits(2);
			}
			else
			{
				if (_bitstream.EnsureBits(_dictSizeBits) == false) return 0;
				pos = ((pos << _dictSizeBits)) | _bitstream.ReadBits(_dictSizeBits);
			}

			return pos + 1;
		}

		private static byte[] GenerateDecodeTable(byte[] bits, byte[] codes)
		{
			byte[] result = new byte[256];

			for (int i = bits.Length - 1; i >= 0; i--)
			{
				UInt32 idx1 = codes[i];
				UInt32 idx2 = (UInt32)1 << bits[i];

				do
				{
					result[idx1] = (byte)i;
					idx1         += idx2;
				} while (idx1 < 0x100);
			}
			return result;
		}
	}

	// ─────────────────────────────────────────────────────────────────────────
	// 7. MpqStream
	// ─────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// A Stream based class for reading a file from an MPQ file
	/// </summary>
	public class MpqStream : Stream
	{
		private Stream _stream;
		private int _blockSize;

		private MpqEntry _entry;
		private uint[] _blockPositions;

		private long _position;
		private byte[] _currentData;
		private int _currentBlockIndex = -1;

		internal MpqStream(MpqArchive archive, MpqEntry entry)
		{
			_entry = entry;

			_stream = archive.BaseStream;
			_blockSize = archive.BlockSize;

			if (_entry.IsCompressed && !_entry.IsSingleUnit)
				LoadBlockPositions();
		}

		// Compressed files start with an array of offsets to make seeking possible
		private void LoadBlockPositions()
		{
			int blockposcount = (int)((_entry.FileSize + _blockSize - 1) / _blockSize) + 1;
			// Files with metadata have an extra block containing block checksums
			if ((_entry.Flags & MpqFileFlags.FileHasMetadata) != 0)
				blockposcount++;

			_blockPositions = new uint[blockposcount];

			lock (_stream)
			{
				_stream.Seek(_entry.FilePos, SeekOrigin.Begin);
				BinaryReader br = new BinaryReader(_stream);
				for (int i = 0; i < blockposcount; i++)
					_blockPositions[i] = br.ReadUInt32();
			}

			uint blockpossize = (uint) blockposcount * 4;

			if (_entry.IsEncrypted)
			{
				if (_entry.EncryptionSeed == 0)  // This should only happen when the file name is not known
				{
					_entry.EncryptionSeed = MpqArchive.DetectFileSeed(_blockPositions[0], _blockPositions[1], blockpossize) + 1;
					if (_entry.EncryptionSeed == 1)
						throw new MpqParserException("Unable to determine encyption seed");
				}

				MpqArchive.DecryptBlock(_blockPositions, _entry.EncryptionSeed - 1);

				if (_blockPositions[0] != blockpossize)
					throw new MpqParserException("Decryption failed");
				if (_blockPositions[1] > _blockSize + blockpossize)
					throw new MpqParserException("Decryption failed");
			}
		}

		private byte[] LoadBlock(int blockIndex, int expectedLength)
		{
			uint offset;
			int toread;
			uint encryptionseed;

			if (_entry.IsCompressed)
			{
				offset = _blockPositions[blockIndex];
				toread = (int)(_blockPositions[blockIndex + 1] - offset);
			}
			else
			{
				offset = (uint)(blockIndex * _blockSize);
				toread = expectedLength;
			}
			offset += _entry.FilePos;

			byte[] data = new byte[toread];
			lock (_stream)
			{
				_stream.Seek(offset, SeekOrigin.Begin);
				int read = _stream.Read(data, 0, toread);
				if (read != toread)
					throw new MpqParserException("Insufficient data or invalid data length");
			}

			if (_entry.IsEncrypted && _entry.FileSize > 3)
			{
				if (_entry.EncryptionSeed == 0)
					throw new MpqParserException("Unable to determine encryption key");

				encryptionseed = (uint)(blockIndex + _entry.EncryptionSeed);
				MpqArchive.DecryptBlock(data, encryptionseed);
			}

			if (_entry.IsCompressed && (toread != expectedLength))
			{
				if ((_entry.Flags & MpqFileFlags.CompressedMulti) != 0)
					data = DecompressMulti(data, expectedLength);
				else
					data = PKDecompress(new MemoryStream(data), expectedLength);
			}

			return data;
		}

		#region Stream overrides
		public override bool CanRead
		{ get { return true; } }

		public override bool CanSeek
		{ get { return true; } }

		public override bool CanWrite
		{ get { return false; } }

		public override long Length
		{ get { return _entry.FileSize; } }

		public override long Position
		{
			get
			{
				return _position;
			}
			set
			{
				Seek(value, SeekOrigin.Begin);
			}
		}

		public override void Flush()
		{
			// NOP
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			long target;

			switch (origin)
			{
				case SeekOrigin.Begin:
					target = offset;
					break;
				case SeekOrigin.Current:
					target = Position + offset;
					break;
				case SeekOrigin.End:
					target = Length + offset;
					break;
				default:
					throw new ArgumentException("Origin", "Invalid SeekOrigin");
			}

			if (target < 0)
				throw new ArgumentOutOfRangeException("Attmpted to Seek before the beginning of the stream");
			if (target >= Length)
				throw new ArgumentOutOfRangeException("Attmpted to Seek beyond the end of the stream");

			_position = target;

			return _position;
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException("SetLength is not supported");
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_entry.IsSingleUnit)
				return ReadInternalSingleUnit(buffer, offset, count);

			int toread = count;
			int readtotal = 0;

			while (toread > 0)
			{
				int read = ReadInternal(buffer, offset, toread);
				if (read == 0) break;
				readtotal += read;
				offset += read;
				toread -= read;
			}
			return readtotal;
		}

		// SingleUnit entries can be compressed but are never encrypted
		private int ReadInternalSingleUnit(byte[] buffer, int offset, int count)
		{
			if (_position >= Length)
				return 0;

			if (_currentData == null)
				LoadSingleUnit();

			int bytestocopy = Math.Min((int)(_currentData.Length - _position), count);

			Array.Copy(_currentData, _position, buffer, offset, bytestocopy);

			_position += bytestocopy;
			return bytestocopy;
		}

		private void LoadSingleUnit()
		{
			// Read the entire file into memory
			byte[] filedata = new byte[_entry.CompressedSize];
			lock (_stream)
			{
				_stream.Seek(_entry.FilePos, SeekOrigin.Begin);
				int read = _stream.Read(filedata, 0, filedata.Length);
				if (read != filedata.Length)
					throw new MpqParserException("Insufficient data or invalid data length");
			}

			if (_entry.CompressedSize == _entry.FileSize)
				_currentData = filedata;
			else
				_currentData = DecompressMulti(filedata, (int)_entry.FileSize);
		}

		private int ReadInternal(byte[] buffer, int offset, int count)
		{
			// OW: avoid reading past the contents of the file
			if (_position >= Length)
				return 0;

			BufferData();

			int localposition = (int)(_position % _blockSize);
			int bytestocopy = Math.Min(_currentData.Length - localposition, count);
			if (bytestocopy <= 0) return 0;

			Array.Copy(_currentData, localposition, buffer, offset, bytestocopy);

			_position += bytestocopy;
			return bytestocopy;
		}

		public override int ReadByte()
		{
			if (_position >= Length) return -1;

			if (_entry.IsSingleUnit)
				return ReadByteSingleUnit();

			BufferData();

			int localposition = (int)(_position % _blockSize);
			_position++;
			return _currentData[localposition];
		}

		private int ReadByteSingleUnit()
		{
			if (_currentData == null)
				LoadSingleUnit();

			return _currentData[_position++];
		}

		private void BufferData()
		{
			int requiredblock = (int)(_position / _blockSize);
			if (requiredblock != _currentBlockIndex)
			{
				int expectedlength = (int)Math.Min(Length - (requiredblock * _blockSize), _blockSize);
				_currentData = LoadBlock(requiredblock, expectedlength);
				_currentBlockIndex = requiredblock;
			}
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException("Writing is not supported");
		}
		#endregion Stream overrides

		/* Compression types in order:
		 *  10 = BZip2
		 *   8 = PKLib
		 *   2 = ZLib
		 *   1 = Huffman
		 *  80 = IMA ADPCM Stereo
		 *  40 = IMA ADPCM Mono
		 */
		private static byte[] DecompressMulti(byte[] input, int outputLength)
		{
			Stream sinput = new MemoryStream(input);

			byte comptype = (byte)sinput.ReadByte();

			// WC3 onward mostly use Zlib
			// Starcraft 1 mostly uses PKLib, plus types 41 and 81 for audio files
			switch (comptype)
			{
				case 1: // Huffman
					return MpqHuffman.Decompress(sinput).ToArray();
				case 2: // ZLib/Deflate
					return ZlibDecompress(sinput, outputLength);
				case 8: // PKLib/Impode
					return PKDecompress(sinput, outputLength);
				case 0x10: // BZip2
					throw new MpqParserException("BZip2 compression is not supported");
				case 0x80: // IMA ADPCM Stereo
					return MpqWavCompression.Decompress(sinput, 2);
				case 0x40: // IMA ADPCM Mono
					return MpqWavCompression.Decompress(sinput, 1);

				case 0x12:
					// TODO: LZMA
					throw new MpqParserException("LZMA compression is not yet supported");

				// Combos
				case 0x22:
					// TODO: sparse then zlib
					throw new MpqParserException("Sparse compression + Deflate compression is not yet supported");
				case 0x30:
					// TODO: sparse then bzip2
					throw new MpqParserException("Sparse compression + BZip2 compression is not yet supported");
				case 0x41:
					sinput = MpqHuffman.Decompress(sinput);
					return MpqWavCompression.Decompress(sinput, 1);
				case 0x48:
					{
						byte[] result = PKDecompress(sinput, outputLength);
						return MpqWavCompression.Decompress(new MemoryStream(result), 1);
					}
				case 0x81:
					sinput = MpqHuffman.Decompress(sinput);
					return MpqWavCompression.Decompress(sinput, 2);
				case 0x88:
					{
						byte[] result = PKDecompress(sinput, outputLength);
						return MpqWavCompression.Decompress(new MemoryStream(result), 2);
					}
				default:
					throw new MpqParserException("Compression is not yet supported: 0x" + comptype.ToString("X"));
			}
		}

		private static byte[] PKDecompress(Stream data, int expectedLength)
		{
			PKLibDecompress pk = new PKLibDecompress(data);
			return pk.Explode(expectedLength);
		}

		private static byte[] ZlibDecompress(Stream data, int expectedLength)
		{
			byte[] output = new byte[expectedLength];
			using var s = new ZLibStream(data, CompressionMode.Decompress, leaveOpen: true);
			int offset = 0, rem = expectedLength;
			while (rem > 0) { int n = s.Read(output, offset, rem); if (n == 0) break; offset += n; rem -= n; }
			return output;
		}
	}
}
