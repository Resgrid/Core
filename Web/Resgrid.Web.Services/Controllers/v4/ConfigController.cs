using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Configs;
using Resgrid.Config;
using Resgrid.Model;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Generic configuration api endpoints
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ConfigController : ControllerBase
	{
		#region Members and Constructors
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IDepartmentsService _departmentsService;

		public ConfigController(IDepartmentSettingsService departmentSettingsService, IUserProfileService userProfileService,
			IFeatureToggleService featureToggleService, IDepartmentsService departmentsService)
		{
			_departmentSettingsService = departmentSettingsService;
			_userProfileService = userProfileService;
			_featureToggleService = featureToggleService;
			_departmentsService = departmentsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the system config
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetSystemConfig")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetSystemConfigResult>> GetSystemConfig()
		{
			var result = new GetSystemConfigResult();

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets the config values for a key
		/// </summary>
		/// <returns></returns>
		/// <param name="key">The key to get config data for</param>
		[HttpGet("GetConfig")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetConfigResult>> GetConfig(string key)
		{
			return await BuildConfigResultAsync(key, GetCurrentDepartmentId());
		}

		[HttpGet("GetDepartmentConfig")]
		[Authorize(AuthenticationSchemes = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetConfigResult>> GetDepartmentConfig(string key)
		{
			return await BuildConfigResultAsync(key, ClaimsAuthorizationHelper.GetDepartmentId());
		}

		private async Task<GetConfigResult> BuildConfigResultAsync(string key, int departmentId)
		{
			var result = new GetConfigResult();
			var mapConfig = await _departmentSettingsService.GetMapConfigForDepartmentAsync(departmentId, key);

			if (key == InfoConfig.DispatchAppKey)
			{
				result.Data.OpenWeatherApiKey = MappingConfig.DispatchOpenWeatherApiKey;

				// The Dispatch app geocodes addresses, what3words and plus codes on the new-call and
				// edit-call screens exactly like the Responder app does; without these it fell back to
				// "Google Maps API key not configured" on every lookup.
				result.Data.GoogleMapsKey = !string.IsNullOrWhiteSpace(MappingConfig.DispatchAppGoogleMapsKey)
					? MappingConfig.DispatchAppGoogleMapsKey
					: MappingConfig.GoogleMapsApiKey;
				result.Data.W3WKey = !string.IsNullOrWhiteSpace(MappingConfig.DispatchAppWhat3WordsKey)
					? MappingConfig.DispatchAppWhat3WordsKey
					: MappingConfig.What3WordsApiKey;
			}
			else if (key == InfoConfig.ResponderAppKey)
			{
				result.Data.GoogleMapsKey = MappingConfig.ResponderAppGoogleMapsKey;
				result.Data.W3WKey = MappingConfig.ResponderAppWhat3WordsKey;
			}
			else if (key == InfoConfig.UnitAppKey)
			{
				result.Data.NavigationMapKey = MappingConfig.UnitAppMapBoxKey;
				result.Data.GoogleMapsKey = MappingConfig.UnitAppGoogleMapsKey;
				result.Data.W3WKey = MappingConfig.UnitAppWhat3WordsKey;
			}
			else if (key == InfoConfig.BigBoardKey)
			{
				result.Data.OpenWeatherApiKey = MappingConfig.BigBoardOpenWeatherApiKey;
			}

			result.Data.MapUrl = mapConfig.TileUrl;
			result.Data.MapProvider = mapConfig.MapProvider;
			result.Data.MapStyleUrl = mapConfig.StyleUrl;
			result.Data.MapAccessToken = mapConfig.AccessToken;
			result.Data.MapAttribution = mapConfig.Attribution;
			result.Data.IsDepartmentMapOverride = mapConfig.IsDepartmentOverride;

			// Every client map -- new-call pickers, live maps, board maps -- opens here. Without it each
			// app fell back to its own hardcoded coordinates, which is how departments ended up staring
			// at the wrong continent.
			await PopulateMapCenterAsync(result, departmentId);
			await PopulateUnitStatusThresholdsAsync(result, departmentId);

			result.Data.EventingUrl = SystemBehaviorConfig.ResgridEventingBaseUrl;

			result.Data.PersonnelLocationStaleSeconds = MappingConfig.PersonnelLocationStaleSeconds;
			result.Data.UnitLocationStaleSeconds = MappingConfig.UnitLocationStaleSeconds;
			result.Data.PersonnelLocationMinMeters = MappingConfig.PersonnelLocationMinMeters;
			result.Data.UnitLocationMinMeters = MappingConfig.UnitLocationMinMeters;

			result.Data.NovuEnvironmentId = ChatConfig.NovuEnvironmentId;
			result.Data.NovuApplicationId = ChatConfig.NovuApplicationId;
			result.Data.NovuBackendApiUrl = ChatConfig.NovuBackendUrl;
			result.Data.NovuSocketUrl = ChatConfig.NovuSocketUrl;

			result.Data.AnalyticsApiKey = "";
			result.Data.AnalyticsHost = "";

			bool departmentModernApplicationSoundsEnabled = false;

			if (departmentId > 0)
			{
				try
				{
					departmentModernApplicationSoundsEnabled = await _departmentSettingsService.GetModernNotificationsEnabledAsync(departmentId);
				}
				catch (System.Exception ex)
				{
					Resgrid.Framework.Logging.LogException(ex,
						$"{nameof(BuildConfigResultAsync)}: {nameof(IDepartmentSettingsService.GetModernNotificationsEnabledAsync)} failed for departmentId {departmentId}.");
				}
			}

			bool userModernApplicationSoundsEnabled = false;
			var userId = GetCurrentUserId();

			if (!string.IsNullOrWhiteSpace(userId))
			{
				var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
				userModernApplicationSoundsEnabled = profile?.EnableModernApplicationSounds == true;
			}

			result.Data.EnableModernApplicationSounds = ModernApplicationSoundSettings.IsEnabled(
				departmentModernApplicationSoundsEnabled,
				userModernApplicationSoundsEnabled);

			if (departmentId > 0)
			{
				try
				{
					result.Data.DispatchRunCardsEnabled = await _featureToggleService.IsEnabledAsync(Resgrid.Model.FeatureFlagKeys.DispatchRunCards, departmentId);

					if (result.Data.DispatchRunCardsEnabled)
					{
						result.Data.DispatchRecommendationMode = (int)await _departmentSettingsService.GetDispatchRecommendationModeAsync(departmentId);
						result.Data.DispatchRecommendationAutoDispatch = await _departmentSettingsService.GetDispatchRecommendationAutoDispatchAsync(departmentId);
					}
				}
				catch (System.Exception ex)
				{
					// A settings/flag store failure must not break config bootstrap for the apps.
					Resgrid.Framework.Logging.LogException(ex,
						$"{nameof(BuildConfigResultAsync)}: run card dispatch settings lookup failed for departmentId {departmentId}.");
				}
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Loads the department's time-in-status thresholds for the board. Failure-safe: with no
		/// thresholds the board simply highlights nothing, which is the pre-feature behaviour.
		/// </summary>
		private async Task PopulateUnitStatusThresholdsAsync(GetConfigResult result, int departmentId)
		{
			if (departmentId <= 0)
				return;

			try
			{
				var thresholds = await _departmentSettingsService.GetUnitStatusThresholdsAsync(departmentId);

				foreach (var threshold in thresholds?.Thresholds ?? new System.Collections.Generic.List<Resgrid.Model.UnitStatusThreshold>())
				{
					result.Data.UnitStatusThresholds.Add(new UnitStatusThresholdData
					{
						BaseType = threshold.BaseType,
						WarnSeconds = threshold.WarnSeconds,
						AlertSeconds = threshold.AlertSeconds
					});
				}
			}
			catch (System.Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex,
					$"{nameof(PopulateUnitStatusThresholdsAsync)}: threshold lookup failed for departmentId {departmentId}.");
			}
		}

		/// <summary>
		/// Resolves the department's default map center. Always leaves the result populated: the
		/// settings service falls back from configured coordinates to the department address to a
		/// system default, and a lookup failure here must not break config bootstrap for the apps.
		/// </summary>
		private async Task PopulateMapCenterAsync(GetConfigResult result, int departmentId)
		{
			result.Data.MapCenterZoomLevel = 9;

			if (departmentId <= 0)
				return;

			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
				var coordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(department);

				if (coordinates?.Latitude != null && coordinates.Longitude != null)
				{
					result.Data.MapCenterLatitude = coordinates.Latitude.Value;
					result.Data.MapCenterLongitude = coordinates.Longitude.Value;
				}

				var zoomLevel = await _departmentSettingsService.GetBigBoardMapZoomLevelForDepartmentAsync(departmentId);

				if (zoomLevel.HasValue && zoomLevel.Value > 0)
					result.Data.MapCenterZoomLevel = zoomLevel.Value;
			}
			catch (System.Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex,
					$"{nameof(PopulateMapCenterAsync)}: map center lookup failed for departmentId {departmentId}.");
			}
		}

		private static int GetCurrentDepartmentId()
		{
			var principal = ClaimsAuthorizationHelper.GetClaimsPrincipal();

			if (principal?.Identity != null && principal.Identity.IsAuthenticated)
				return ClaimsAuthorizationHelper.GetDepartmentId();

			return 0;
		}

		private static string GetCurrentUserId()
		{
			var principal = ClaimsAuthorizationHelper.GetClaimsPrincipal();

			if (principal?.Identity != null && principal.Identity.IsAuthenticated)
				return ClaimsAuthorizationHelper.GetUserId();

			return string.Empty;
		}
	}
}
