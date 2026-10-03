using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class DepartmentSettingsServiceMapConfigTests
	{
		private Mock<IDepartmentSettingsRepository> _departmentSettingsRepository;
		private Mock<IAddressService> _addressService;
		private Mock<IGeoLocationProvider> _geoLocationProvider;
		private Mock<ICacheProvider> _cacheProvider;
		private DepartmentSettingsService _service;

		[SetUp]
		public void SetUp()
		{
			_departmentSettingsRepository = new Mock<IDepartmentSettingsRepository>();
			_addressService = new Mock<IAddressService>();
			_geoLocationProvider = new Mock<IGeoLocationProvider>();
			_cacheProvider = new Mock<ICacheProvider>();

			_service = new DepartmentSettingsService(
				_departmentSettingsRepository.Object,
				_addressService.Object,
				_geoLocationProvider.Object,
				_cacheProvider.Object);

			MappingConfig.MapBoxStyleUrl = "mapbox://styles/resgrid/abc123";
			MappingConfig.MapBoxTileUrl = "https://api.mapbox.com/styles/v1/resgrid/abc123/tiles/256/{{z}}/{{x}}/{{y}}?access_token={0}";
			MappingConfig.WebsiteOSMKey = "system-token";
			MappingConfig.WebsiteMapboxKey = string.Empty;
			MappingConfig.WebsiteMapboxAccessToken = string.Empty;
			MappingConfig.WebsiteMapMode = MappingConfig.LeafletMapProvider;
			MappingConfig.LeafletTileUrl = "https://tiles.example.com/{z}/{x}/{y}.png";
			MappingConfig.UnitAppMapBoxKey = string.Empty;
			MappingConfig.DispatchAppMapboxKey = string.Empty;
			MappingConfig.ResponderAppMapboxKey = string.Empty;
			MappingConfig.ICAppMapboxKey = string.Empty;
			MappingConfig.BigBoardMapboxKey = string.Empty;

			// Pass-through cache so the cached map style reads reach the repository mocks.
			_cacheProvider
				.Setup(x => x.RetrieveAsync<string>(It.IsAny<string>(), It.IsAny<Func<Task<string>>>(), It.IsAny<TimeSpan>()))
				.Returns((string key, Func<Task<string>> fallback, TimeSpan expiration) => fallback());
		}

		private void SetupMapStyles(MapStyleTypes? day, MapStyleTypes? night = null)
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapStyle))
				.ReturnsAsync(day.HasValue ? new DepartmentSetting { DepartmentId = 7, Setting = ((int)day.Value).ToString(), SettingType = (int)DepartmentSettingTypes.MappingMapStyle } : null);
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapStyleNight))
				.ReturnsAsync(night.HasValue ? new DepartmentSetting { DepartmentId = 7, Setting = ((int)night.Value).ToString(), SettingType = (int)DepartmentSettingTypes.MappingMapStyleNight } : null);
		}

		private void SetupOverride(string enabled, string styleUrl, string token)
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = enabled, SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxStyleUrl))
				.ReturnsAsync(styleUrl == null ? null : new DepartmentSetting { DepartmentId = 7, Setting = styleUrl, SettingType = (int)DepartmentSettingTypes.MappingMapboxStyleUrl });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxAccessToken))
				.ReturnsAsync(token == null ? null : new DepartmentSetting { DepartmentId = 7, Setting = token, SettingType = (int)DepartmentSettingTypes.MappingMapboxAccessToken });
		}

		[Test]
		public async Task app_map_should_hand_out_department_override_token_and_custom_style_day_and_night()
		{
			MappingConfig.ResponderAppMapboxKey = "pk.responder-system";
			SetupOverride("true", "https://api.mapbox.com/styles/v1/dept/custom123.html", " pk.department-token ");
			SetupMapStyles(MapStyleTypes.Satellite);

			var result = await _service.GetAppMapConfigForDepartmentAsync(7, InfoConfig.ResponderAppKey);

			result.IsDepartmentOverride.Should().BeTrue();
			result.AccessToken.Should().Be("pk.department-token");
			result.DayStyleUrl.Should().Be("mapbox://styles/dept/custom123");
			result.NightStyleUrl.Should().Be(result.DayStyleUrl);
		}

		[Test]
		public async Task app_map_should_never_hand_out_a_secret_override_token()
		{
			MappingConfig.UnitAppMapBoxKey = "pk.unit-system";
			SetupOverride("true", "mapbox://styles/dept/custom", "sk.department-secret");
			SetupMapStyles(MapStyleTypes.NavigationDay);

			var result = await _service.GetAppMapConfigForDepartmentAsync(7, InfoConfig.UnitAppKey);

			result.IsDepartmentOverride.Should().BeFalse();
			result.AccessToken.Should().Be("pk.unit-system");
			result.DayStyleUrl.Should().Be(MapStylePresets.NavigationDayStyleUrl);
			result.NightStyleUrl.Should().Be(MapStylePresets.NavigationNightStyleUrl);
		}

		[Test]
		public async Task app_map_should_ignore_override_token_without_a_usable_style()
		{
			SetupOverride("true", null, "pk.department-token");
			SetupMapStyles(null);

			var result = await _service.GetAppMapConfigForDepartmentAsync(7, InfoConfig.DispatchAppKey);

			result.IsDepartmentOverride.Should().BeFalse();
			result.AccessToken.Should().BeEmpty();
			result.DayStyleUrl.Should().Be(MapStylePresets.StreetsStyleUrl);
			result.NightStyleUrl.Should().Be(MapStylePresets.DarkStyleUrl);
		}

		[TestCase("ResponderAppKey")]
		[TestCase("UnitAppKey")]
		[TestCase("DispatchAppKey")]
		[TestCase("ICAppKey")]
		[TestCase("BigBoardKey")]
		public async Task app_map_should_hand_out_the_system_token_for_each_app(string key)
		{
			MappingConfig.ResponderAppMapboxKey = "pk.responder";
			MappingConfig.UnitAppMapBoxKey = "pk.unit";
			MappingConfig.DispatchAppMapboxKey = "pk.dispatch";
			MappingConfig.ICAppMapboxKey = "pk.ic";
			MappingConfig.BigBoardMapboxKey = "pk.bigboard";
			SetupMapStyles(null);

			var result = await _service.GetAppMapConfigForDepartmentAsync(7, key);

			var expected = key switch
			{
				"ResponderAppKey" => "pk.responder",
				"UnitAppKey" => "pk.unit",
				"DispatchAppKey" => "pk.dispatch",
				"ICAppKey" => "pk.ic",
				_ => "pk.bigboard"
			};
			result.AccessToken.Should().Be(expected);
		}

		[Test]
		public async Task app_map_should_send_no_token_when_the_system_token_is_secret_or_unknown_app()
		{
			MappingConfig.ICAppMapboxKey = "sk.ic-secret";
			SetupMapStyles(null);

			(await _service.GetAppMapConfigForDepartmentAsync(7, InfoConfig.ICAppKey)).AccessToken.Should().BeEmpty();
			(await _service.GetAppMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey)).AccessToken.Should().BeEmpty();
		}

		[Test]
		public async Task app_map_should_send_no_token_to_an_anonymous_caller()
		{
			MappingConfig.ResponderAppMapboxKey = "pk.responder";

			var result = await _service.GetAppMapConfigForDepartmentAsync(0, InfoConfig.ResponderAppKey);

			result.AccessToken.Should().BeEmpty();
			result.DayStyleUrl.Should().Be(MapStylePresets.StreetsStyleUrl);
			result.NightStyleUrl.Should().Be(MapStylePresets.DarkStyleUrl);
			_departmentSettingsRepository.Verify(x => x.GetDepartmentSettingByIdTypeAsync(It.IsAny<int>(), It.IsAny<DepartmentSettingTypes>()), Times.Never);
		}

		[Test]
		public async Task should_render_department_style_on_website_with_system_token_even_when_system_is_leaflet()
		{
			MappingConfig.WebsiteMapboxAccessToken = "pk.website-token";
			SetupMapStyles(MapStyleTypes.Satellite);

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.IsDepartmentOverride.Should().BeFalse();
			result.MapProvider.Should().Be(MappingConfig.MapboxMapProvider);
			result.StyleUrl.Should().Be(MapStylePresets.SatelliteStyleUrl);
			result.AccessToken.Should().Be("pk.website-token");
			result.TileUrl.Should().Be("https://api.mapbox.com/styles/v1/mapbox/satellite-v9/tiles/256/{z}/{x}/{y}@2x?access_token=pk.website-token");
		}

		[Test]
		public async Task should_replace_system_mapbox_style_with_department_style()
		{
			MappingConfig.WebsiteMapMode = MappingConfig.MapboxMapProvider;
			MappingConfig.WebsiteOSMKey = "pk.system-token";
			SetupMapStyles(MapStyleTypes.Outdoors);

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.StyleUrl.Should().Be(MapStylePresets.OutdoorsStyleUrl);
			result.AccessToken.Should().Be("pk.system-token");
		}

		[Test]
		public async Task should_keep_system_tiles_when_department_style_is_automatic()
		{
			MappingConfig.WebsiteMapboxAccessToken = "pk.website-token";
			SetupMapStyles(null);

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.MapProvider.Should().Be(MappingConfig.LeafletMapProvider);
			result.TileUrl.Should().Be("https://tiles.example.com/{z}/{x}/{y}.png");
		}

		[Test]
		public async Task should_keep_system_tiles_when_department_style_has_no_system_token()
		{
			MappingConfig.WebsiteOSMKey = "maptiler-key";
			SetupMapStyles(MapStyleTypes.Dark);

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.MapProvider.Should().Be(MappingConfig.LeafletMapProvider);
			result.AccessToken.Should().BeEmpty();
		}

		[Test]
		public async Task should_prefer_department_override_over_department_style()
		{
			MappingConfig.WebsiteMapboxAccessToken = "pk.website-token";
			SetupMapStyles(MapStyleTypes.Satellite);
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "true", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxStyleUrl))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "mapbox://styles/department/customstyle", SettingType = (int)DepartmentSettingTypes.MappingMapboxStyleUrl });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxAccessToken))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "pk.department-token", SettingType = (int)DepartmentSettingTypes.MappingMapboxAccessToken });

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.IsDepartmentOverride.Should().BeTrue();
			result.StyleUrl.Should().Be("mapbox://styles/department/customstyle");
		}

		[Test]
		public async Task should_render_department_style_for_unit_app_on_its_system_token()
		{
			MappingConfig.UnitAppMapBoxKey = "pk.unit-system-token";
			SetupMapStyles(MapStyleTypes.NavigationDay);

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.UnitAppKey);

			result.StyleUrl.Should().Be(MapStylePresets.NavigationDayStyleUrl);
			result.AccessToken.Should().Be("pk.unit-system-token");
		}

		[Test]
		public async Task should_read_unknown_stored_style_as_automatic()
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapStyle))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "99", SettingType = (int)DepartmentSettingTypes.MappingMapStyle });

			var result = await _service.GetMappingMapStyleAsync(7);

			result.Should().Be(MapStyleTypes.Automatic);
		}

		[Test]
		public async Task should_delete_rows_when_saving_automatic_styles()
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapStyle))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "5", SettingType = (int)DepartmentSettingTypes.MappingMapStyle });
			_departmentSettingsRepository
				.Setup(x => x.DeleteAsync(It.IsAny<DepartmentSetting>(), It.IsAny<System.Threading.CancellationToken>()))
				.ReturnsAsync(true);

			await _service.SaveMappingMapStylesAsync(7, MapStyleTypes.Automatic, (MapStyleTypes)42);

			_departmentSettingsRepository.Verify(x => x.DeleteAsync(It.Is<DepartmentSetting>(s => s.SettingType == (int)DepartmentSettingTypes.MappingMapStyle), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
			_departmentSettingsRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<System.Threading.CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task should_store_explicit_styles_as_integers()
		{
			await _service.SaveMappingMapStylesAsync(7, MapStyleTypes.SatelliteStreets, MapStyleTypes.NavigationNight);

			_departmentSettingsRepository.Verify(x => x.SaveOrUpdateAsync(It.Is<DepartmentSetting>(s => s.SettingType == (int)DepartmentSettingTypes.MappingMapStyle && s.Setting == "6"), It.IsAny<System.Threading.CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_departmentSettingsRepository.Verify(x => x.SaveOrUpdateAsync(It.Is<DepartmentSetting>(s => s.SettingType == (int)DepartmentSettingTypes.MappingMapStyleNight && s.Setting == "8"), It.IsAny<System.Threading.CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task should_return_department_override_when_enabled_and_valid()
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "true", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxStyleUrl))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "mapbox://styles/department/customstyle", SettingType = (int)DepartmentSettingTypes.MappingMapboxStyleUrl });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxAccessToken))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "pk.department-token", SettingType = (int)DepartmentSettingTypes.MappingMapboxAccessToken });

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.IsDepartmentOverride.Should().BeTrue();
			result.MapProvider.Should().Be(MappingConfig.MapboxMapProvider);
			result.StyleUrl.Should().Be("mapbox://styles/department/customstyle");
			result.AccessToken.Should().Be("pk.department-token");
			result.TileUrl.Should().Contain("pk.department-token");
		}

		[Test]
		public async Task should_return_department_override_for_unit_app_when_system_config_is_leaflet()
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "true", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxStyleUrl))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "mapbox://styles/department/mobileunit", SettingType = (int)DepartmentSettingTypes.MappingMapboxStyleUrl });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxAccessToken))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "pk.unit-token", SettingType = (int)DepartmentSettingTypes.MappingMapboxAccessToken });

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.UnitAppKey);

			result.IsDepartmentOverride.Should().BeTrue();
			result.MapProvider.Should().Be(MappingConfig.MapboxMapProvider);
			result.StyleUrl.Should().Be("mapbox://styles/department/mobileunit");
			result.AccessToken.Should().Be("pk.unit-token");
			result.TileUrl.Should().Contain("pk.unit-token");
		}

		[Test]
		public async Task should_return_department_override_for_responder_app_when_system_config_is_leaflet()
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "true", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxStyleUrl))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "mapbox://styles/department/mobileresponder", SettingType = (int)DepartmentSettingTypes.MappingMapboxStyleUrl });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxAccessToken))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "pk.responder-token", SettingType = (int)DepartmentSettingTypes.MappingMapboxAccessToken });

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.ResponderAppKey);

			result.IsDepartmentOverride.Should().BeTrue();
			result.MapProvider.Should().Be(MappingConfig.MapboxMapProvider);
			result.StyleUrl.Should().Be("mapbox://styles/department/mobileresponder");
			result.AccessToken.Should().Be("pk.responder-token");
			result.TileUrl.Should().Contain("pk.responder-token");
		}

		[Test]
		public async Task should_fall_back_to_leaflet_system_config_when_override_disabled()
		{
			MappingConfig.LeafletTileUrl = "https://tiles.example.com/{{z}}/{{x}}/{{y}}.png?key={0}";

			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "false", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.IsDepartmentOverride.Should().BeFalse();
			result.MapProvider.Should().Be(MappingConfig.LeafletMapProvider);
			result.TileUrl.Should().Be("https://tiles.example.com/{z}/{x}/{y}.png?key=system-token");
			result.AccessToken.Should().BeEmpty();
			result.StyleUrl.Should().BeEmpty();
		}

		[Test]
		public async Task should_fall_back_to_mapbox_system_config_when_website_mode_is_mapbox()
		{
			MappingConfig.WebsiteMapMode = MappingConfig.MapboxMapProvider;
			MappingConfig.WebsiteOSMKey = "pk.system-token";

			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "false", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.IsDepartmentOverride.Should().BeFalse();
			result.MapProvider.Should().Be(MappingConfig.MapboxMapProvider);
			result.AccessToken.Should().Be("pk.system-token");
			result.StyleUrl.Should().Be("mapbox://styles/resgrid/abc123");
		}

		[Test]
		public async Task should_use_dedicated_website_mapbox_access_token_when_configured()
		{
			MappingConfig.WebsiteMapMode = MappingConfig.MapboxMapProvider;
			MappingConfig.WebsiteMapboxAccessToken = "pk.website-token";
			MappingConfig.WebsiteOSMKey = string.Empty;

			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "false", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.MapProvider.Should().Be(MappingConfig.MapboxMapProvider);
			result.AccessToken.Should().Be("pk.website-token");
			result.StyleUrl.Should().Be("mapbox://styles/resgrid/abc123");
		}

		[Test]
		public async Task should_fall_back_to_leaflet_when_mapbox_mode_has_no_website_token()
		{
			MappingConfig.WebsiteMapMode = MappingConfig.MapboxMapProvider;
			MappingConfig.WebsiteOSMKey = string.Empty;

			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "false", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.MapProvider.Should().Be(MappingConfig.LeafletMapProvider);
			result.AccessToken.Should().BeEmpty();
			result.StyleUrl.Should().BeEmpty();
		}

		[Test]
		public async Task should_use_configured_mapbox_tile_url_when_style_url_is_missing()
		{
			MappingConfig.WebsiteMapMode = MappingConfig.MapboxMapProvider;
			MappingConfig.MapBoxStyleUrl = string.Empty;
			MappingConfig.WebsiteOSMKey = "pk.system-token";

			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "false", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.MapProvider.Should().Be(MappingConfig.MapboxMapProvider);
			result.TileUrl.Should().Be("https://api.mapbox.com/styles/v1/resgrid/abc123/tiles/256/{z}/{x}/{y}?access_token=pk.system-token");
			result.StyleUrl.Should().BeEmpty();
			result.AccessToken.Should().Be("pk.system-token");
		}

		[Test]
		public async Task should_fall_back_to_system_config_when_override_is_incomplete()
		{
			MappingConfig.WebsiteMapMode = MappingConfig.MapboxMapProvider;
			MappingConfig.WebsiteOSMKey = "pk.system-token";

			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "true", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxStyleUrl))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "mapbox://styles/department/customstyle", SettingType = (int)DepartmentSettingTypes.MappingMapboxStyleUrl });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxAccessToken))
				.ReturnsAsync((DepartmentSetting)null);

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.IsDepartmentOverride.Should().BeFalse();
			result.AccessToken.Should().Be("pk.system-token");
			result.StyleUrl.Should().Be("mapbox://styles/resgrid/abc123");
		}

		[Test]
		public async Task should_fall_back_to_leaflet_when_website_mapbox_access_token_is_private()
		{
			MappingConfig.WebsiteMapMode = MappingConfig.MapboxMapProvider;
			MappingConfig.WebsiteMapboxAccessToken = "sk.website-secret";
			MappingConfig.WebsiteOSMKey = string.Empty;

			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "false", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.MapProvider.Should().Be(MappingConfig.LeafletMapProvider);
			result.AccessToken.Should().BeEmpty();
			result.StyleUrl.Should().BeEmpty();
			result.TileUrl.Should().NotContain("sk.website-secret");
		}

		[Test]
		public async Task should_ignore_department_override_when_mapbox_access_token_is_private()
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingUseMapboxOverride))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "true", SettingType = (int)DepartmentSettingTypes.MappingUseMapboxOverride });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxStyleUrl))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "mapbox://styles/department/customstyle", SettingType = (int)DepartmentSettingTypes.MappingMapboxStyleUrl });
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.MappingMapboxAccessToken))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = 7, Setting = "sk.department-secret", SettingType = (int)DepartmentSettingTypes.MappingMapboxAccessToken });

			var result = await _service.GetMapConfigForDepartmentAsync(7, InfoConfig.WebsiteKey);

			result.IsDepartmentOverride.Should().BeFalse();
			result.MapProvider.Should().Be(MappingConfig.LeafletMapProvider);
			result.AccessToken.Should().BeEmpty();
			result.TileUrl.Should().NotContain("sk.department-secret");
		}
	}
}
