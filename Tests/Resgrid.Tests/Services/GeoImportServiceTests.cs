using System;
using System.Collections.Generic;
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
	public class GeoImportServiceTests
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

		#region GeoJSON Import

		[Test]
		public async Task ImportGeoJson_WithPointFeature_ShouldCreateRegion()
		{
			var geoJson = @"{
				""type"": ""FeatureCollection"",
				""features"": [{
					""type"": ""Feature"",
					""geometry"": { ""type"": ""Point"", ""coordinates"": [-104.99, 39.74] },
					""properties"": { ""name"": ""Fire Hydrant 42"" }
				}]
			}";

			_importsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapImport i, CancellationToken c, bool f) => i);
			_zonesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapZone>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IndoorMapZone z, CancellationToken c, bool f) => z);

			var result = await _service.ImportGeoJsonAsync("m1", "l1", geoJson, "user1");

			result.Status.Should().Be((int)CustomMapImportStatus.Complete);
			_zonesRepo.Verify(x => x.SaveOrUpdateAsync(
				It.Is<IndoorMapZone>(z => z.Name == "Fire Hydrant 42" && z.CenterLatitude == 39.74m && z.CenterLongitude == -104.99m),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task ImportGeoJson_WithPolygonFeature_ShouldCreateRegion()
		{
			var geoJson = @"{
				""type"": ""FeatureCollection"",
				""features"": [{
					""type"": ""Feature"",
					""geometry"": {
						""type"": ""Polygon"",
						""coordinates"": [[[-104.0, 39.0], [-104.0, 40.0], [-103.0, 40.0], [-103.0, 39.0], [-104.0, 39.0]]]
					},
					""properties"": { ""name"": ""District A"" }
				}]
			}";

			_importsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapImport i, CancellationToken c, bool f) => i);
			_zonesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapZone>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IndoorMapZone z, CancellationToken c, bool f) => z);

			var result = await _service.ImportGeoJsonAsync("m1", "l1", geoJson, "user1");

			result.Status.Should().Be((int)CustomMapImportStatus.Complete);
			_zonesRepo.Verify(x => x.SaveOrUpdateAsync(
				It.Is<IndoorMapZone>(z => z.Name == "District A" && z.GeoGeometry != null),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task ImportGeoJson_WithLineStringFeature_ShouldCreateRegion()
		{
			var geoJson = @"{
				""type"": ""FeatureCollection"",
				""features"": [{
					""type"": ""Feature"",
					""geometry"": {
						""type"": ""LineString"",
						""coordinates"": [[-104.0, 39.0], [-103.0, 40.0]]
					},
					""properties"": { ""name"": ""Emergency Route"" }
				}]
			}";

			_importsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapImport i, CancellationToken c, bool f) => i);
			_zonesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapZone>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IndoorMapZone z, CancellationToken c, bool f) => z);

			var result = await _service.ImportGeoJsonAsync("m1", "l1", geoJson, "user1");

			result.Status.Should().Be((int)CustomMapImportStatus.Complete);
			_zonesRepo.Verify(x => x.SaveOrUpdateAsync(
				It.Is<IndoorMapZone>(z => z.Name == "Emergency Route"),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task ImportGeoJson_MultipleFeatures_ShouldCreateMultipleRegions()
		{
			var geoJson = @"{
				""type"": ""FeatureCollection"",
				""features"": [
					{
						""type"": ""Feature"",
						""geometry"": { ""type"": ""Point"", ""coordinates"": [-104.0, 39.0] },
						""properties"": { ""name"": ""Point A"" }
					},
					{
						""type"": ""Feature"",
						""geometry"": { ""type"": ""Point"", ""coordinates"": [-105.0, 40.0] },
						""properties"": { ""name"": ""Point B"" }
					}
				]
			}";

			_importsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapImport i, CancellationToken c, bool f) => i);
			_zonesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapZone>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IndoorMapZone z, CancellationToken c, bool f) => z);

			var result = await _service.ImportGeoJsonAsync("m1", "l1", geoJson, "user1");

			result.Status.Should().Be((int)CustomMapImportStatus.Complete);
			_zonesRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapZone>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2));
		}

		[Test]
		public async Task ImportGeoJson_InvalidJson_ShouldFailWithErrorMessage()
		{
			_importsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapImport i, CancellationToken c, bool f) => i);

			var result = await _service.ImportGeoJsonAsync("m1", "l1", "{{invalid json", "user1");

			result.Status.Should().Be((int)CustomMapImportStatus.Failed);
			result.ErrorMessage.Should().NotBeNullOrEmpty();
		}

		[Test]
		public async Task ImportGeoJson_ShouldCreateAuditRecord()
		{
			var geoJson = @"{ ""type"": ""FeatureCollection"", ""features"": [] }";

			_importsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapImport i, CancellationToken c, bool f) => i);

			var result = await _service.ImportGeoJsonAsync("m1", "l1", geoJson, "user1");

			result.Should().NotBeNull();
			result.CustomMapId.Should().Be("m1");
			result.CustomMapLayerId.Should().Be("l1");
			result.ImportedById.Should().Be("user1");
			result.SourceFileType.Should().Be((int)CustomMapImportFileType.GeoJSON);
		}

		[Test]
		public async Task ImportGeoJson_StatusTransition_ShouldGoFromProcessingToComplete()
		{
			var geoJson = @"{ ""type"": ""FeatureCollection"", ""features"": [] }";

			var statusHistory = new List<int>();
			_importsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback<CustomMapImport, CancellationToken, bool>((i, c, f) => statusHistory.Add(i.Status))
				.ReturnsAsync((CustomMapImport i, CancellationToken c, bool f) => i);

			await _service.ImportGeoJsonAsync("m1", "l1", geoJson, "user1");

			statusHistory.Should().HaveCount(2);
			statusHistory[0].Should().Be((int)CustomMapImportStatus.Processing);
			statusHistory[1].Should().Be((int)CustomMapImportStatus.Complete);
		}

		[Test]
		public async Task ImportGeoJson_RegionsShouldBeDispatchableByDefault()
		{
			var geoJson = @"{
				""type"": ""FeatureCollection"",
				""features"": [{
					""type"": ""Feature"",
					""geometry"": { ""type"": ""Point"", ""coordinates"": [-104.0, 39.0] },
					""properties"": { ""name"": ""Test"" }
				}]
			}";

			_importsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CustomMapImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CustomMapImport i, CancellationToken c, bool f) => i);
			_zonesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IndoorMapZone>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IndoorMapZone z, CancellationToken c, bool f) => z);

			await _service.ImportGeoJsonAsync("m1", "l1", geoJson, "user1");

			_zonesRepo.Verify(x => x.SaveOrUpdateAsync(
				It.Is<IndoorMapZone>(z => z.IsDispatchable && z.IsSearchable),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		#endregion GeoJSON Import
	}
}
