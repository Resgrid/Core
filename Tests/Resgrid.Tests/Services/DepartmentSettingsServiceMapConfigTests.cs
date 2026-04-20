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
