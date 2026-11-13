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

		/// <summary>Live traffic tiles for the MapLibre maps' traffic toggle (plan §11.1): the department's own or the install's; empty when neither.</summary>
		public string TrafficTileUrl { get; set; } = string.Empty;

		/// <summary>"vector" (Mapbox Traffic) or "raster" (a department's own traffic tile URL).</summary>
		public string TrafficTileType { get; set; } = string.Empty;

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

		/// <summary>The department's Records module switch; whether Records exists at all is the Records.System flag.</summary>
		public static bool IsRecordsEnabled()
		{
			return !GetModuleSettings().RecordsDisabled;
		}

		public static bool IsContactsEnabled()
		{
			return !GetModuleSettings().ContactsDisabled;
		}

		public static bool IsCertificationsEnabled()
		{
			return !GetModuleSettings().CertificationsDisabled;
		}

		/// <summary>The department's Deployments module switch; whether Deployments exists at all is the Operations.Deployments flag.</summary>
		public static bool IsDeploymentsEnabled()
		{
			return !GetModuleSettings().DeploymentsDisabled;
		}

		/// <summary>The department's Chat module switch; whether chat exists at all is the Chat.System flag.</summary>
		public static bool IsChatEnabled()
		{
			return !GetModuleSettings().ChatDisabled;
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
					var traffic = styles.GetTrafficLayerAsync(departmentId).GetAwaiter().GetResult();
					config.TrafficTileUrl = traffic?.Url ?? string.Empty;
					config.TrafficTileType = traffic?.Type ?? string.Empty;
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
