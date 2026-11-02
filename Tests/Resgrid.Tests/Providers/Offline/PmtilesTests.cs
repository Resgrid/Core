using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.GeoLocationProvider.Offline;

namespace Resgrid.Tests.Providers.Offline
{
	/// <summary>PMTiles v3 primitives, writer and region cut used by offline base-map packs (§19.4).</summary>
	[TestFixture]
	public class PmtilesTests
	{
		[Test]
		public void tile_ids_match_the_spec_examples_and_round_trip()
		{
			Pmtiles.ZxyToTileId(0, 0, 0).Should().Be(0);
			Pmtiles.ZxyToTileId(1, 0, 0).Should().Be(1);
			Pmtiles.ZxyToTileId(1, 0, 1).Should().Be(2);
			Pmtiles.ZxyToTileId(1, 1, 1).Should().Be(3);
			Pmtiles.ZxyToTileId(1, 1, 0).Should().Be(4);
			Pmtiles.ZxyToTileId(2, 0, 0).Should().Be(5);

			for (var z = 0; z <= 6; z++)
			{
				var n = 1UL << z;
				for (ulong x = 0; x < n; x++)
				for (ulong y = 0; y < n; y++)
					Pmtiles.TileIdToZxy(Pmtiles.ZxyToTileId(z, x, y)).Should().Be((z, x, y));
			}
		}

		[Test]
		public void a_cells_descendants_are_one_contiguous_range_per_zoom()
		{
			const int cellZoom = 3;
			for (ulong cx = 0; cx < 8; cx++)
			for (ulong cy = 0; cy < 8; cy++)
			{
				var ranges = Pmtiles.CellRanges(cellZoom, cx, cy, cellZoom, 6);

				for (var z = cellZoom; z <= 6; z++)
				{
					var k = z - cellZoom;
					var expected = new List<ulong>();
					for (var x = cx << k; x < (cx + 1) << k; x++)
					for (var y = cy << k; y < (cy + 1) << k; y++)
						expected.Add(Pmtiles.ZxyToTileId(z, x, y));

					var range = ranges[z - cellZoom];
					expected.Min().Should().Be(range.First);
					expected.Max().Should().Be(range.Last);
					((ulong)expected.Count).Should().Be(range.Last - range.First + 1);
				}
			}
		}

		[Test]
		public void cell_ranges_include_the_ancestors_below_the_cell_zoom()
		{
			var ranges = Pmtiles.CellRanges(10, 163, 395, 0, 12);

			ranges.Should().HaveCount(13);
			ranges[0].Should().Be(new TileIdRange(0, 0));
			ranges[9].First.Should().Be(Pmtiles.ZxyToTileId(9, 81, 197));
		}

		[Test]
		public void directories_round_trip_with_offset_elision()
		{
			var entries = new List<PmtilesEntry>
			{
				new PmtilesEntry(5, 0, 100, 1),
				new PmtilesEntry(6, 100, 50, 3),
				new PmtilesEntry(20, 0, 100, 1),
				new PmtilesEntry(21, 500, 7, 0)
			};

			var restored = Pmtiles.DeserializeDirectory(Pmtiles.SerializeDirectory(entries));

			restored.Should().BeEquivalentTo(entries, options => options.WithStrictOrdering());
		}

		[Test]
		public void the_header_round_trips()
		{
			var header = new PmtilesHeader
			{
				RootOffset = 127, RootLength = 40, MetadataOffset = 167, MetadataLength = 20, TileDataOffset = 187, TileDataLength = 999,
				AddressedTilesCount = 9, TileEntriesCount = 5, TileContentsCount = 4, Clustered = true, InternalCompression = 2, TileCompression = 2,
				TileType = 1, MinZoom = 10, MaxZoom = 15, MinLonE7 = -1200000000, MinLatE7 = 390000000, MaxLonE7 = -1190000000, MaxLatE7 = 400000000,
				CenterZoom = 12, CenterLonE7 = -1195000000, CenterLatE7 = 395000000
			};

			PmtilesHeader.Parse(header.ToBytes()).Should().BeEquivalentTo(header);
		}

