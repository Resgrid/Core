using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using System.IO;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class TileProcessingTests
	{
		private Mock<IIndoorMapsRepository> _mapsRepo;
		private Mock<IIndoorMapFloorsRepository> _floorsRepo;
		private Mock<IIndoorMapZonesRepository> _zonesRepo;
		private Mock<ICustomMapTilesRepository> _tilesRepo;
		private Mock<ICustomMapImportsRepository> _importsRepo;
		private CustomMapService _service;

		[SetUp]
		public void SetUp()
		{
			_mapsRepo = new Mock<IIndoorMapsRepository>();
			_floorsRepo = new Mock<IIndoorMapFloorsRepository>();
			_zonesRepo = new Mock<IIndoorMapZonesRepository>();
			_tilesRepo = new Mock<ICustomMapTilesRepository>();
			_importsRepo = new Mock<ICustomMapImportsRepository>();
			_service = new CustomMapService(_mapsRepo.Object, _floorsRepo.Object, _zonesRepo.Object, _tilesRepo.Object, _importsRepo.Object);
		}

		private byte[] CreateTestImage(int width, int height)
		{
			using (var image = new Image<Rgba32>(width, height))
			{
				using (var ms = new MemoryStream())
				{
					image.SaveAsPng(ms);
					return ms.ToArray();
				}
			}
		}

		[Test]
		public async Task ProcessAndStoreTiles_SmallImage_ShouldNotTile()
		{
			var layer = new IndoorMapFloor { IndoorMapFloorId = "l1" };
			_floorsRepo.Setup(x => x.GetByIdAsync("l1")).ReturnsAsync(layer);
			_floorsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapFloor>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IndoorMapFloor f, CancellationToken c, bool fl) => f);

			var imageData = CreateTestImage(1024, 768);

			await _service.ProcessAndStoreTilesAsync("l1", imageData);

			layer.IsTiled.Should().BeFalse();
			layer.ImageData.Should().NotBeNull();
			layer.SourceFileSize.Should().Be(imageData.Length);
			_tilesRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapTile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task ProcessAndStoreTiles_LargeImage_ShouldTile()
		{
			var layer = new IndoorMapFloor { IndoorMapFloorId = "l1" };
			_floorsRepo.Setup(x => x.GetByIdAsync("l1")).ReturnsAsync(layer);
			_floorsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapFloor>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IndoorMapFloor f, CancellationToken c, bool fl) => f);
			_tilesRepo.Setup(x => x.DeleteTilesForLayerAsync("l1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_tilesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapTile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapTile t, CancellationToken c, bool f) => t);

			var imageData = CreateTestImage(4096, 3072);

			await _service.ProcessAndStoreTilesAsync("l1", imageData);

			layer.IsTiled.Should().BeTrue();
			layer.ImageData.Should().BeNull();
			layer.TileMinZoom.Should().NotBeNull();
			layer.TileMaxZoom.Should().NotBeNull();
			layer.TileMinZoom.Value.Should().BeLessThanOrEqualTo(layer.TileMaxZoom.Value);
			_tilesRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapTile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.AtLeastOnce);
		}

		[Test]
		public async Task ProcessAndStoreTiles_LargeImage_ShouldHaveCorrectZoomLevels()
		{
			var layer = new IndoorMapFloor { IndoorMapFloorId = "l1" };
			_floorsRepo.Setup(x => x.GetByIdAsync("l1")).ReturnsAsync(layer);
			_floorsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapFloor>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IndoorMapFloor f, CancellationToken c, bool fl) => f);
			_tilesRepo.Setup(x => x.DeleteTilesForLayerAsync("l1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_tilesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapTile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapTile t, CancellationToken c, bool f) => t);

			var imageData = CreateTestImage(2560, 2560);

			await _service.ProcessAndStoreTilesAsync("l1", imageData);

			layer.TileMinZoom.Should().Be(0);
			// maxZoom = ceil(log2(2560/256)) = ceil(log2(10)) = ceil(3.32) = 4
			layer.TileMaxZoom.Should().Be(4);
		}

		[Test]
		public async Task ProcessAndStoreTiles_NullLayer_ShouldNotThrow()
		{
			_floorsRepo.Setup(x => x.GetByIdAsync("l99")).ReturnsAsync((IndoorMapFloor)null);

			await _service.Invoking(s => s.ProcessAndStoreTilesAsync("l99", new byte[] { 1, 2, 3 }))
				.Should().NotThrowAsync();
		}

		[Test]
		public async Task ProcessAndStoreTiles_LargeImage_TilesShouldBe256x256()
		{
			var layer = new IndoorMapFloor { IndoorMapFloorId = "l1" };
			_floorsRepo.Setup(x => x.GetByIdAsync("l1")).ReturnsAsync(layer);
			_floorsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapFloor>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IndoorMapFloor f, CancellationToken c, bool fl) => f);
			_tilesRepo.Setup(x => x.DeleteTilesForLayerAsync("l1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

			CustomMapTile savedTile = null;
			_tilesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapTile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback<CustomMapTile, CancellationToken, bool>((t, c, f) => savedTile = t)
				.ReturnsAsync((CustomMapTile t, CancellationToken c, bool f) => t);

			var imageData = CreateTestImage(3000, 3000);

			await _service.ProcessAndStoreTilesAsync("l1", imageData);

			savedTile.Should().NotBeNull();
			savedTile.TileData.Should().NotBeNull();
			savedTile.TileContentType.Should().Be("image/png");

			// Verify the saved tile image is 256x256
			using (var tileImage = Image.Load(savedTile.TileData))
			{
				tileImage.Width.Should().Be(256);
				tileImage.Height.Should().Be(256);
			}
		}

		[Test]
		public async Task ProcessAndStoreTiles_LargeImage_ShouldDeleteExistingTilesFirst()
		{
			var layer = new IndoorMapFloor { IndoorMapFloorId = "l1" };
			_floorsRepo.Setup(x => x.GetByIdAsync("l1")).ReturnsAsync(layer);
			_floorsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapFloor>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IndoorMapFloor f, CancellationToken c, bool fl) => f);
			_tilesRepo.Setup(x => x.DeleteTilesForLayerAsync("l1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_tilesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapTile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapTile t, CancellationToken c, bool f) => t);

			var imageData = CreateTestImage(4096, 4096);

			await _service.ProcessAndStoreTilesAsync("l1", imageData);

			_tilesRepo.Verify(x => x.DeleteTilesForLayerAsync("l1", It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
