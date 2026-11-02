using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Resgrid.Providers.GeoLocationProvider.Offline
{
	/// <summary>Random-access bytes of a PMTiles archive (HTTP Range, or a local file).</summary>
	public interface IPmtilesSource : IDisposable
	{
		/// <summary>Identifies the archive in caches (its URL or path).</summary>
		string Key { get; }

		Task<byte[]> ReadAsync(long offset, int length, CancellationToken cancellationToken = default);
	}

	/// <summary>An archive behind HTTP(S) that honours Range requests (object storage, a CDN, a web server).</summary>
	public sealed class HttpPmtilesSource : IPmtilesSource
	{
		private static readonly HttpClient SharedClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

		private readonly HttpClient _client;
		private readonly string _url;

		public HttpPmtilesSource(string url, HttpClient client = null)
		{
			_url = url;
			_client = client ?? SharedClient;
		}

		public string Key => _url;

		public async Task<byte[]> ReadAsync(long offset, int length, CancellationToken cancellationToken = default)
		{
			if (length <= 0)
				return Array.Empty<byte>();

			using var request = new HttpRequestMessage(HttpMethod.Get, _url);
			request.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);

			using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (response.StatusCode != HttpStatusCode.PartialContent)
				throw new IOException($"The base map archive did not answer a range request ({(int)response.StatusCode}); it must support HTTP Range.");

			var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
			if (bytes.Length != length)
				throw new IOException($"Short read from the base map archive ({bytes.Length} of {length} bytes)");

			return bytes;
		}

		public void Dispose()
		{
		}
	}

	/// <summary>An archive on local disk (self-hosted regional extract, tests).</summary>
	public sealed class FilePmtilesSource : IPmtilesSource
	{
		private readonly FileStream _stream;
		private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

		public FilePmtilesSource(string path)
		{
			Key = Path.GetFullPath(path);
			_stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
		}

		public string Key { get; }

		public async Task<byte[]> ReadAsync(long offset, int length, CancellationToken cancellationToken = default)
		{
			var buffer = new byte[length];
			await _lock.WaitAsync(cancellationToken);
			try
			{
				_stream.Seek(offset, SeekOrigin.Begin);
				var read = 0;
				while (read < length)
				{
					var n = await _stream.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken);
					if (n == 0)
						throw new IOException("Short read from the base map archive");
					read += n;
				}
			}
			finally
			{
				_lock.Release();
			}

			return buffer;
		}

		public void Dispose()
		{
			_stream.Dispose();
			_lock.Dispose();
		}
	}

	/// <summary>
	/// Reads a PMTiles v3 archive's directories and finds the tile entries in id ranges. Header, root and leaf
	/// directories are cached in memory across instances (keyed by archive URL, which carries the archive version), so
	/// repeated size estimates over the same area cost no further range reads.
	/// </summary>
	public sealed class PmtilesArchiveReader
	{
		// Directories (and headers) by archive and offset. A planet archive's leaves are a few hundred KB each; the cap keeps
		// the cache to roughly the leaves a busy estimator touches, and a full cache simply starts over.
		private const int MaxCachedDirectories = 2048;
		private static readonly ConcurrentDictionary<string, object> Directories = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);

		private static void Remember(string key, object value)
		{
			if (Directories.Count >= MaxCachedDirectories)
				Directories.Clear();

			Directories[key] = value;
		}

		private readonly IPmtilesSource _source;

		public PmtilesArchiveReader(IPmtilesSource source)
		{
			_source = source;
		}

		public IPmtilesSource Source => _source;

		public async Task<PmtilesHeader> GetHeaderAsync(CancellationToken cancellationToken = default)
		{
			var key = "h|" + _source.Key;
			if (Directories.TryGetValue(key, out var cached) && cached is PmtilesHeader known)
				return known;

			var header = PmtilesHeader.Parse(await _source.ReadAsync(0, PmtilesHeader.Length, cancellationToken));
			Remember(key, header);
			return header;
		}

		public async Task<byte[]> GetMetadataAsync(CancellationToken cancellationToken = default)
		{
			var header = await GetHeaderAsync(cancellationToken);
			if (header.MetadataLength == 0)
				return Array.Empty<byte>();

			var raw = await _source.ReadAsync((long)header.MetadataOffset, (int)header.MetadataLength, cancellationToken);
			return Pmtiles.Decompress(raw, header.InternalCompression);
		}

		/// <summary>The tile entries inside the ranges (clipped to them), sorted by tile id.</summary>
		public async Task<List<PmtilesEntry>> GetEntriesAsync(IReadOnlyList<TileIdRange> ranges, CancellationToken cancellationToken = default)
		{
			var header = await GetHeaderAsync(cancellationToken);
			var root = await GetDirectoryAsync("r", (long)header.RootOffset, (int)header.RootLength, header, cancellationToken);
			var sorted = ranges.OrderBy(r => r.First).ToList();
			var found = new List<PmtilesEntry>();

			await CollectAsync(root, sorted, header, found, 0, cancellationToken);

			return found.OrderBy(e => e.TileId).ToList();
		}

		private async Task CollectAsync(List<PmtilesEntry> directory, List<TileIdRange> ranges, PmtilesHeader header, List<PmtilesEntry> found, int depth,
			CancellationToken cancellationToken)
		{
			if (depth > 4)
				throw new InvalidDataException("PMTiles directories nest too deeply");

			for (var i = 0; i < directory.Count; i++)
			{
				var entry = directory[i];

				if (entry.RunLength == 0)
				{
					var last = i + 1 < directory.Count ? directory[i + 1].TileId - 1 : ulong.MaxValue;
					if (!Overlaps(ranges, entry.TileId, last))
						continue;

					var leaf = await GetDirectoryAsync("l", (long)(header.LeafDirectoryOffset + entry.Offset), (int)entry.Length, header, cancellationToken);
					await CollectAsync(leaf, ranges, header, found, depth + 1, cancellationToken);
					continue;
				}

				var entryLast = entry.TileId + entry.RunLength - 1;
				foreach (var range in ranges)
				{
					if (range.First > entryLast)
						break;
					if (range.Last < entry.TileId)
						continue;

					var first = Math.Max(range.First, entry.TileId);
					var lastId = Math.Min(range.Last, entryLast);
					found.Add(new PmtilesEntry(first, entry.Offset, entry.Length, (uint)(lastId - first + 1)));
				}
			}
		}

		private static bool Overlaps(List<TileIdRange> ranges, ulong first, ulong last)
		{
			foreach (var range in ranges)
			{
				if (range.First > last)
					return false;
				if (range.Last >= first)
					return true;
			}

			return false;
		}

		private async Task<List<PmtilesEntry>> GetDirectoryAsync(string kind, long offset, int length, PmtilesHeader header, CancellationToken cancellationToken)
		{
			var key = kind + "|" + _source.Key + "|" + offset;
			if (Directories.TryGetValue(key, out var cached) && cached is List<PmtilesEntry> known)
				return known;

			var raw = await _source.ReadAsync(offset, length, cancellationToken);
			var entries = Pmtiles.DeserializeDirectory(Pmtiles.Decompress(raw, header.InternalCompression));
			Remember(key, entries);
			return entries;
		}

		/// <summary>Bytes a device downloads for these entries: each distinct tile body once.</summary>
		public static long DistinctBytes(IEnumerable<PmtilesEntry> entries)
		{
			var seen = new HashSet<ulong>();
			long total = 0;

			foreach (var entry in entries)
			{
				if (seen.Add(entry.Offset))
					total += entry.Length;
			}

			return total;
		}
	}

	/// <summary>A tile body to write and the tile ids that share it.</summary>
	public sealed class PmtilesWriterTile
	{
		public ulong TileId { get; set; }

		public uint RunLength { get; set; } = 1;

		/// <summary>Key of the body in the writer's data provider; tiles with the same key share one copy.</summary>
		public string DataKey { get; set; }
	}

	/// <summary>
	/// Writes PMTiles v3 archives: header, root directory (with leaf directories when the root would not fit in the first
	/// 16 KiB), gzip metadata, then the tile bodies in tile-id order (clustered).
	/// </summary>
	public static class PmtilesWriter
	{
		private const int RootLimit = 16384 - PmtilesHeader.Length;

		/// <summary>
		/// Writes the archive to <paramref name="output"/>. <paramref name="readData"/> returns the body for a data key;
		/// each key is read once, in tile order.
		/// </summary>
		public static async Task WriteAsync(Stream output, IReadOnlyList<PmtilesWriterTile> tiles, Func<string, Task<byte[]>> readData, byte[] metadataJson,
			byte tileType, byte tileCompression, double west, double south, double east, double north, int minZoom, int maxZoom,
			CancellationToken cancellationToken = default)
		{
			var ordered = tiles.OrderBy(t => t.TileId).ToList();

			// Tile bodies go to a temp file first: the directory (which comes first) needs their offsets and lengths.
			var dataPath = Path.Combine(Path.GetTempPath(), "rg-pmtiles-" + Guid.NewGuid().ToString("N") + ".bin");
			try
			{
				var entries = new List<PmtilesEntry>();
				var written = new Dictionary<string, (ulong Offset, uint Length)>(StringComparer.Ordinal);
				ulong addressed = 0;

				await using (var data = new FileStream(dataPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
				{
					foreach (var tile in ordered)
					{
						cancellationToken.ThrowIfCancellationRequested();

						if (!written.TryGetValue(tile.DataKey, out var location))
						{
							var body = await readData(tile.DataKey) ?? Array.Empty<byte>();
							location = ((ulong)data.Position, (uint)body.Length);
							await data.WriteAsync(body, cancellationToken);
							written[tile.DataKey] = location;
						}

						addressed += tile.RunLength;

						// Consecutive ids sharing one body collapse into a run.
						if (entries.Count > 0)
						{
							var last = entries[^1];
							if (last.Offset == location.Offset && last.Length == location.Length && last.TileId + last.RunLength == tile.TileId)
							{
								last.RunLength += tile.RunLength;
								entries[^1] = last;
								continue;
							}
						}

						entries.Add(new PmtilesEntry(tile.TileId, location.Offset, location.Length, tile.RunLength));
					}
				}

				var (root, leaves) = BuildDirectories(entries);
				var metadata = Pmtiles.Gzip(metadataJson ?? Array.Empty<byte>());
				var dataLength = (ulong)new FileInfo(dataPath).Length;

				var header = new PmtilesHeader
				{
					RootOffset = PmtilesHeader.Length,
					RootLength = (ulong)root.Length,
					MetadataOffset = PmtilesHeader.Length + (ulong)root.Length,
					MetadataLength = (ulong)metadata.Length,
					LeafDirectoryOffset = PmtilesHeader.Length + (ulong)root.Length + (ulong)metadata.Length,
					LeafDirectoryLength = (ulong)leaves.Length,
					TileDataOffset = PmtilesHeader.Length + (ulong)root.Length + (ulong)metadata.Length + (ulong)leaves.Length,
					TileDataLength = dataLength,
					AddressedTilesCount = addressed,
					TileEntriesCount = (ulong)entries.Count,
					TileContentsCount = (ulong)written.Count,
					Clustered = true,
					InternalCompression = Pmtiles.CompressionGzip,
					TileCompression = tileCompression,
					TileType = tileType,
					MinZoom = (byte)Math.Clamp(minZoom, 0, 31),
					MaxZoom = (byte)Math.Clamp(maxZoom, 0, 31),
					MinLonE7 = E7(west),
					MinLatE7 = E7(south),
					MaxLonE7 = E7(east),
					MaxLatE7 = E7(north),
					CenterZoom = (byte)Math.Clamp((minZoom + maxZoom) / 2, 0, 31),
					CenterLonE7 = E7((west + east) / 2),
					CenterLatE7 = E7((south + north) / 2)
				};

				await output.WriteAsync(header.ToBytes(), cancellationToken);
				await output.WriteAsync(root, cancellationToken);
				await output.WriteAsync(metadata, cancellationToken);
				await output.WriteAsync(leaves, cancellationToken);

				await using (var data = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
					await data.CopyToAsync(output, cancellationToken);
			}
			finally
			{
				try
				{
					File.Delete(dataPath);
				}
				catch (IOException)
				{
					// A temp file left behind is cleaned by the OS.
				}
			}
		}

		/// <summary>Root only when it fits; else leaves of growing size until the root of leaf pointers fits.</summary>
		public static (byte[] Root, byte[] Leaves) BuildDirectories(IReadOnlyList<PmtilesEntry> entries)
		{
			var single = Pmtiles.Gzip(Pmtiles.SerializeDirectory(entries));
			if (single.Length <= RootLimit)
				return (single, Array.Empty<byte>());

			for (var leafSize = 4096; ; leafSize *= 2)
			{
				var rootEntries = new List<PmtilesEntry>();
				using var leaves = new MemoryStream();

				for (var i = 0; i < entries.Count; i += leafSize)
				{
					var chunk = entries.Skip(i).Take(leafSize).ToList();
					var bytes = Pmtiles.Gzip(Pmtiles.SerializeDirectory(chunk));
					rootEntries.Add(new PmtilesEntry(chunk[0].TileId, (ulong)leaves.Position, (uint)bytes.Length, 0));
					leaves.Write(bytes, 0, bytes.Length);
				}

				var root = Pmtiles.Gzip(Pmtiles.SerializeDirectory(rootEntries));
				if (root.Length <= RootLimit)
					return (root, leaves.ToArray());
			}
		}

		private static int E7(double degrees) => (int)Math.Round(degrees * 10_000_000);
	}

	/// <summary>
	/// Cuts a region out of a source archive (§19.4 region cut): finds the entries in the region's id ranges, downloads
	/// the distinct tile bodies with coalesced range reads and writes a new archive. The size estimate uses only the
	/// directories (no tile bodies).
	/// </summary>
	public sealed class PmtilesRegionExtractor
	{
		private const long CoalesceGap = 256 * 1024;
		private const long MaxReadBytes = 32L * 1024 * 1024;

		private readonly PmtilesArchiveReader _reader;

		public PmtilesRegionExtractor(PmtilesArchiveReader reader)
		{
			_reader = reader;
		}

		public async Task<long> EstimateBytesAsync(IReadOnlyList<TileIdRange> ranges, CancellationToken cancellationToken = default)
		{
			return PmtilesArchiveReader.DistinctBytes(await _reader.GetEntriesAsync(ranges, cancellationToken));
		}

		public async Task ExtractAsync(Stream output, IReadOnlyList<TileIdRange> ranges, double west, double south, double east, double north,
			int minZoom, int maxZoom, CancellationToken cancellationToken = default)
		{
			var header = await _reader.GetHeaderAsync(cancellationToken);
			var entries = await _reader.GetEntriesAsync(ranges, cancellationToken);
			var metadata = await _reader.GetMetadataAsync(cancellationToken);

			// Distinct bodies, read in source order with nearby ranges coalesced.
			var bodies = entries.GroupBy(e => e.Offset).Select(g => (Offset: g.Key, Length: g.First().Length)).OrderBy(b => b.Offset).ToList();
			var cache = new Dictionary<ulong, byte[]>();

			for (var i = 0; i < bodies.Count;)
			{
				var start = bodies[i].Offset;
				var end = start + bodies[i].Length;
				var j = i + 1;
				while (j < bodies.Count && bodies[j].Offset <= end + (ulong)CoalesceGap && bodies[j].Offset + bodies[j].Length - start <= (ulong)MaxReadBytes)
				{
					end = Math.Max(end, bodies[j].Offset + bodies[j].Length);
					j++;
				}

				var block = await _reader.Source.ReadAsync((long)(header.TileDataOffset + start), (int)(end - start), cancellationToken);
				for (var k = i; k < j; k++)
				{
					var body = new byte[bodies[k].Length];
					Buffer.BlockCopy(block, (int)(bodies[k].Offset - start), body, 0, body.Length);
					cache[bodies[k].Offset] = body;
				}

				i = j;
			}

			var tiles = entries.Select(e => new PmtilesWriterTile { TileId = e.TileId, RunLength = e.RunLength, DataKey = e.Offset.ToString() }).ToList();

			await PmtilesWriter.WriteAsync(output, tiles, key => Task.FromResult(cache[ulong.Parse(key)]), metadata, header.TileType, header.TileCompression,
				west, south, east, north, minZoom, maxZoom, cancellationToken);
		}
	}
}
