using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Services.Offline;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Resgrid.Tests.Services.Offline
{
	/// <summary>Maximum response area geometry and the department sprite (§19.2–19.3).</summary>
	[TestFixture]
	public class OfflineResponseAreaTests
	{
		private const string Square = "{\"type\":\"Polygon\",\"coordinates\":[[[-119.9,39.4],[-119.6,39.4],[-119.6,39.7],[-119.9,39.7],[-119.9,39.4]]]}";

		[Test]
		public void a_polygon_parses_and_is_covered_by_z10_cells()
		{
			var rings = OfflineResponseArea.Parse(Square, out var error);

			error.Should().BeNull();
			rings.Should().ContainSingle();

			var cells = OfflineResponseArea.CoveringCells(rings);
			cells.Should().NotBeEmpty();
			cells.Should().Contain((OfflineResponseArea.TileX(-119.75, 10), OfflineResponseArea.TileY(39.55, 10)));

			// Every cell actually touches the square.
			foreach (var (x, y) in cells)
			{
				var bounds = OfflineResponseArea.TileBounds(10, x, y);
				bounds.East.Should().BeGreaterThan(-119.9);
				bounds.West.Should().BeLessThan(-119.6);
			}

			OfflineResponseArea.AreaSquareKm(rings).Should().BeInRange(800, 900);
		}

		[Test]
		public void features_and_multipolygons_parse_and_an_unclosed_ring_is_closed()
		{
			var feature = "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"geometry\":{\"type\":\"MultiPolygon\",\"coordinates\":[[[[0,0],[1,0],[1,1],[0,1]]],[[[5,5],[6,5],[6,6],[5,6],[5,5]]]]},\"properties\":{}}]}";

			var rings = OfflineResponseArea.Parse(feature, out var error);

			error.Should().BeNull();
			rings.Should().HaveCount(2);
			rings[0].First().Should().Be(rings[0].Last());
		}

		[Test]
		public void self_intersecting_and_malformed_areas_are_refused()
		{
			var bowtie = "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,1],[1,0],[0,1],[0,0]]]}";

			OfflineResponseArea.Parse(bowtie, out var error).Should().BeNull();
			error.Should().Be("self-intersecting");

			OfflineResponseArea.Parse("{\"type\":\"Point\",\"coordinates\":[0,0]}", out error).Should().BeNull();
			error.Should().Be("invalid");

			OfflineResponseArea.Parse("not json", out error).Should().BeNull();
		}

		[Test]
		public void the_fallback_circle_has_the_requested_radius()
		{
			var ring = OfflineResponseArea.Circle(39.5, -119.8, 40);

			ring.Should().HaveCount(65);
			OfflineResponseArea.AreaSquareKm(new[] { ring }.ToList()).Should().BeInRange(4900, 5100);
			OfflineResponseArea.IsSelfIntersecting(ring).Should().BeFalse();
		}

		[Test]
		public void the_sprite_packs_every_icon_at_both_ratios()
		{
			byte[] Png(int w, int h, byte r)
			{
				using var image = new Image<Rgba32>(w, h, new Rgba32(r, 0, 0, 255));
				using var stream = new System.IO.MemoryStream();
				image.SaveAsPng(stream);
				return stream.ToArray();
			}

			var files = DepartmentSpriteBuilder.Build(new[] { ("ut-1", Png(32, 37, 255)), ("ct-2", Png(64, 64, 100)), ("bad", new byte[] { 1, 2, 3 }) });

			files.Keys.Should().BeEquivalentTo(new[] { "sprite.json", "sprite.png", "sprite@2x.json", "sprite@2x.png" });

			var index = JsonDocument.Parse(files["sprite.json"]).RootElement;
			index.TryGetProperty("ut-1", out var unitIcon).Should().BeTrue();
			index.TryGetProperty("bad", out _).Should().BeFalse();
			unitIcon.GetProperty("pixelRatio").GetInt32().Should().Be(1);
			index.GetProperty("ct-2").GetProperty("width").GetInt32().Should().Be(48);

			var retina = JsonDocument.Parse(files["sprite@2x.json"]).RootElement;
			retina.GetProperty("ct-2").GetProperty("width").GetInt32().Should().Be(96);
			retina.GetProperty("ct-2").GetProperty("pixelRatio").GetInt32().Should().Be(2);

			using var sheet = Image.Load<Rgba32>(files["sprite.png"]);
			var at = index.GetProperty("ut-1");
			sheet[at.GetProperty("x").GetInt32() + 2, at.GetProperty("y").GetInt32() + 2].R.Should().Be(255);
		}
	}
}
