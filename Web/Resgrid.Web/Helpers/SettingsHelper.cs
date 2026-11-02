using CommonServiceLocator;
using Microsoft.AspNetCore.Http;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
	/// <summary>What rg-map needs to render with MapLibre (empty when the department has not been switched over).</summary>
	public class MapLibreWebConfig
	{
		public string Renderer { get; set; } = string.Empty;
		public string StyleUrlDay { get; set; } = string.Empty;
		public string StyleUrlNight { get; set; } = string.Empty;

		/// <summary>Live traffic vector tiles for the MapLibre maps' traffic toggle (plan §11.1); empty when the install has none.</summary>
		public string TrafficTileUrl { get; set; } = string.Empty;

		public int TrafficRefreshSeconds { get; set; }
	}

	public static class SettingsHelper
	{
		private const string DepartmentMapConfigCacheKeyFormat = "DepartmentMapConfig_{0}";
		private static IDepartmentSettingsService _departmentSettingsService;
		private static IHttpContextAccessor _httpContextAccessor;

		private static IDepartmentSettingsService GetSettingsService()
		{
			if (_departmentSettingsService == null)
				_departmentSettingsService = ServiceLocator.Current.GetInstance<IDepartmentSettingsService>();

			return _departmentSettingsService;
		}

		private static IHttpContextAccessor GetHttpContextAccessor()
		{
			if (_httpContextAccessor == null)
				_httpContextAccessor = ServiceLocator.Current.GetInstance<IHttpContextAccessor>();

			return _httpContextAccessor;
		}

		private static DepartmentModuleSettings GetModuleSettings()
		{
			var settings = GetSettingsService().GetDepartmentModuleSettingsAsync(ClaimsAuthorizationHelper.GetDepartmentId());

			if (settings != null)
				return settings.Result;

			return new DepartmentModuleSettings();
		}

		public static bool IsMessagingEnabled()
		{
			return !GetModuleSettings().MessagingDisabled;
		}

		public static bool IsMappingEnabled()
		{
			return !GetModuleSettings().MappingDisabled;
		}

		public static bool IsShiftsEnabled()
		{
			return !GetModuleSettings().ShiftsDisabled;
		}

		public static bool IsLogsEnabled()
		{
			return !GetModuleSettings().LogsDisabled;
		}

		public static bool IsReportsEnabled()
		{
			return !GetModuleSettings().ReportsDisabled;
		}

		public static bool IsDocumentsEnabled()
		{
			return !GetModuleSettings().DocumentsDisabled;
		}

		public static bool IsCalendarEnabled()
		{
			return !GetModuleSettings().CalendarDisabled;
		}

		public static bool IsNotesEnabled()
		{
			return !GetModuleSettings().NotesDisabled;
		}

		public static bool IsTrainingEnabled()
		{
			return !GetModuleSettings().TrainingDisabled;
		}

		public static bool IsInventoryEnabled()
		{
			return !GetModuleSettings().InventoryDisabled;
		}

		public static bool IsMaintenanceEnabled()
		{
			return !GetModuleSettings().MaintenanceDisabled;
		}

		/// <summary>Business Operations module switch (Workforce &amp; Business Operations plan, decision 42); the add-on entitlement is checked separately.</summary>
		public static bool IsBusinessOperationsEnabled()
		{
			return !GetModuleSettings().BusinessOperationsDisabled;
		}

		/// <summary>
		/// The MapLibre web renderer for the department (maplibre-mapping-routing-navigation-plan.md §7): "maplibre" and the
		/// signed day/night style URLs (API Mapping/GetStyle) when the Maps.MapLibreWeb flag is on, else empty values, which
		/// leave rg-map on its Mapbox or Leaflet renderer exactly as before.
		/// </summary>
		public static MapLibreWebConfig GetMapLibreWebConfig(string key = null)
		{
			var requestedKey = string.IsNullOrWhiteSpace(key) ? InfoConfig.WebsiteKey : key;
			var httpContext = GetHttpContextAccessor().HttpContext;
			var cacheKey = "MapLibreWebConfig_" + requestedKey;

			if (httpContext?.Items != null && httpContext.Items.ContainsKey(cacheKey))
				return httpContext.Items[cacheKey] as MapLibreWebConfig;

			var config = new MapLibreWebConfig();
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();

			try
			{
				var flags = ServiceLocator.Current.GetInstance<IFeatureToggleService>();
				if (departmentId > 0 && flags.IsEnabledAsync(FeatureFlagKeys.MapsMapLibreWeb, departmentId).GetAwaiter().GetResult())
				{
					var styles = ServiceLocator.Current.GetInstance<IMapStyleService>();
					config.Renderer = "maplibre";
					config.StyleUrlDay = styles.GetSignedStyleUrl(departmentId, requestedKey, false);
					config.StyleUrlNight = styles.GetSignedStyleUrl(departmentId, requestedKey, true);
					config.TrafficTileUrl = styles.GetTrafficTileUrl() ?? string.Empty;
					config.TrafficRefreshSeconds = MappingConfig.TrafficRefreshSeconds;
				}
			}
			catch (System.Exception ex)
			{
				// The page keeps its current renderer.
				Framework.Logging.LogException(ex, "Resolving the MapLibre web map config");
			}

			if (httpContext?.Items != null)
				httpContext.Items[cacheKey] = config;

			return config;
		}

		public static ResolvedMapConfig GetDepartmentMapConfig(string key = null)
		{
			var requestedKey = string.IsNullOrWhiteSpace(key) ? InfoConfig.WebsiteKey : key;
			var httpContext = GetHttpContextAccessor().HttpContext;
			var cacheKey = string.Format(DepartmentMapConfigCacheKeyFormat, requestedKey);

			if (httpContext?.Items != null && httpContext.Items.ContainsKey(cacheKey))
				return httpContext.Items[cacheKey] as ResolvedMapConfig;

			var mapConfig = GetSettingsService().GetMapConfigForDepartmentAsync(ClaimsAuthorizationHelper.GetDepartmentId(), requestedKey).GetAwaiter().GetResult();

			if (httpContext?.Items != null)
				httpContext.Items[cacheKey] = mapConfig;

			return mapConfig;
		}
	}
}
