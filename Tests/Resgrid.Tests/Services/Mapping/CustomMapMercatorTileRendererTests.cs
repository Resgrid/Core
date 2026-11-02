using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Resgrid.Tests.Services.Mapping
{
	/// <summary>Web Mercator tiles cut from georeferenced custom map layers (plan §6.7).</summary>
	[TestFixture]
	public class CustomMapMercatorTileRendererTests
	{
		private static byte[] RedPng(int width, int height)
		{
			using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0, 255));
			using var stream = new MemoryStream();
			image.SaveAsPng(stream);
			return stream.ToArray();
		}

		private static (int X, int Y) TileFor(double latitude, double longitude, int zoom)
		{
			var n = Math.Pow(2, zoom);
			var x = (int)Math.Floor((longitude + 180.0) / 360.0 * n);
			var sin = Math.Sin(latitude * Math.PI / 180.0);
			var y = (int)Math.Floor((0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * n);
			return (x, y);
		}

		private static CustomMapMercatorTileRenderer Renderer()
		{
			return new CustomMapMercatorTileRenderer(new Mock<ICustomMapService>().Object, new Mock<ICustomMapTilesRepository>().Object);
		}

		private static IndoorMapFloor Layer()
		{
			return new IndoorMapFloor
			{
				IndoorMapFloorId = Guid.NewGuid().ToString(),
				IndoorMapId = "map",
				ImageData = RedPng(256, 256),
				BoundsNELat = 39.60m,
				BoundsNELon = -119.70m,
				BoundsSWLat = 39.50m,
				BoundsSWLon = -119.80m
			};
		}

		[Test]
		public async Task a_tile_inside_the_layer_is_painted()
		{
			var (x, y) = TileFor(39.55, -119.75, 14);

			var bytes = await Renderer().RenderTileAsync(Layer(), 14, x, y);

			bytes.Should().NotBeNull();
			using var tile = Image.Load<Rgba32>(bytes);
			tile.Width.Should().Be(256);
			tile[128, 128].R.Should().Be(255);
			tile[128, 128].A.Should().Be(255);
		}

		[Test]
		public async Task a_tile_outside_the_layer_is_empty()
		{
			var (x, y) = TileFor(40.5, -110.0, 14);

			(await Renderer().RenderTileAsync(Layer(), 14, x, y)).Should().BeNull();
		}

		[Test]
		public async Task a_layer_without_bounds_has_no_mercator_tiles()
		{
			var service = new Mock<ICustomMapService>();
			service.Setup(s => s.GetCustomMapByIdAsync("map")).ReturnsAsync(new IndoorMap());
			var renderer = new CustomMapMercatorTileRenderer(service.Object, new Mock<ICustomMapTilesRepository>().Object);
			var layer = Layer();
			layer.BoundsNELat = null;

			(await renderer.GetBoundsAsync(layer)).Should().BeNull();
		}
	}
}
