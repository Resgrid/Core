using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Departments;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Mapping Settings, navigation defaults (setting 122) and offline maps (setting 123): the form's positive wording is
	/// stored inverted, numbers are clamped, and a response area over the download limit refuses the whole save.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class MappingSettingsSaveTests
	{
		private const int DepartmentId = 12;
		private const string UserId = "mapping-admin";

		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IOfflineMapPackService> _offline;
		private OfflineMapPacksConfig _storedOffline;
		private Mock<IStaticMapService> _staticMaps;
		private Mock<IMapStyleService> _styles;
		private DepartmentController _controller;

		[SetUp]
		public void SetUp()
		{
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = UserId });

			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserModifyDepartmentAsync(UserId, DepartmentId)).ReturnsAsync(true);

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetUnitStatusThresholdsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new UnitStatusThresholds());

			_storedOffline = new OfflineMapPacksConfig();
			_offline = new Mock<IOfflineMapPackService>();
			_offline.Setup(o => o.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _storedOffline);
			_offline.Setup(o => o.SaveConfigAsync(DepartmentId, It.IsAny<OfflineMapPacksConfig>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new OfflineResponseAreaEstimate { Valid = true, Fits = true });

			_staticMaps = new Mock<IStaticMapService>();
			_staticMaps.Setup(s => s.GetSignedTemplate(DepartmentId, It.IsAny<string>()))
				.Returns("https://api.test/api/v4/Mapping/GetStaticMap?key=web&lat={lat}&lon={lon}&zoom={zoom}&width={width}&height={height}&scale={scale}&scheme={scheme}&d=12&e=1&sig=s");
			_styles = new Mock<IMapStyleService>();
			_styles.Setup(s => s.GetPresetTileProvider()).Returns("selfhosted");
			_styles.Setup(s => s.GetAvailablePresets()).Returns(new[] { MapStyleTypes.Streets, MapStyleTypes.Light, MapStyleTypes.Dark });

			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Department.Department>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			_controller = new DepartmentController(departments.Object, Mock.Of<IUsersService>(), Mock.Of<IActionLogsService>(), Mock.Of<IEmailService>(),
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IDeleteService>(), Mock.Of<IInvitesService>(), authorization.Object,
				Mock.Of<IAddressService>(), Mock.Of<ISubscriptionsService>(), Mock.Of<ILimitsService>(), Mock.Of<ICallsService>(), _settings.Object, Mock.Of<IUnitsService>(),
				Mock.Of<ICertificationService>(), Mock.Of<INumbersService>(), Mock.Of<IScheduledTasksService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IEventAggregator>(), Mock.Of<ICustomStateService>(), Mock.Of<ICqrsProvider>(), Mock.Of<IPrinterProvider>(), Mock.Of<IQueueService>(),
				Mock.Of<IDocumentsService>(), Mock.Of<INotesService>(), Mock.Of<IContactsService>(), Mock.Of<ICheckInTimerService>(), Mock.Of<ISecurityPinService>(),
				Mock.Of<IRunCardsService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<IDepartmentProfileMediaService>(),
				localizer.Object, Mock.Of<Resgrid.Model.AiDispatch.IAiDispatchEnrichmentService>(),
				Mock.Of<IProtectedReadService>(), Mock.Of<ICallNumberingService>(), Mock.Of<IDocumentNumberingService>(), _offline.Object, _staticMaps.Object, _styles.Object, Mock.Of<IRoutingService>(), Mock.Of<IDepartmentMapProvidersService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public async Task Navigation_defaults_are_stored_inverted_with_avoid_flags_and_clamped_numbers()
		{
			var model = new MappingSettingsView
			{
				NavigationVoiceGuidance = false,
				NavigationDistanceUnits = NavigationDistanceUnits.Metric,
				NavigationArrivalRadiusMeters = 5000,
				NavigationOnScenePrompt = true,
				NavigationAvoidTolls = true,
				NavigationAvoidFerries = true,
				RouteTimeDeviations = false,
				RouteDeviationThresholdMinutes = 0
			};

			var result = await _controller.MappingSettings(model, CancellationToken.None);

			((MappingSettingsView)((ViewResult)result).Model).SaveSuccess.Should().BeTrue();
			_settings.Verify(s => s.SetMappingNavigationConfigAsync(DepartmentId, It.Is<MappingNavigationConfig>(c =>
				c.VoiceGuidanceOff && !c.OnScenePromptOff && c.DistanceUnits == (int)NavigationDistanceUnits.Metric &&
				c.ArrivalRadiusMeters == 1000 && c.DeviationThresholdMinutes == 1 && c.TimeDeviationsOff &&
				c.DefaultAvoidOptions == (int)(RouteAvoidOptions.Tolls | RouteAvoidOptions.Ferries)), It.IsAny<CancellationToken>()), Times.Once);
			model.NavigationArrivalRadiusMeters.Should().Be(1000, "the saved page shows what was stored");
		}

		[Test]
		public async Task Offline_settings_are_saved_over_the_stored_value_so_the_accepted_zoom_survives()
		{
			_storedOffline = new OfflineMapPacksConfig { ResponseAreaGeoJson = "{\"type\":\"Polygon\"}", AcceptedMaxZoom = 14 };
			var model = new MappingSettingsView
			{
				OfflineResponseAreaGeoJson = "  {\"type\":\"Polygon\"}  ",
				OfflineIncludeCustomMaps = true,
				OfflineIncludeIndoorMaps = false,
				OfflineIncludeLayers = true,
				OfflineIncludePois = false,
				OfflineIncludeHydrants = true,
				OfflineDownloadPolicy = OfflineDownloadPolicy.Manual,
				OfflineIncidentPacks = false,
				OfflineIncidentRadiusKm = 99,
				OfflineCorridorPacks = true,
				OfflineCorridorBufferMeters = 10,
				OfflineRefreshHours = 6,
				OfflineStaleDays = 3,
				OfflinePreferLocalInArea = true
			};

			await _controller.MappingSettings(model, CancellationToken.None);

			_offline.Verify(o => o.SaveConfigAsync(DepartmentId, It.Is<OfflineMapPacksConfig>(c =>
				c.ResponseAreaGeoJson == "{\"type\":\"Polygon\"}" && c.AcceptedMaxZoom == 14 &&
				!c.ExcludeCustomMaps && c.ExcludeIndoorMaps && !c.ExcludeLayers && c.ExcludePois && !c.ExcludeHydrants &&
				c.DownloadPolicy == (int)OfflineDownloadPolicy.Manual && c.IncidentPacksOff && c.IncidentRadiusKm == 25 &&
				!c.CorridorPacksOff && c.CorridorBufferMeters == 50 && c.RefreshHours == 6 && c.StaleDays == 3 && c.PreferLocalInArea),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_response_area_over_the_limit_refuses_the_whole_save()
		{
			_offline.Setup(o => o.SaveConfigAsync(DepartmentId, It.IsAny<OfflineMapPacksConfig>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new OfflineResponseAreaEstimate { Valid = true, Fits = false, Error = "too-large", SizeBytes = 900L * 1024 * 1024, MaxBytes = 500L * 1024 * 1024 });

			var result = await _controller.MappingSettings(new MappingSettingsView { OfflineResponseAreaGeoJson = "{\"type\":\"Polygon\"}" }, CancellationToken.None);

			((MappingSettingsView)((ViewResult)result).Model).SaveSuccess.Should().BeFalse();
			_controller.ModelState[nameof(MappingSettingsView.OfflineResponseAreaGeoJson)].Errors.Select(e => e.ErrorMessage).Should().Equal("OfflineAreaTooLarge");
			_settings.Verify(s => s.SetMappingNavigationConfigAsync(It.IsAny<int>(), It.IsAny<MappingNavigationConfig>(), It.IsAny<CancellationToken>()), Times.Never);
			_settings.Verify(s => s.SaveUnitStatusThresholdsAsync(It.IsAny<int>(), It.IsAny<UnitStatusThresholds>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task The_page_shows_stored_values_in_positive_wording_and_defaults_for_unset_numbers()
		{
			_settings.Setup(s => s.GetMappingNavigationConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new MappingNavigationConfig
			{
				VoiceGuidanceOff = true,
				DefaultAvoidOptions = (int)RouteAvoidOptions.Highways
			});
			_storedOffline = new OfflineMapPacksConfig { ExcludeHydrants = true, CorridorPacksOff = true, DownloadPolicy = (int)OfflineDownloadPolicy.AnyNetwork };

			var model = (MappingSettingsView)((ViewResult)await _controller.MappingSettings()).Model;

			model.NavigationVoiceGuidance.Should().BeFalse();
			model.NavigationOnScenePrompt.Should().BeTrue();
			model.NavigationAvoidHighways.Should().BeTrue();
			model.NavigationAvoidTolls.Should().BeFalse();
			model.NavigationArrivalRadiusMeters.Should().Be(MappingNavigationConfig.DefaultArrivalRadiusMeters);
			model.RouteDeviationThresholdMinutes.Should().Be(MappingNavigationConfig.DefaultDeviationThresholdMinutes);
			model.OfflineIncludeHydrants.Should().BeFalse();
			model.OfflineIncludePois.Should().BeTrue();
			model.OfflineCorridorPacks.Should().BeFalse();
			model.OfflineDownloadPolicy.Should().Be(OfflineDownloadPolicy.AnyNetwork);
			model.OfflineRefreshHours.Should().Be(OfflineMapPacksConfig.DefaultRefreshHours);
		}

		[Test]
		public async Task Style_pickers_list_what_the_install_can_render_and_preview_through_the_signed_static_map()
		{
			// No website Mapbox token on this install (static config other tests may have left set).
			var tokens = (Resgrid.Config.MappingConfig.WebsiteMapboxAccessToken, Resgrid.Config.MappingConfig.WebsiteMapboxKey, Resgrid.Config.MappingConfig.WebsiteOSMKey);
			Resgrid.Config.MappingConfig.WebsiteMapboxAccessToken = Resgrid.Config.MappingConfig.WebsiteMapboxKey = Resgrid.Config.MappingConfig.WebsiteOSMKey = "";
			MappingSettingsView model;
			try
			{
				model = (MappingSettingsView)((ViewResult)await _controller.MappingSettings()).Model;
			}
			finally
			{
				(Resgrid.Config.MappingConfig.WebsiteMapboxAccessToken, Resgrid.Config.MappingConfig.WebsiteMapboxKey, Resgrid.Config.MappingConfig.WebsiteOSMKey) = tokens;
			}

			model.BaseMapProvider.Should().Be("selfhosted");
			model.AvailableStyles.Should().Equal(MapStyleTypes.Streets, MapStyleTypes.Light, MapStyleTypes.Dark);
			model.PreviewUrlTemplate.Should().Contain("Mapping/GetStaticMap").And.Contain("sig=", "previews go through the signed static map, not a token in the page");
			model.RoutingProvider.Should().NotBeNullOrWhiteSpace();
			model.WebsiteSupportsMapStyle.Should().BeFalse("self-hosted styles reach the website only on MapLibre (flag off here)");
		}
	}
}
