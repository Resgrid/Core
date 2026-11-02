using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Offline
{
	/// <summary>The offline manifest and department data bundle (§19.3).</summary>
	[TestFixture]
	public class OfflineMapPackServiceTests
	{
		private static int _nextDepartment = 9100;

		private int _departmentId;
		private Mock<IOfflineMapPacksRepository> _packs;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ICustomMapService> _customMaps;
		private Mock<ICustomMapMercatorTileRenderer> _renderer;
		private Mock<IMappingService> _mapping;
		private List<Call> _activeCalls;
		private List<RoutePlan> _routePlans;
		private List<OfflineMapPack> _saved;
		private OfflineMapPacksConfig _config;
		private OfflineMapPackService _service;
		private string _archive;

		[SetUp]
		public void SetUp()
		{
			// Each test its own department: the service caches manifests per department.
			_departmentId = Interlocked.Increment(ref _nextDepartment);
			_archive = MappingConfig.OfflineBasemapArchiveUrl;
			MappingConfig.OfflineBasemapArchiveUrl = "";

			_saved = new List<OfflineMapPack>();
			_config = new OfflineMapPacksConfig();

			_packs = new Mock<IOfflineMapPacksRepository>();
			_packs.Setup(p => p.GetByPackKeysAsync(It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync(() => _saved.ToList());
			_packs.Setup(p => p.SaveOrUpdateAsync(It.IsAny<OfflineMapPack>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((OfflineMapPack pack, CancellationToken _, bool __) =>
				{
					pack.OfflineMapPackId ??= Guid.NewGuid().ToString();
					if (!_saved.Contains(pack))
						_saved.Add(pack);
					return pack;
				});

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetOfflineMapPacksConfigAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => _config);

			_customMaps = new Mock<ICustomMapService>();
			_customMaps.Setup(c => c.GetCustomMapsForDepartmentAsync(It.IsAny<int>(), null)).ReturnsAsync(new List<IndoorMap>
			{
				new IndoorMap { IndoorMapId = "outdoor", Name = "Pre-plan", MapType = (int)CustomMapType.Outdoor },
				new IndoorMap { IndoorMapId = "indoor", Name = "Station 4", MapType = (int)CustomMapType.Indoor, BoundsNELat = 39.6m, BoundsNELon = -119.7m, BoundsSWLat = 39.5m, BoundsSWLon = -119.8m }
			});
			_customMaps.Setup(c => c.GetLayersForMapAsync("outdoor")).ReturnsAsync(new List<IndoorMapFloor>
			{
				new IndoorMapFloor { IndoorMapFloorId = "geo-layer", IndoorMapId = "outdoor", Name = "Site", ImageData = new byte[] { 1, 2, 3 }, BoundsNELat = 39.6m, BoundsNELon = -119.7m, BoundsSWLat = 39.5m, BoundsSWLon = -119.8m }
			});
			_customMaps.Setup(c => c.GetLayersForMapAsync("indoor")).ReturnsAsync(new List<IndoorMapFloor>
			{
				new IndoorMapFloor { IndoorMapFloorId = "floor-1", IndoorMapId = "indoor", Name = "Level 1", ImageData = new byte[] { 9, 9 } }
			});
			_customMaps.Setup(c => c.GetRegionsForLayerAsync("geo-layer")).ReturnsAsync(new List<IndoorMapZone>
			{
				new IndoorMapZone { IndoorMapZoneId = "z1", Name = "Bay", GeoGeometry = "{\"type\":\"Point\",\"coordinates\":[-119.75,39.55]}" }
			});
			_customMaps.Setup(c => c.GetRegionsForLayerAsync("floor-1")).ReturnsAsync(new List<IndoorMapZone>());

			_renderer = new Mock<ICustomMapMercatorTileRenderer>();
			_renderer.Setup(r => r.GetBoundsAsync(It.Is<IndoorMapFloor>(l => l.IndoorMapFloorId == "geo-layer"))).ReturnsAsync(new MercatorLayerBounds { West = 0.1, East = 0.2, North = 0.3, South = 0.4 });

			_mapping = new Mock<IMappingService>();
			_mapping.Setup(m => m.GetPOIsForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new List<Poi> { new Poi { PoiId = 5, PoiTypeId = 2, Name = "Hydrant", Latitude = 39.55, Longitude = -119.75 } });
			_mapping.Setup(m => m.GetPOITypesForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new List<PoiType> { new PoiType { PoiTypeId = 2, Name = "Water", IconKey = "poi-icon" } });

			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetUnitTypesForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new List<UnitType> { new UnitType { IconKey = "engine-icon" } });

			var store = new Mock<IOfflineMapPackStore>();
			store.SetupGet(s => s.IsConfigured).Returns(true);

			_routePlans = new List<RoutePlan>();
			var routes = new Mock<IRouteService>();
			routes.Setup(r => r.GetRoutePlansForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(() => _routePlans);

			_activeCalls = new List<Call>();
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetActiveCallsByDepartmentAsync(It.IsAny<int>())).ReturnsAsync(() => _activeCalls);

			_service = new OfflineMapPackService(_packs.Object, store.Object, _settings.Object, new Mock<IDepartmentsService>().Object,
				new Mock<IDepartmentGroupsService>().Object, new Mock<IGeoService>().Object, new Mock<IFeatureToggleService>().Object, _customMaps.Object,
				_renderer.Object, _mapping.Object, units.Object, calls.Object, new Mock<ITypeIconsService>().Object, routes.Object);
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.OfflineBasemapArchiveUrl = _archive;
		}

		// About 49,000 km², over the 25,000 km² any area may cover: refused whenever it is measured.
		private const string OversizedArea = "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[2,0],[2,2],[0,2],[0,0]]]}";
		private const string SmallArea = "{\"type\":\"Polygon\",\"coordinates\":[[[-119.8,39.5],[-119.7,39.5],[-119.7,39.6],[-119.8,39.6],[-119.8,39.5]]]}";

		[Test]
		public async Task Saving_an_unchanged_area_keeps_the_zoom_it_was_accepted_at_without_measuring_it_again()
		{
			// Accepted earlier; a new archive (or limit) would now refuse it, which must not block the other settings.
			_config = new OfflineMapPacksConfig { ResponseAreaGeoJson = OversizedArea, AcceptedMaxZoom = 13 };

			var estimate = await _service.SaveConfigAsync(_departmentId, new OfflineMapPacksConfig { ResponseAreaGeoJson = OversizedArea + " ", RefreshHours = 12 });

			estimate.Valid.Should().BeTrue();
			estimate.Fits.Should().BeTrue();
			estimate.MaxZoom.Should().Be(13);
			_settings.Verify(s => s.SetOfflineMapPacksConfigAsync(_departmentId,
				It.Is<OfflineMapPacksConfig>(c => c.AcceptedMaxZoom == 13 && c.RefreshHours == 12), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_changed_area_is_measured_and_refused_when_too_large()
		{
			_config = new OfflineMapPacksConfig { ResponseAreaGeoJson = SmallArea, AcceptedMaxZoom = 15 };

			var estimate = await _service.SaveConfigAsync(_departmentId, new OfflineMapPacksConfig { ResponseAreaGeoJson = OversizedArea });

			estimate.Valid.Should().BeFalse();
			estimate.Error.Should().Be("too-large-area");
			_settings.Verify(s => s.SetOfflineMapPacksConfigAsync(It.IsAny<int>(), It.IsAny<OfflineMapPacksConfig>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task the_manifest_queues_custom_map_and_sprite_packs_and_lists_floors_and_data()
		{
			var manifest = await _service.GetManifestAsync(_departmentId);

			manifest.ManifestVersion.Should().StartWith("v1#");
			manifest.Policy.WifiOnly.Should().BeTrue();
			manifest.Policy.RefreshHours.Should().Be(24);

			manifest.Packs.Should().ContainSingle(p => p.Kind == (int)OfflineMapPackKind.CustomMapRaster && p.CustomMapLayerId == "geo-layer" && p.Status == (int)OfflineMapPackStatus.Queued);
			manifest.Packs.Should().OnlyContain(p => p.DownloadUrl == null);
			_saved.Should().Contain(p => p.Kind == (int)OfflineMapPackKind.Sprite && p.DepartmentId == _departmentId);

			// No archive configured: no base map cells.
			manifest.Packs.Should().NotContain(p => p.Kind == (int)OfflineMapPackKind.Basemap);

			var floor = manifest.IndoorFloors.Single();
			floor.FloorId.Should().Be("floor-1");
			floor.IsCustomMapLayer.Should().BeFalse();
			floor.Corners.Should().HaveCount(4);
			floor.Corners[0].Should().Equal(-119.8, 39.6);

			manifest.DepartmentData.Sha256.Should().HaveLength(64);
		}

		[Test]
		public async Task a_second_manifest_reuses_the_queued_rows()
		{
			await _service.GetManifestAsync(_departmentId);
			var count = _saved.Count;
			_service.InvalidateDepartment(_departmentId);

			await _service.GetManifestAsync(_departmentId);

			_saved.Should().HaveCount(count);
		}

		[Test]
		public async Task the_department_data_bundle_is_gzip_geojson_by_section_and_honours_exclusions()
		{
			var data = await _service.GetDepartmentDataAsync(_departmentId);

			var sections = Unzip(data.Content);
			sections["pois"]["features"][0]["properties"]["name"].ToString().Should().Be("Hydrant");
			sections["pois"]["features"][0]["properties"]["iconKey"].ToString().Should().Be("poi-icon");
			sections["customMapRegions"]["features"][0]["properties"]["name"].ToString().Should().Be("Bay");
			sections["stations"]["type"].ToString().Should().Be("FeatureCollection");

			_config = new OfflineMapPacksConfig { ExcludePois = true, ExcludeCustomMaps = true };
			_service.InvalidateDepartment(_departmentId);

			var trimmed = Unzip((await _service.GetDepartmentDataAsync(_departmentId)).Content);
			trimmed.ContainsKey("pois").Should().BeFalse();
			trimmed.ContainsKey("customMapRegions").Should().BeFalse();
			trimmed.ContainsKey("indoorZones").Should().BeTrue();
		}

		[Test]
		public async Task excluded_custom_maps_leave_the_manifest()
		{
			_config = new OfflineMapPacksConfig { ExcludeCustomMaps = true, ExcludeIndoorMaps = true };

			var manifest = await _service.GetManifestAsync(_departmentId);

			manifest.Packs.Should().NotContain(p => p.Kind == (int)OfflineMapPackKind.CustomMapRaster);
			manifest.IndoorFloors.Should().BeEmpty();
		}

		// Both calls fall in z10 cell 173/385 (lon -119.18..-118.83), far from SmallArea; the route runs on into cell 174.
		private static Call ActiveCall(int callId, string latLon) => new Call { CallId = callId, Number = $"26-{callId}", GeoLocationData = latLon };

		private static RoutePlan ActivePlan(string routePlanId, string lineGeoJson) =>
			new RoutePlan { RoutePlanId = routePlanId, Name = "Hydrant loop", RouteStatus = (int)RouteStatus.Active, MapboxRouteGeometry = lineGeoJson };

		private const string PlanThroughBothCells = "{\"type\":\"LineString\",\"coordinates\":[[-119.05,40.5],[-118.6,40.5]]}";

		[Test]
		public async Task incident_and_corridor_packs_list_every_call_and_route_plan_that_touches_their_cell()
		{
			MappingConfig.OfflineBasemapArchiveUrl = "basemap.pmtiles";
			_config = new OfflineMapPacksConfig { ResponseAreaGeoJson = SmallArea, AcceptedMaxZoom = 14 };
			_activeCalls = new List<Call> { ActiveCall(12, "40.51,-119.01"), ActiveCall(11, "40.50,-119.00") };
			_routePlans = new List<RoutePlan> { ActivePlan("rp-1", PlanThroughBothCells) };

			var manifest = await _service.GetManifestAsync(_departmentId);

			// The shared cell is named and kinded by the first call, and lists both calls and the route plan.
			var incident = manifest.Packs.Single(p => p.PackKey.StartsWith("basemap:z10/173/385:"));
			incident.Kind.Should().Be((int)OfflineMapPackKind.IncidentDetail);
			incident.Name.Should().Be("Call 26-12");
			incident.CallIds.Should().Equal(11, 12);
			incident.RoutePlanIds.Should().Equal("rp-1");

			var corridor = manifest.Packs.Single(p => p.PackKey.StartsWith("basemap:z10/174/385:"));
			corridor.Kind.Should().Be((int)OfflineMapPackKind.RouteCorridor);
			corridor.CallIds.Should().BeEmpty();
			corridor.RoutePlanIds.Should().Equal("rp-1");

			// Response area cells, the overview and custom maps carry no ids.
			manifest.Packs.Where(p => p.Kind != (int)OfflineMapPackKind.IncidentDetail && p.Kind != (int)OfflineMapPackKind.RouteCorridor)
				.Should().NotBeEmpty().And.OnlyContain(p => p.CallIds == null && p.RoutePlanIds == null);

			// The rows stay shared base-map cells: the ids live only in the department's manifest.
			_saved.Where(p => p.PackKey == incident.PackKey || p.PackKey == corridor.PackKey)
				.Should().HaveCount(2).And.OnlyContain(p => p.Kind == (int)OfflineMapPackKind.Basemap && p.DepartmentId == null);
		}

		[Test]
		public async Task a_new_call_on_an_already_listed_cell_changes_the_manifest_version()
		{
			MappingConfig.OfflineBasemapArchiveUrl = "basemap.pmtiles";
			_config = new OfflineMapPacksConfig { ResponseAreaGeoJson = SmallArea, AcceptedMaxZoom = 14 };
			_activeCalls = new List<Call> { ActiveCall(11, "40.50,-119.00") };

			var first = await _service.GetManifestAsync(_departmentId);

			_activeCalls.Add(ActiveCall(12, "40.51,-119.01"));
			_service.InvalidateDepartment(_departmentId);
			var second = await _service.GetManifestAsync(_departmentId);

			second.Packs.Select(p => p.PackKey).Should().BeEquivalentTo(first.Packs.Select(p => p.PackKey));
			second.ManifestVersion.Should().NotBe(first.ManifestVersion);
		}

		private static JObject Unzip(byte[] gzip)
		{
			using var input = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress);
			using var reader = new StreamReader(input, Encoding.UTF8);
			return JObject.Parse(reader.ReadToEnd());
		}
	}
}
