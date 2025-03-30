using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Configs;
using Resgrid.Config;

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
		public ConfigController()
		{

		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the config values for a key
		/// </summary>
		/// <returns></returns>
		/// <param name="key">The key to get config data for</param>
		[HttpGet("GetConfig")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetConfigResult>> GetConfig(string key)
		{
			var result = new GetConfigResult();

			if (key == InfoConfig.WebsiteKey)
			{
				result.Data.MapUrl = MappingConfig.GetWebsiteOSMUrl();
			}
			else if (key == InfoConfig.ApiKey)
			{
				result.Data.MapUrl = MappingConfig.GetApiOSMUrl();
			}
			else if (key == InfoConfig.DispatchAppKey)
			{
				result.Data.MapUrl = MappingConfig.GetDispatchAppOSMUrl();
				result.Data.OpenWeatherApiKey = MappingConfig.DispatchOpenWeatherApiKey;
			}
			else if (key == InfoConfig.ResponderAppKey)
			{
				result.Data.MapUrl = MappingConfig.GetResponderAppOSMUrl();
				result.Data.GoogleMapsKey = MappingConfig.ResponderAppGoogleMapsKey;
				result.Data.W3WKey = MappingConfig.ResponderAppWhat3WordsKey;
			}
			else if (key == InfoConfig.UnitAppKey)
			{
				result.Data.MapUrl = MappingConfig.GetUnitAppOSMUrl();
				result.Data.NavigationMapKey = MappingConfig.UnitAppMapBoxKey;
				result.Data.GoogleMapsKey = MappingConfig.UnitAppGoogleMapsKey;
				result.Data.W3WKey = MappingConfig.UnitAppWhat3WordsKey;
			}
			else if (key == InfoConfig.BigBoardKey)
			{
				result.Data.MapUrl = MappingConfig.GetBigBoardAppOSMUrl();
				result.Data.OpenWeatherApiKey = MappingConfig.BigBoardOpenWeatherApiKey;
			}

			result.Data.MapAttribution = MappingConfig.LeafletAttribution;

			result.Data.PersonnelLocationStaleSeconds = MappingConfig.PersonnelLocationStaleSeconds;
			result.Data.UnitLocationStaleSeconds = MappingConfig.UnitLocationStaleSeconds;
			result.Data.PersonnelLocationMinMeters = MappingConfig.PersonnelLocationMinMeters;
			result.Data.UnitLocationMinMeters = MappingConfig.UnitLocationMinMeters;

			result.Data.NovuEnvironmentId = ChatConfig.NovuEnvironmentId;
			result.Data.NovuApplicationId = ChatConfig.NovuApplicationId;
			result.Data.NovuBackendApiUrl = ChatConfig.NovuBackendUrl;
			result.Data.NovuSocketUrl = ChatConfig.NovuSocketUrl;

			result.Data.PostHogApiKey = TelemetryConfig.PostHogApiKey;
			result.Data.PostHogHost = TelemetryConfig.PostHogUrl;

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}
	}
}
