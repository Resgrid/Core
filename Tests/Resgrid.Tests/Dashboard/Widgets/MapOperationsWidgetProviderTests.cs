using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Awareness;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Awareness;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture, NonParallelizable]
	public class MapOperationsWidgetProviderTests
	{
		private string _websiteMapboxAccessToken;

		[SetUp]
		public void SetUp() => _websiteMapboxAccessToken = MappingConfig.WebsiteMapboxAccessToken;

		[TearDown]
		public void TearDown() => MappingConfig.WebsiteMapboxAccessToken = _websiteMapboxAccessToken;

		private static Mock<IDepartmentSettingsService> Settings(string center, int? zoom, bool overrideMap = false, MapStyleTypes day = MapStyleTypes.Automatic,
			MapStyleTypes night = MapStyleTypes.Automatic)
		{
			var settings = new Mock<IDepartmentSettingsService>(MockBehavior.Strict);
			settings.Setup(x => x.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId)).ReturnsAsync(center);
			settings.Setup(x => x.GetBigBoardMapZoomLevelForDepartmentAsync(DepartmentId)).ReturnsAsync(zoom);
			settings.Setup(x => x.GetMappingUseMapboxOverrideAsync(DepartmentId)).ReturnsAsync(overrideMap);
			settings.Setup(x => x.GetMappingMapStyleAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(day);
			settings.Setup(x => x.GetMappingMapStyleNightAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(night);
			return settings;
		}

		[Test]
		public async Task Points_the_widget_at_the_rg_map_endpoints_with_the_department_center_and_zoom()
		{
			var provider = new MapOperationsWidgetProvider(Settings("39.5, -119.8", 11).Object);

			var data = (MapOperationsWidgetData)await provider.GetDataAsync(For("map.operations"), CancellationToken.None);

			data.MarkersUrl.Should().Be("/api/v4/Mapping/GetMapDataAndMarkers");
			data.LayersUrl.Should().Be("/api/v4/Mapping/GetMayLayers?type=0");
			data.CenterLatitude.Should().Be(39.5);
			data.CenterLongitude.Should().Be(-119.8);
			data.ZoomLevel.Should().Be(11);
			data.ZoomFromSettings.Should().BeFalse();
			data.ShowCalls.Should().BeTrue();
			data.ShowPois.Should().BeFalse();
			data.NightMap.Should().BeNull("Automatic keeps the system tiles in both themes");
			data.StyleColors.Should().ContainKey(MapIconStyles.Responding).And.ContainKey(MapIconStyles.PriorityEmergency).And.ContainKey(MapIconStyles.Unknown);
		}

		[Test]
		public async Task A_zoom_setting_wins_and_unparseable_coordinates_leave_the_center_to_the_markers_endpoint()
		{
			var settings = Settings("not,coordinates", 11);
			var provider = new MapOperationsWidgetProvider(settings.Object);

			var data = (MapOperationsWidgetData)await provider.GetDataAsync(
				For("map.operations", CommsPlanningWidgetContexts.Settings(("zoom", 14), ("showUnits", false))), CancellationToken.None);

			data.ZoomLevel.Should().Be(14);
			data.ZoomFromSettings.Should().BeTrue();
			data.ShowUnits.Should().BeFalse();
			data.CenterLatitude.Should().BeNull();
			settings.Verify(x => x.GetBigBoardMapZoomLevelForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_department_style_with_a_different_night_pair_gets_a_night_map()
		{
			MappingConfig.WebsiteMapboxAccessToken = "pk.test-website-token";
			var provider = new MapOperationsWidgetProvider(Settings(null, null, day: MapStyleTypes.Streets).Object);

			var data = (MapOperationsWidgetData)await provider.GetDataAsync(For("map.operations"), CancellationToken.None);

			data.ZoomLevel.Should().Be(MapOperationsWidgetProvider.DefaultZoomLevel);
			data.NightMap.Should().NotBeNull();
			data.NightMap.StyleUrl.Should().Contain("dark");
		}

		[Test]
		public async Task A_department_mapbox_override_uses_one_style_in_both_themes()
		{
			MappingConfig.WebsiteMapboxAccessToken = "pk.test-website-token";
			var settings = Settings(null, 9, overrideMap: true, day: MapStyleTypes.Streets);
			var provider = new MapOperationsWidgetProvider(settings.Object);

			var data = (MapOperationsWidgetData)await provider.GetDataAsync(For("map.operations"), CancellationToken.None);

			data.NightMap.Should().BeNull();
			settings.Verify(x => x.GetMappingMapStyleAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}
	}
}
