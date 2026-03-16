using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class CustomMapServiceTests
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

		#region Maps CRUD

		[Test]
		public async Task SaveCustomMapAsync_ShouldCallRepository()
		{
			var map = new IndoorMap { IndoorMapId = "m1", Name = "Test" };
			_mapsRepo.Setup(x => x.SaveOrUpdateAsync(map, It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(map);

			var result = await _service.SaveCustomMapAsync(map);

			result.Should().NotBeNull();
			result.IndoorMapId.Should().Be("m1");
			_mapsRepo.Verify(x => x.SaveOrUpdateAsync(map, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task GetCustomMapByIdAsync_ShouldReturnMap()
		{
			var map = new IndoorMap { IndoorMapId = "m1" };
			_mapsRepo.Setup(x => x.GetByIdAsync("m1")).ReturnsAsync(map);

			var result = await _service.GetCustomMapByIdAsync("m1");

			result.Should().NotBeNull();
			result.IndoorMapId.Should().Be("m1");
		}

		[Test]
		public async Task GetCustomMapsForDepartmentAsync_ShouldFilterByType()
		{
			var maps = new List<IndoorMap>
			{
				new IndoorMap { IndoorMapId = "m1", MapType = 0, IsDeleted = false },
				new IndoorMap { IndoorMapId = "m2", MapType = 1, IsDeleted = false },
				new IndoorMap { IndoorMapId = "m3", MapType = 0, IsDeleted = true }
			};
			_mapsRepo.Setup(x => x.GetIndoorMapsByDepartmentIdAsync(1)).ReturnsAsync(maps);

			var result = await _service.GetCustomMapsForDepartmentAsync(1, CustomMapType.Indoor);

			result.Should().HaveCount(1);
			result[0].IndoorMapId.Should().Be("m1");
		}

		[Test]
		public async Task GetCustomMapsForDepartmentAsync_NoFilter_ShouldReturnAllNonDeleted()
		{
			var maps = new List<IndoorMap>
			{
				new IndoorMap { IndoorMapId = "m1", MapType = 0, IsDeleted = false },
				new IndoorMap { IndoorMapId = "m2", MapType = 1, IsDeleted = false },
				new IndoorMap { IndoorMapId = "m3", MapType = 0, IsDeleted = true }
			};
			_mapsRepo.Setup(x => x.GetIndoorMapsByDepartmentIdAsync(1)).ReturnsAsync(maps);

			var result = await _service.GetCustomMapsForDepartmentAsync(1);

			result.Should().HaveCount(2);
		}

		[Test]
		public async Task DeleteCustomMapAsync_ShouldSoftDelete()
		{
			var map = new IndoorMap { IndoorMapId = "m1", IsDeleted = false };
			_mapsRepo.Setup(x => x.GetByIdAsync("m1")).ReturnsAsync(map);
			_mapsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMap>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(map);

			var result = await _service.DeleteCustomMapAsync("m1");

			result.Should().BeTrue();
			map.IsDeleted.Should().BeTrue();
		}

		[Test]
		public async Task DeleteCustomMapAsync_MapNotFound_ShouldReturnFalse()
		{
			_mapsRepo.Setup(x => x.GetByIdAsync("m99")).ReturnsAsync((IndoorMap)null);

			var result = await _service.DeleteCustomMapAsync("m99");

			result.Should().BeFalse();
		}

		#endregion Maps CRUD

		#region Layers CRUD

		[Test]
		public async Task GetLayersForMapAsync_ShouldReturnOrderedNonDeleted()
		{
			var layers = new List<IndoorMapFloor>
			{
				new IndoorMapFloor { IndoorMapFloorId = "l1", FloorOrder = 2, IsDeleted = false },
				new IndoorMapFloor { IndoorMapFloorId = "l2", FloorOrder = 1, IsDeleted = false },
				new IndoorMapFloor { IndoorMapFloorId = "l3", FloorOrder = 0, IsDeleted = true }
			};
			_floorsRepo.Setup(x => x.GetFloorsByIndoorMapIdAsync("m1")).ReturnsAsync(layers);

			var result = await _service.GetLayersForMapAsync("m1");

			result.Should().HaveCount(2);
			result[0].IndoorMapFloorId.Should().Be("l2");
			result[1].IndoorMapFloorId.Should().Be("l1");
		}

		[Test]
		public async Task DeleteLayerAsync_ShouldSoftDelete()
		{
			var layer = new IndoorMapFloor { IndoorMapFloorId = "l1", IsDeleted = false };
			_floorsRepo.Setup(x => x.GetByIdAsync("l1")).ReturnsAsync(layer);
			_floorsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapFloor>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(layer);

			var result = await _service.DeleteLayerAsync("l1");

			result.Should().BeTrue();
			layer.IsDeleted.Should().BeTrue();
		}

		#endregion Layers CRUD

		#region Regions CRUD

		[Test]
		public async Task GetRegionsForLayerAsync_ShouldReturnNonDeleted()
		{
			var regions = new List<IndoorMapZone>
			{
				new IndoorMapZone { IndoorMapZoneId = "r1", IsDeleted = false },
				new IndoorMapZone { IndoorMapZoneId = "r2", IsDeleted = true }
			};
			_zonesRepo.Setup(x => x.GetZonesByFloorIdAsync("l1")).ReturnsAsync(regions);

			var result = await _service.GetRegionsForLayerAsync("l1");

			result.Should().HaveCount(1);
			result[0].IndoorMapZoneId.Should().Be("r1");
		}

		[Test]
		public async Task DeleteRegionAsync_ShouldSoftDelete()
		{
			var region = new IndoorMapZone { IndoorMapZoneId = "r1", IsDeleted = false };
			_zonesRepo.Setup(x => x.GetByIdAsync("r1")).ReturnsAsync(region);
			_zonesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapZone>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(region);

			var result = await _service.DeleteRegionAsync("r1");

			result.Should().BeTrue();
			region.IsDeleted.Should().BeTrue();
		}

		#endregion Regions CRUD

		#region Dispatch Integration

		[Test]
		public async Task GetRegionDisplayNameAsync_ShouldBuildFullPath()
		{
			var region = new IndoorMapZone { IndoorMapZoneId = "r1", IndoorMapFloorId = "l1", Name = "Room 201" };
			var layer = new IndoorMapFloor { IndoorMapFloorId = "l1", IndoorMapId = "m1", Name = "Floor 2" };
			var map = new IndoorMap { IndoorMapId = "m1", Name = "Main Hospital" };

			_zonesRepo.Setup(x => x.GetByIdAsync("r1")).ReturnsAsync(region);
			_floorsRepo.Setup(x => x.GetByIdAsync("l1")).ReturnsAsync(layer);
			_mapsRepo.Setup(x => x.GetByIdAsync("m1")).ReturnsAsync(map);

			var result = await _service.GetRegionDisplayNameAsync("r1");

			result.Should().Be("Main Hospital > Floor 2 > Room 201");
		}

		[Test]
		public async Task GetRegionDisplayNameAsync_NoMap_ShouldReturnLayerAndRegion()
		{
			var region = new IndoorMapZone { IndoorMapZoneId = "r1", IndoorMapFloorId = "l1", Name = "Stage A" };
			var layer = new IndoorMapFloor { IndoorMapFloorId = "l1", IndoorMapId = "m1", Name = "Base" };

			_zonesRepo.Setup(x => x.GetByIdAsync("r1")).ReturnsAsync(region);
			_floorsRepo.Setup(x => x.GetByIdAsync("l1")).ReturnsAsync(layer);
			_mapsRepo.Setup(x => x.GetByIdAsync("m1")).ReturnsAsync((IndoorMap)null);

			var result = await _service.GetRegionDisplayNameAsync("r1");

			result.Should().Be("Base > Stage A");
		}

		[Test]
		public async Task GetRegionDisplayNameAsync_RegionNotFound_ShouldReturnNull()
		{
			_zonesRepo.Setup(x => x.GetByIdAsync("r99")).ReturnsAsync((IndoorMapZone)null);

			var result = await _service.GetRegionDisplayNameAsync("r99");

			result.Should().BeNull();
		}

		[Test]
		public async Task SearchRegionsAsync_ShouldReturnResults()
		{
			var zones = new List<IndoorMapZone>
			{
				new IndoorMapZone { IndoorMapZoneId = "r1", Name = "Room 101" }
			};
			_zonesRepo.Setup(x => x.SearchZonesAsync(1, "Room")).ReturnsAsync(zones);

			var result = await _service.SearchRegionsAsync(1, "Room");

			result.Should().HaveCount(1);
			result[0].Name.Should().Be("Room 101");
		}

		#endregion Dispatch Integration

		#region Tiles

		[Test]
		public async Task GetTileAsync_ShouldDelegateToRepository()
		{
			var tile = new CustomMapTile { CustomMapTileId = "t1", TileData = new byte[] { 1, 2, 3 } };
			_tilesRepo.Setup(x => x.GetTileAsync("l1", 2, 3, 4)).ReturnsAsync(tile);

			var result = await _service.GetTileAsync("l1", 2, 3, 4);

			result.Should().NotBeNull();
			result.TileData.Should().HaveCount(3);
		}

		[Test]
		public async Task DeleteTilesForLayerAsync_ShouldCallRepository()
		{
			_tilesRepo.Setup(x => x.DeleteTilesForLayerAsync("l1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

			await _service.DeleteTilesForLayerAsync("l1");

			_tilesRepo.Verify(x => x.DeleteTilesForLayerAsync("l1", It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion Tiles

		#region Imports

		[Test]
		public async Task GetImportsForMapAsync_ShouldReturnResults()
		{
			var imports = new List<CustomMapImport>
			{
				new CustomMapImport { CustomMapImportId = "i1", SourceFileName = "test.geojson" }
			};
			_importsRepo.Setup(x => x.GetImportsForMapAsync("m1")).ReturnsAsync(imports);

			var result = await _service.GetImportsForMapAsync("m1");

			result.Should().HaveCount(1);
			result[0].SourceFileName.Should().Be("test.geojson");
		}

		[Test]
		public async Task ImportGeoJsonAsync_ValidGeoJson_ShouldCreateRegions()
		{
			var geoJson = @"{
				""type"": ""FeatureCollection"",
				""features"": [
					{
						""type"": ""Feature"",
						""geometry"": {
							""type"": ""Point"",
							""coordinates"": [-104.99, 39.74]
						},
						""properties"": {
							""name"": ""Test Point""
						}
					}
				]
			}";

			_importsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapImport i, CancellationToken c, bool f) => i);
			_zonesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapZone>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IndoorMapZone z, CancellationToken c, bool f) => z);

			var result = await _service.ImportGeoJsonAsync("m1", "l1", geoJson, "user1");

			result.Should().NotBeNull();
			result.Status.Should().Be((int)CustomMapImportStatus.Complete);
			_zonesRepo.Verify(x => x.SaveOrUpdateAsync(It.Is<IndoorMapZone>(z => z.Name == "Test Point"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task ImportGeoJsonAsync_InvalidJson_ShouldSetStatusFailed()
		{
			_importsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapImport i, CancellationToken c, bool f) => i);

			var result = await _service.ImportGeoJsonAsync("m1", "l1", "not-valid-json{{{", "user1");

			result.Should().NotBeNull();
			result.Status.Should().Be((int)CustomMapImportStatus.Failed);
			result.ErrorMessage.Should().NotBeNullOrEmpty();
		}

		#endregion Imports
	}
}