		[Test]
		public void a_directory_too_big_for_the_root_is_split_into_leaves()
		{
			var random = new Random(7);
			ulong id = 0, offset = 0;
			var entries = new List<PmtilesEntry>();
			for (var i = 0; i < 60000; i++)
			{
				id += (ulong)random.Next(1, 40);
				var length = (uint)random.Next(50, 90000);
				entries.Add(new PmtilesEntry(id, offset, length, (uint)random.Next(1, 3)));
				offset += length + (ulong)random.Next(0, 2) * 13;
			}

			var (root, leaves) = PmtilesWriter.BuildDirectories(entries);

			root.Length.Should().BeLessThanOrEqualTo(16384 - PmtilesHeader.Length);
			leaves.Length.Should().BeGreaterThan(0);

			var pointers = Pmtiles.DeserializeDirectory(Pmtiles.Decompress(root, Pmtiles.CompressionGzip));
			pointers.Should().OnlyContain(p => p.RunLength == 0);

			var restored = pointers.SelectMany(p => Pmtiles.DeserializeDirectory(Pmtiles.Decompress(leaves.Skip((int)p.Offset).Take((int)p.Length).ToArray(), Pmtiles.CompressionGzip))).ToList();
			restored.Should().BeEquivalentTo(entries, options => options.WithStrictOrdering());
		}

		[Test]
		public async Task an_archive_written_with_leaves_reads_back_and_a_region_cuts_out_of_it()
		{
			var tiles = new List<PmtilesWriterTile>();
			var bodies = new Dictionary<string, byte[]>();
			for (ulong x = 0; x < 128; x++)
			for (ulong y = 0; y < 128; y++)
			{
				var key = $"{x}/{y}";
				var shared = (x + y) % 7 == 0;
				bodies[shared ? "ocean" : key] = shared ? Encoding.ASCII.GetBytes("ocean") : Encoding.ASCII.GetBytes($"tile {x} {y}");
				tiles.Add(new PmtilesWriterTile { TileId = Pmtiles.ZxyToTileId(7, x, y), DataKey = shared ? "ocean" : key });
			}

			var path = Path.Combine(Path.GetTempPath(), "rg-test-" + Guid.NewGuid().ToString("N") + ".pmtiles");
			var cutPath = path + ".cut";
			try
			{
				await using (var output = File.Create(path))
					await PmtilesWriter.WriteAsync(output, tiles, key => Task.FromResult(bodies[key]), Encoding.UTF8.GetBytes("{\"name\":\"test\"}"), 1, 1,
						-180, -85, 180, 85, 7, 7);

				using (var source = new FilePmtilesSource(path))
				{
					var reader = new PmtilesArchiveReader(source);
					var header = await reader.GetHeaderAsync();
					header.TileContentsCount.Should().BeLessThan(header.AddressedTilesCount);
					Encoding.UTF8.GetString(await reader.GetMetadataAsync()).Should().Be("{\"name\":\"test\"}");

					// The z7 descendants of z5 tile (3, 9): 16 tiles.
					var ranges = Pmtiles.CellRanges(5, 3, 9, 7, 7);
					var entries = await reader.GetEntriesAsync(ranges);
					entries.Sum(e => (long)e.RunLength).Should().Be(16);

					await using (var cut = File.Create(cutPath))
						await new PmtilesRegionExtractor(reader).ExtractAsync(cut, ranges, -150, 60, -140, 70, 7, 7);
				}

				using (var cutSource = new FilePmtilesSource(cutPath))
				{
					var cutReader = new PmtilesArchiveReader(cutSource);
					var cutHeader = await cutReader.GetHeaderAsync();
					cutHeader.AddressedTilesCount.Should().Be(16);

					var id = Pmtiles.ZxyToTileId(7, 13, 37);
					var entry = (await cutReader.GetEntriesAsync(new[] { new TileIdRange(id, id) })).Single();
					var body = await cutSource.ReadAsync((long)(cutHeader.TileDataOffset + entry.Offset), (int)entry.Length);
					Encoding.ASCII.GetString(body).Should().Be((13 + 37) % 7 == 0 ? "ocean" : "tile 13 37");
				}
			}
			finally
			{
				File.Delete(path);
				File.Delete(cutPath);
			}
		}
	}
}
