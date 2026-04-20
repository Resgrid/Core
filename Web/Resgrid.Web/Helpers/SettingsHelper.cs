using CommonServiceLocator;
using Microsoft.AspNetCore.Http;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
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
