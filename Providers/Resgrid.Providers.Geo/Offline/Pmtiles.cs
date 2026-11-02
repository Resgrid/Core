using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Resgrid.Providers.GeoLocationProvider.Offline
{
	/// <summary>The fixed 127-byte PMTiles v3 header.</summary>
	public sealed class PmtilesHeader
	{
		public const int Length = 127;

		public ulong RootOffset { get; set; }
		public ulong RootLength { get; set; }
		public ulong MetadataOffset { get; set; }
		public ulong MetadataLength { get; set; }
		public ulong LeafDirectoryOffset { get; set; }
		public ulong LeafDirectoryLength { get; set; }
		public ulong TileDataOffset { get; set; }
		public ulong TileDataLength { get; set; }
		public ulong AddressedTilesCount { get; set; }
		public ulong TileEntriesCount { get; set; }
		public ulong TileContentsCount { get; set; }
		public bool Clustered { get; set; }
		/// <summary>1 none, 2 gzip, 3 brotli, 4 zstd.</summary>
		public byte InternalCompression { get; set; }
		public byte TileCompression { get; set; }
		/// <summary>1 mvt, 2 png, 3 jpeg, 4 webp, 5 avif.</summary>
		public byte TileType { get; set; }
		public byte MinZoom { get; set; }
		public byte MaxZoom { get; set; }
		public int MinLonE7 { get; set; }
		public int MinLatE7 { get; set; }
		public int MaxLonE7 { get; set; }
		public int MaxLatE7 { get; set; }
		public byte CenterZoom { get; set; }
		public int CenterLonE7 { get; set; }
		public int CenterLatE7 { get; set; }

		public static PmtilesHeader Parse(byte[] bytes)
		{
			if (bytes == null || bytes.Length < Length || Encoding.ASCII.GetString(bytes, 0, 7) != "PMTiles")
				throw new InvalidDataException("Not a PMTiles archive");
			if (bytes[7] != 3)
				throw new InvalidDataException($"PMTiles version {bytes[7]} is not supported (3 is)");

			var span = bytes.AsSpan();
			return new PmtilesHeader
			{
				RootOffset = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(8)),
				RootLength = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(16)),
				MetadataOffset = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(24)),
				MetadataLength = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(32)),
				LeafDirectoryOffset = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(40)),
				LeafDirectoryLength = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(48)),
				TileDataOffset = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(56)),
				TileDataLength = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(64)),
				AddressedTilesCount = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(72)),
				TileEntriesCount = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(80)),
				TileContentsCount = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(88)),
				Clustered = bytes[96] == 1,
				InternalCompression = bytes[97],
				TileCompression = bytes[98],
				TileType = bytes[99],
				MinZoom = bytes[100],
				MaxZoom = bytes[101],
				MinLonE7 = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(102)),
				MinLatE7 = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(106)),
				MaxLonE7 = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(110)),
				MaxLatE7 = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(114)),
				CenterZoom = bytes[118],
				CenterLonE7 = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(119)),
				CenterLatE7 = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(123))
			};
		}

		public byte[] ToBytes()
		{
			var bytes = new byte[Length];
			Encoding.ASCII.GetBytes("PMTiles").CopyTo(bytes, 0);
			bytes[7] = 3;
			var span = bytes.AsSpan();
			BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(8), RootOffset);
			BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(16), RootLength);
			BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(24), MetadataOffset);
			BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(32), MetadataLength);
			BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(40), LeafDirectoryOffset);
			BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(48), LeafDirectoryLength);
			BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(56), TileDataOffset);
			BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(64), TileDataLength);
			BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(72), AddressedTilesCount);
			BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(80), TileEntriesCount);
			BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(88), TileContentsCount);
			bytes[96] = (byte)(Clustered ? 1 : 0);
			bytes[97] = InternalCompression;
			bytes[98] = TileCompression;
			bytes[99] = TileType;
			bytes[100] = MinZoom;
			bytes[101] = MaxZoom;
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(102), MinLonE7);
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(106), MinLatE7);
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(110), MaxLonE7);
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(114), MaxLatE7);
			bytes[118] = CenterZoom;
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(119), CenterLonE7);
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(123), CenterLatE7);
			return bytes;
		}
	}

	/// <summary>
	/// A directory entry: RunLength consecutive tile ids from TileId share the bytes at Offset; RunLength 0 points at a leaf
	/// directory instead (Offset/Length within the leaf section).
	/// </summary>
	public struct PmtilesEntry
	{
		public ulong TileId;
		public ulong Offset;
		public uint Length;
		public uint RunLength;

		public PmtilesEntry(ulong tileId, ulong offset, uint length, uint runLength)
		{
			TileId = tileId;
			Offset = offset;
			Length = length;
			RunLength = runLength;
		}
	}

	/// <summary>An inclusive range of tile ids.</summary>
	public readonly struct TileIdRange
	{
		public TileIdRange(ulong first, ulong last)
		{
			First = first;
			Last = last;
		}

		public ulong First { get; }

		public ulong Last { get; }
	}

	/// <summary>
	/// PMTiles v3 primitives (https://github.com/protomaps/PMTiles/blob/main/spec/v3/spec.md): Hilbert tile ids, directory
	/// (de)serialization, internal compression. Tile ids number every tile of every zoom along a Hilbert curve, so the
	/// descendants of any tile at a deeper zoom form one contiguous id range — which is how a region is cut cheaply.
	/// </summary>
	public static class Pmtiles
	{
		public const byte CompressionNone = 1;
		public const byte CompressionGzip = 2;

		/// <summary>Number of tiles in all zooms below <paramref name="z"/>: (4^z - 1) / 3.</summary>
		public static ulong ZoomBase(int z) => ((1UL << (2 * z)) - 1) / 3;

		/// <summary>The tile's position on the Hilbert curve of its zoom (no zoom base).</summary>
		public static ulong HilbertIndex(int z, ulong x, ulong y)
		{
			if (z == 0)
				return 0;

			unchecked
			{
				ulong n = 1UL << z, d = 0, tx = x, ty = y;
				for (var s = n / 2; s > 0; s /= 2)
				{
					var rx = (tx & s) > 0 ? 1UL : 0UL;
					var ry = (ty & s) > 0 ? 1UL : 0UL;
					d += s * s * ((3 * rx) ^ ry);
					Rotate(s, ref tx, ref ty, rx, ry);
				}

				return d;
			}
		}

		public static ulong ZxyToTileId(int z, ulong x, ulong y)
		{
			if (z < 0 || z > 31)
				throw new ArgumentOutOfRangeException(nameof(z));

			var n = 1UL << z;
			if (x >= n || y >= n)
				throw new ArgumentOutOfRangeException(nameof(x), "Tile outside its zoom");

			return ZoomBase(z) + HilbertIndex(z, x, y);
		}

		public static (int Z, ulong X, ulong Y) TileIdToZxy(ulong id)
		{
			ulong acc = 0;
			for (var z = 0; z < 32; z++)
			{
				var count = 1UL << (2 * z);
				if (acc + count > id)
					return (z, PositionX(z, id - acc), PositionY(z, id - acc));

				acc += count;
			}

			throw new ArgumentOutOfRangeException(nameof(id));
		}

		private static ulong PositionX(int z, ulong position) => Position(z, position).X;

		private static ulong PositionY(int z, ulong position) => Position(z, position).Y;

		private static (ulong X, ulong Y) Position(int z, ulong position)
		{
			unchecked
			{
				ulong n = 1UL << z, t = position, tx = 0, ty = 0;
				for (ulong s = 1; s < n; s *= 2)
				{
					var rx = 1 & (t / 2);
					var ry = 1 & (t ^ rx);
					Rotate(s, ref tx, ref ty, rx, ry);
					tx += s * rx;
					ty += s * ry;
					t /= 4;
				}

				return (tx, ty);
			}
		}

		private static void Rotate(ulong n, ref ulong x, ref ulong y, ulong rx, ulong ry)
		{
			unchecked
			{
				if (ry != 0)
					return;

				if (rx == 1)
				{
					x = n - 1 - x;
					y = n - 1 - y;
				}

				(x, y) = (y, x);
			}
		}

		/// <summary>
		/// The id ranges of a tile and everything under it down to <paramref name="maxZoom"/>, plus its ancestors (so a pack
		/// for one cell still draws when zoomed out). One range per zoom.
		/// </summary>
		public static List<TileIdRange> CellRanges(int cellZoom, ulong cellX, ulong cellY, int minZoom, int maxZoom)
		{
			var ranges = new List<TileIdRange>();
			var cellIndex = HilbertIndex(cellZoom, cellX, cellY);

			for (var z = Math.Max(0, minZoom); z <= maxZoom; z++)
			{
				if (z < cellZoom)
				{
					var id = ZxyToTileId(z, cellX >> (cellZoom - z), cellY >> (cellZoom - z));
					ranges.Add(new TileIdRange(id, id));
				}
				else
				{
					var k = z - cellZoom;
					var first = ZoomBase(z) + (cellIndex << (2 * k));
					ranges.Add(new TileIdRange(first, first + (1UL << (2 * k)) - 1));
				}
			}

			return ranges;
		}

		public static List<PmtilesEntry> DeserializeDirectory(byte[] data)
		{
			var position = 0;
			var count = (int)ReadVarint(data, ref position);
			var entries = new PmtilesEntry[count];

			ulong lastId = 0;
			for (var i = 0; i < count; i++)
			{
				lastId += ReadVarint(data, ref position);
				entries[i].TileId = lastId;
			}

			for (var i = 0; i < count; i++)
				entries[i].RunLength = (uint)ReadVarint(data, ref position);

			for (var i = 0; i < count; i++)
				entries[i].Length = (uint)ReadVarint(data, ref position);

			for (var i = 0; i < count; i++)
			{
				var value = ReadVarint(data, ref position);
				entries[i].Offset = value == 0 && i > 0 ? entries[i - 1].Offset + entries[i - 1].Length : value - 1;
			}

			return entries.ToList();
		}

		public static byte[] SerializeDirectory(IReadOnlyList<PmtilesEntry> entries)
		{
			using var stream = new MemoryStream();
			WriteVarint(stream, (ulong)entries.Count);

			ulong lastId = 0;
			foreach (var entry in entries)
			{
				WriteVarint(stream, entry.TileId - lastId);
				lastId = entry.TileId;
			}

			foreach (var entry in entries)
				WriteVarint(stream, entry.RunLength);

			foreach (var entry in entries)
				WriteVarint(stream, entry.Length);

			for (var i = 0; i < entries.Count; i++)
			{
				if (i > 0 && entries[i].Offset == entries[i - 1].Offset + entries[i - 1].Length)
					WriteVarint(stream, 0);
				else
					WriteVarint(stream, entries[i].Offset + 1);
			}

			return stream.ToArray();
		}

		public static byte[] Decompress(byte[] data, byte compression)
		{
			switch (compression)
			{
				case 0:
				case CompressionNone:
					return data;
				case CompressionGzip:
					using (var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
					using (var output = new MemoryStream())
					{
						input.CopyTo(output);
						return output.ToArray();
					}
				case 3:
					using (var input = new BrotliStream(new MemoryStream(data), CompressionMode.Decompress))
					using (var output = new MemoryStream())
					{
						input.CopyTo(output);
						return output.ToArray();
					}
				default:
					throw new NotSupportedException($"PMTiles internal compression {compression} is not supported");
			}
		}

		public static byte[] Gzip(byte[] data)
		{
			using var output = new MemoryStream();
			using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
				gzip.Write(data, 0, data.Length);

			return output.ToArray();
		}

		private static ulong ReadVarint(byte[] data, ref int position)
		{
			ulong value = 0;
			var shift = 0;

			while (true)
			{
				if (position >= data.Length)
					throw new InvalidDataException("Truncated PMTiles directory");

				var b = data[position++];
				value |= (ulong)(b & 0x7F) << shift;
				if ((b & 0x80) == 0)
					return value;

				shift += 7;
				if (shift > 63)
					throw new InvalidDataException("Bad varint in PMTiles directory");
			}
		}

		private static void WriteVarint(Stream stream, ulong value)
		{
			while (value >= 0x80)
			{
				stream.WriteByte((byte)(value | 0x80));
				value >>= 7;
			}

			stream.WriteByte((byte)value);
		}
	}
}
