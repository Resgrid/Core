using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.WeatherAlerts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Weather alert operations
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class WeatherAlertsController : V4AuthenticatedApiControllerbase
	{
		private readonly IWeatherAlertService _weatherAlertService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;

		public WeatherAlertsController(IWeatherAlertService weatherAlertService, IDepartmentSettingsService departmentSettingsService, IDepartmentsService departmentsService)
		{
			_weatherAlertService = weatherAlertService;
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
		}

		/// <summary>
		/// Gets all active weather alerts for the department
		/// </summary>
		[HttpGet("GetActiveAlerts")]
		[Authorize(Policy = ResgridResources.WeatherAlert_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetActiveWeatherAlertsResult>> GetActiveAlerts()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var alerts = await _weatherAlertService.GetActiveAlertsByDepartmentIdAsync(DepartmentId);
			var result = new GetActiveWeatherAlertsResult();

			foreach (var alert in alerts)
			{
				result.Data.Add(MapAlertToResultData(alert, department));
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets a single weather alert by id
		/// </summary>
		[HttpGet("GetWeatherAlert/{alertId}")]
		[Authorize(Policy = ResgridResources.WeatherAlert_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetWeatherAlertResult>> GetWeatherAlert(string alertId)
		{
			if (!Guid.TryParse(alertId, out var alertGuid))
				return BadRequest();

			var alert = await _weatherAlertService.GetAlertByIdAsync(alertGuid);
			if (alert == null || alert.DepartmentId != DepartmentId)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var result = new GetWeatherAlertResult();
			result.Data = MapAlertToResultData(alert, department);

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets historical weather alerts for a date range
		/// </summary>
		[HttpGet("GetAlertHistory")]
		[Authorize(Policy = ResgridResources.WeatherAlert_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetActiveWeatherAlertsResult>> GetAlertHistory([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
		{
			if (startDate > endDate)
				return BadRequest();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var alerts = await _weatherAlertService.GetAlertHistoryAsync(DepartmentId, startDate, endDate);
			var result = new GetActiveWeatherAlertsResult();

			foreach (var alert in alerts)
			{
				result.Data.Add(MapAlertToResultData(alert, department));
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets active weather alerts near a geographic location
		/// </summary>
		[HttpGet("GetAlertsNearLocation")]
		[Authorize(Policy = ResgridResources.WeatherAlert_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetActiveWeatherAlertsResult>> GetAlertsNearLocation([FromQuery] double lat, [FromQuery] double lng, [FromQuery] double radiusMiles = 25)
		{
			if (lat < -90 || lat > 90 || lng < -180 || lng > 180 || radiusMiles < 0 || radiusMiles > 500)
				return BadRequest();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var alerts = await _weatherAlertService.GetActiveAlertsNearLocationAsync(DepartmentId, lat, lng, radiusMiles);
			var result = new GetActiveWeatherAlertsResult();

			foreach (var alert in alerts)
			{
				result.Data.Add(MapAlertToResultData(alert, department));
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets all weather alert sources for the department
		/// </summary>
		[HttpGet("GetSources")]
		[Authorize(Policy = ResgridResources.WeatherAlert_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetWeatherAlertSourcesResult>> GetSources()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var sources = await _weatherAlertService.GetSourcesByDepartmentIdAsync(DepartmentId);
			var result = new GetWeatherAlertSourcesResult();

			foreach (var source in sources)
			{
				result.Data.Add(MapSourceToResultData(source, department));
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Creates or updates a weather alert source
		/// </summary>
		[HttpPost("SaveSource")]
		[Authorize(Policy = ResgridResources.WeatherAlert_Create)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetWeatherAlertSourcesResult>> SaveSource([FromBody] SaveWeatherAlertSourceInput input)
		{
			WeatherAlertSource source;

			if (!string.IsNullOrWhiteSpace(input.WeatherAlertSourceId))
			{
				if (!Guid.TryParse(input.WeatherAlertSourceId, out var sourceGuid))
					return BadRequest();

				if (!User.HasClaim(ResgridClaimTypes.Resources.WeatherAlert, ResgridClaimTypes.Actions.Update))
					return Forbid();

				source = await _weatherAlertService.GetSourceByIdAsync(sourceGuid);
				if (source == null || source.DepartmentId != DepartmentId)
					return NotFound();
			}
			else
			{
				source = new WeatherAlertSource
				{
					DepartmentId = DepartmentId,
					CreatedOn = DateTime.UtcNow,
					CreatedByUserId = UserId
				};
			}

			source.Name = input.Name;
			source.SourceType = input.SourceType;
			source.AreaFilter = NormalizeAreaFilter(input.AreaFilter);
			source.ApiKey = input.ApiKey;
			source.CustomEndpoint = ValidateCustomEndpoint(input.CustomEndpoint, input.SourceType);
			source.PollIntervalMinutes = Math.Max(input.PollIntervalMinutes, 15);
			source.Active = input.Active;

			await _weatherAlertService.SaveSourceAsync(source);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var sources = await _weatherAlertService.GetSourcesByDepartmentIdAsync(DepartmentId);
			var result = new GetWeatherAlertSourcesResult();

			foreach (var s in sources)
			{
				result.Data.Add(MapSourceToResultData(s, department));
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Deletes a weather alert source
		/// </summary>
		[HttpDelete("DeleteSource/{sourceId}")]
		[Authorize(Policy = ResgridResources.WeatherAlert_Delete)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<StandardApiResponseV4Base>> DeleteSource(string sourceId)
		{
			if (!Guid.TryParse(sourceId, out var sourceGuid))
				return BadRequest();

			var source = await _weatherAlertService.GetSourceByIdAsync(sourceGuid);
			if (source == null || source.DepartmentId != DepartmentId)
				return NotFound();

			await _weatherAlertService.DeleteSourceAsync(sourceGuid);

			var result = new StandardApiResponseV4Base();
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets all weather alert zones for the department
		/// </summary>
		[HttpGet("GetZones")]
		[Authorize(Policy = ResgridResources.WeatherAlert_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetWeatherAlertZonesResult>> GetZones()
		{
			var zones = await _weatherAlertService.GetZonesByDepartmentIdAsync(DepartmentId);
			var result = new GetWeatherAlertZonesResult();

			foreach (var zone in zones)
			{
				result.Data.Add(MapZoneToResultData(zone));
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Creates or updates a weather alert zone
		/// </summary>
		[HttpPost("SaveZone")]
		[Authorize(Policy = ResgridResources.WeatherAlert_Create)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetWeatherAlertZonesResult>> SaveZone([FromBody] SaveWeatherAlertZoneInput input)
		{
			WeatherAlertZone zone;

			if (!string.IsNullOrWhiteSpace(input.WeatherAlertZoneId))
			{
				if (!Guid.TryParse(input.WeatherAlertZoneId, out var zoneGuid))
					return BadRequest();

				if (!User.HasClaim(ResgridClaimTypes.Resources.WeatherAlert, ResgridClaimTypes.Actions.Update))
					return Forbid();

				zone = await _weatherAlertService.GetZoneByIdAsync(zoneGuid);
				if (zone == null || zone.DepartmentId != DepartmentId)
					return NotFound();
			}
			else
			{
				zone = new WeatherAlertZone
				{
					DepartmentId = DepartmentId,
					CreatedOn = DateTime.UtcNow
				};
			}

			zone.Name = input.Name;
			zone.ZoneCode = input.ZoneCode;
			zone.CenterGeoLocation = input.CenterGeoLocation;
			zone.RadiusMiles = input.RadiusMiles;
			zone.IsActive = input.IsActive;
			zone.IsPrimary = input.IsPrimary;

			await _weatherAlertService.SaveZoneAsync(zone);

			var zones = await _weatherAlertService.GetZonesByDepartmentIdAsync(DepartmentId);
			var result = new GetWeatherAlertZonesResult();

			foreach (var z in zones)
			{
				result.Data.Add(MapZoneToResultData(z));
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Deletes a weather alert zone
		/// </summary>
		[HttpDelete("DeleteZone/{zoneId}")]
		[Authorize(Policy = ResgridResources.WeatherAlert_Delete)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<StandardApiResponseV4Base>> DeleteZone(string zoneId)
		{
			if (!Guid.TryParse(zoneId, out var zoneGuid))
				return BadRequest();

			var zone = await _weatherAlertService.GetZoneByIdAsync(zoneGuid);
			if (zone == null || zone.DepartmentId != DepartmentId)
				return NotFound();

			await _weatherAlertService.DeleteZoneAsync(zoneGuid);

			var result = new StandardApiResponseV4Base();
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets weather alert settings for the department
		/// </summary>
		[HttpGet("GetSettings")]
		[Authorize(Policy = ResgridResources.WeatherAlert_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetWeatherAlertSettingsResult>> GetSettings()
		{
			var result = new GetWeatherAlertSettingsResult();
			result.Data = await GetWeatherAlertSettingsDataAsync();

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Saves weather alert settings for the department
		/// </summary>
		[HttpPost("SaveSettings")]
		[Authorize(Policy = ResgridResources.WeatherAlert_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetWeatherAlertSettingsResult>> SaveSettings([FromBody] SaveWeatherAlertSettingsInput input)
		{
			await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, input.WeatherAlertsEnabled.ToString(), DepartmentSettingTypes.WeatherAlertsEnabled);
			await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, input.MinimumSeverity.ToString(), DepartmentSettingTypes.WeatherAlertMinimumSeverity);
			await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, input.AutoMessageSeverity.ToString(), DepartmentSettingTypes.WeatherAlertAutoMessageSeverity);
			await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, input.CallIntegrationEnabled.ToString(), DepartmentSettingTypes.WeatherAlertCallIntegration);

			var result = new GetWeatherAlertSettingsResult();
			result.Data = new WeatherAlertSettingsData
			{
				WeatherAlertsEnabled = input.WeatherAlertsEnabled,
				MinimumSeverity = input.MinimumSeverity,
				AutoMessageSeverity = input.AutoMessageSeverity,
				CallIntegrationEnabled = input.CallIntegrationEnabled
			};

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		private async Task<WeatherAlertSettingsData> GetWeatherAlertSettingsDataAsync()
		{
			var settings = new WeatherAlertSettingsData();

			var enabledSetting = await _departmentSettingsService.GetSettingByTypeAsync(DepartmentId, DepartmentSettingTypes.WeatherAlertsEnabled);
			var minSeveritySetting = await _departmentSettingsService.GetSettingByTypeAsync(DepartmentId, DepartmentSettingTypes.WeatherAlertMinimumSeverity);
			var autoMsgSetting = await _departmentSettingsService.GetSettingByTypeAsync(DepartmentId, DepartmentSettingTypes.WeatherAlertAutoMessageSeverity);
			var callIntSetting = await _departmentSettingsService.GetSettingByTypeAsync(DepartmentId, DepartmentSettingTypes.WeatherAlertCallIntegration);

			if (enabledSetting != null && !string.IsNullOrWhiteSpace(enabledSetting.Setting))
				settings.WeatherAlertsEnabled = bool.TryParse(enabledSetting.Setting, out var enabled) && enabled;

			if (minSeveritySetting != null && !string.IsNullOrWhiteSpace(minSeveritySetting.Setting))
				settings.MinimumSeverity = int.TryParse(minSeveritySetting.Setting, out var minSev) ? minSev : 0;

			if (autoMsgSetting != null && !string.IsNullOrWhiteSpace(autoMsgSetting.Setting))
				settings.AutoMessageSeverity = int.TryParse(autoMsgSetting.Setting, out var autoSev) ? autoSev : 0;

			if (callIntSetting != null && !string.IsNullOrWhiteSpace(callIntSetting.Setting))
				settings.CallIntegrationEnabled = bool.TryParse(callIntSetting.Setting, out var callInt) && callInt;

			return settings;
		}

		private static WeatherAlertResultData MapAlertToResultData(WeatherAlert alert, Department department)
		{
			return new WeatherAlertResultData
			{
				WeatherAlertId = alert.WeatherAlertId.ToString(),
				DepartmentId = alert.DepartmentId,
				WeatherAlertSourceId = alert.WeatherAlertSourceId.ToString(),
				ExternalId = alert.ExternalId,
				Sender = alert.Sender,
				Event = alert.Event,
				AlertCategory = alert.AlertCategory,
				Severity = alert.Severity,
				Urgency = alert.Urgency,
				Certainty = alert.Certainty,
				Status = alert.Status,
				Headline = alert.Headline,
				Description = alert.Description,
				Instruction = alert.Instruction,
				AreaDescription = alert.AreaDescription,
				Polygon = alert.Polygon,
				Geocodes = alert.Geocodes,
				CenterGeoLocation = alert.CenterGeoLocation,
				OnsetUtc = alert.OnsetUtc?.TimeConverterToString(department),
				ExpiresUtc = alert.ExpiresUtc?.TimeConverterToString(department),
				EffectiveUtc = alert.EffectiveUtc.TimeConverterToString(department),
				SentUtc = alert.SentUtc?.TimeConverterToString(department),
				FirstSeenUtc = alert.FirstSeenUtc.TimeConverterToString(department),
				LastUpdatedUtc = alert.LastUpdatedUtc.TimeConverterToString(department),
				ReferencesExternalId = alert.ReferencesExternalId,
				NotificationSent = alert.NotificationSent,
				SystemMessageId = alert.SystemMessageId
			};
		}

		private static WeatherAlertSourceResultData MapSourceToResultData(WeatherAlertSource source, Department department)
		{
			return new WeatherAlertSourceResultData
			{
				WeatherAlertSourceId = source.WeatherAlertSourceId.ToString(),
				DepartmentId = source.DepartmentId,
				Name = source.Name,
				SourceType = source.SourceType,
				AreaFilter = FormatAreaFilterForDisplay(source.AreaFilter),
				HasApiKey = !string.IsNullOrEmpty(source.ApiKey),
				CustomEndpoint = source.CustomEndpoint,
				PollIntervalMinutes = source.PollIntervalMinutes,
				Active = source.Active,
				LastPollUtc = source.LastPollUtc?.TimeConverterToString(department),
				LastSuccessUtc = source.LastSuccessUtc?.TimeConverterToString(department),
				IsFailure = source.IsFailure,
				ErrorMessage = source.ErrorMessage
			};
		}

		private static WeatherAlertZoneResultData MapZoneToResultData(WeatherAlertZone zone)
		{
			return new WeatherAlertZoneResultData
			{
				WeatherAlertZoneId = zone.WeatherAlertZoneId.ToString(),
				DepartmentId = zone.DepartmentId,
				Name = zone.Name,
				ZoneCode = zone.ZoneCode,
				CenterGeoLocation = zone.CenterGeoLocation,
				RadiusMiles = zone.RadiusMiles,
				IsActive = zone.IsActive,
				IsPrimary = zone.IsPrimary
			};
		}

		/// <summary>
		/// Converts a comma-separated area filter string (e.g. "TX, WAZ021, WAZ022")
		/// into a JSON array (e.g. ["TX","WAZ021","WAZ022"]) for the weather provider.
		/// If the input is already valid JSON array, it is returned as-is.
		/// </summary>
		private static string NormalizeAreaFilter(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
				return null;

			var trimmed = input.Trim();

			// Already a JSON array — return as-is if valid, reject if malformed
			if (trimmed.StartsWith("["))
			{
				try
				{
					JsonSerializer.Deserialize<string[]>(trimmed);
					return trimmed;
				}
				catch
				{
					return null;
				}
			}

			// Comma-separated list — split, trim, remove empties, serialize to JSON array
			var codes = trimmed.Split(',')
				.Select(s => s.Trim())
				.Where(s => !string.IsNullOrEmpty(s))
				.ToArray();

			return codes.Length > 0 ? JsonSerializer.Serialize(codes) : null;
		}

		/// <summary>
		/// Converts a JSON array area filter back to a comma-separated string for display.
		/// </summary>
		private static string FormatAreaFilterForDisplay(string jsonArrayOrRaw)
		{
			if (string.IsNullOrWhiteSpace(jsonArrayOrRaw))
				return null;

			var trimmed = jsonArrayOrRaw.Trim();
			if (trimmed.StartsWith("["))
			{
				try
				{
					var codes = JsonSerializer.Deserialize<string[]>(trimmed);
					return codes != null ? string.Join(", ", codes) : trimmed;
				}
				catch { }
			}

			return trimmed;
		}

		/// <summary>
		/// Validates a custom endpoint URL to prevent SSRF. Enforces HTTPS and
		/// restricts the host to known weather API domains per source type.
		/// Returns null if the URL is empty, invalid, or not on the allowlist.
		/// </summary>
		private static string ValidateCustomEndpoint(string url, int sourceType)
		{
			if (string.IsNullOrWhiteSpace(url))
				return null;

			if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
				return null;

			if (uri.Scheme != Uri.UriSchemeHttps)
				return null;

			var allowedHosts = sourceType switch
			{
				0 => new[] { "api.weather.gov" },                            // NWS
				1 => new[] { "dd.weather.gc.ca", "dd.meteo.gc.ca" },         // Environment Canada
				2 => new[] { "feeds.meteoalarm.org", "meteoalarm.org" },     // MeteoAlarm
				_ => Array.Empty<string>()
			};

			var host = uri.Host.ToLowerInvariant();
			if (!Array.Exists(allowedHosts, h => host == h || host.EndsWith("." + h)))
				return null;

			return uri.GetLeftPart(UriPartial.Query);
		}
	}
}
