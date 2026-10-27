using ProtoBuf;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments
{
    public class DepartmentModulesSettingView
    {
		public bool SaveSuccess { get; set; }
		public DepartmentModuleSettings Modules { get; set; }

		public bool MessagingEnabled { get; set; }
		public bool MappingEnabled { get; set; }
		public bool ShiftsEnabled { get; set; }
		public bool LogsEnabled { get; set; }
		public bool ReportsEnabled { get; set; }
		public bool DocumentsEnabled { get; set; }
		public bool CalendarEnabled { get; set; }
		public bool NotesEnabled { get; set; }
		public bool TrainingEnabled { get; set; }
		public bool InventoryEnabled { get; set; }
		public bool MaintenanceEnabled { get; set; }
		public bool ChecklistsEnabled { get; set; } = true;
		public bool AiEnabled { get; set; } = true;
		public bool BusinessOperationsEnabled { get; set; } = true;
		// Incident Back Office switches (DepartmentModuleSettings 26-28): the module, its finance desks and coordination boards.
		public bool IncidentBackOfficeEnabled { get; set; } = true;
		public bool IncidentBusinessEnabled { get; set; } = true;
		public bool IncidentCoordinationEnabled { get; set; } = true;

		public DepartmentModulesSettingView()
		{
			Modules = new DepartmentModuleSettings();
		}
	}
}
