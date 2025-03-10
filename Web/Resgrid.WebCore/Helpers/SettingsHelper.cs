using CommonServiceLocator;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
    public static class SettingsHelper
    {
		private static IDepartmentSettingsService _departmentSettingsService;

		private static IDepartmentSettingsService GetSettingsService()
		{
			if (_departmentSettingsService == null)
				_departmentSettingsService = ServiceLocator.Current.GetInstance<IDepartmentSettingsService>();

			return _departmentSettingsService;
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
	}
}
