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

		public DepartmentModulesSettingView()
		{
			Modules = new DepartmentModuleSettings();
		}
	}
}
