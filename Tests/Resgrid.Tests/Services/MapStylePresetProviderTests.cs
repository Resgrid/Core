using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Provider-aware style presets for the Mapping Settings pickers (maplibre-mapping-routing-navigation-plan.md §5.3, §6.6):
	/// a self-hosted install lists only what it can draw and previews through its own style server.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class MapStylePresetProviderTests
	{
		private string _tileProvider;
		private string _catalog;
		private string _day;
		private string _night;
		private string _websiteToken;
		private MapStyleService _service;

		[SetUp]
		public void SetUp()
		{
			_tileProvider = MappingConfig.TileProvider;
			_catalog = MappingConfig.SelfHostedStyleCatalog;
			_day = MappingConfig.SelfHostedStyleUrlDay;
			_night = MappingConfig.SelfHostedStyleUrlNight;
			_websiteToken = MappingConfig.WebsiteMapboxAccessToken;

			MappingConfig.TileProvider = MappingConfig.SelfHostedMapProvider;
			MappingConfig.SelfHostedStyleCatalog = "";
			MappingConfig.SelfHostedStyleUrlDay = "https://tiles.example.org/styles/osm-bright/style.json";
			MappingConfig.SelfHostedStyleUrlNight = "https://tiles.example.org/styles/dark-matter/style.json";

			_service = new MapStyleService(Mock.Of<IDepartmentSettingsService>(), Mock.Of<ICacheProvider>());
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.TileProvider = _tileProvider;
			MappingConfig.SelfHostedStyleCatalog = _catalog;
			MappingConfig.SelfHostedStyleUrlDay = _day;
			MappingConfig.SelfHostedStyleUrlNight = _night;
			MappingConfig.WebsiteMapboxAccessToken = _websiteToken;
		}

		[Test]
		public void A_self_hosted_install_lists_no_community_or_satellite_styles_without_a_catalog_entry()
		{
			var presets = _service.GetAvailablePresets();

			_service.GetPresetTileProvider().Should().Be(MappingConfig.SelfHostedMapProvider);
			presets.Should().Contain(new[] { MapStyleTypes.Streets, MapStyleTypes.Light, MapStyleTypes.Dark, MapStyleTypes.Outdoors });
			presets.Should().NotContain(MapStyleTypes.Satellite);
			presets.Should().NotContain(s => MapStylePresets.IsCommunityStyle(s));
		}

		[Test]
		public void A_satellite_catalog_entry_makes_satellite_selectable_and_is_its_preview_source()
		{
			MappingConfig.SelfHostedStyleCatalog = "{\"satellite\":\"https://tiles.example.org/styles/imagery/style.json\"}";

			_service.GetAvailablePresets().Should().Contain(MapStyleTypes.Satellite);
			_service.GetPresetSource(MapStyleTypes.Satellite, false).Should().Be("https://tiles.example.org/styles/imagery/style.json");
		}

		[Test]
		public void A_kind_without_a_catalog_entry_previews_as_the_install_default_for_its_slot()
		{
			_service.GetPresetSource(MapStyleTypes.Light, false).Should().Be(MappingConfig.SelfHostedStyleUrlDay);
			_service.GetPresetSource(MapStyleTypes.Light, true).Should().Be(MappingConfig.SelfHostedStyleUrlNight);
		}

		[Test]
		public void A_mapbox_install_lists_every_preset_with_its_mapbox_style()
		{
			MappingConfig.TileProvider = "auto";
			MappingConfig.WebsiteMapboxAccessToken = "pk.test-website-token";

			_service.GetPresetTileProvider().Should().Be(MappingConfig.MapboxMapProvider);

			_service.GetAvailablePresets().Should().BeEquivalentTo(MapStylePresets.SelectableStyles);
			_service.GetPresetSource(MapStyleTypes.Dark, true).Should().Be(MapStylePresets.DarkStyleUrl);
		}

		[TestCase("https://tiles.example.org/styles/osm-bright/style.json", 2,
			"https://tiles.example.org/styles/osm-bright/static/-119.75,39.5,11/600x240@2x.png")]
		[TestCase("https://tiles.example.org/maps/styles/basic/style.json?key=abc", 1,
			"https://tiles.example.org/maps/styles/basic/static/-119.75,39.5,11/600x240.png?key=abc")]
		[TestCase("https://tiles.example.org/basic.json", 1, null)]
		[TestCase("mapbox://styles/mapbox/streets-v12", 1, null)]
		[TestCase("", 1, null)]
		public void TileServer_GL_static_images_come_from_the_style_url(string source, int scale, string expected)
		{
			StaticMapService.TileServerStaticUrl(source, 39.5, -119.75, 11, 600, 240, scale).Should().Be(expected);
		}
	}
}
