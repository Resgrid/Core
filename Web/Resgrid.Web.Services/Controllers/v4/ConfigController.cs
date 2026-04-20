using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Configs;
using Resgrid.Config;
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

		public ConfigController(IDepartmentSettingsService departmentSettingsService)
		{
			_departmentSettingsService = departmentSettingsService;
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

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		private static int GetCurrentDepartmentId()
		{
			var principal = ClaimsAuthorizationHelper.GetClaimsPrincipal();

			if (principal?.Identity != null && principal.Identity.IsAuthenticated)
				return ClaimsAuthorizationHelper.GetDepartmentId();

			return 0;
		}
	}
}
