using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Units
{
	public class UnitsIndexView : BaseUserModel
	{
		public Department Department { get; set; }
		public List<Unit> Units { get; set; }
		public List<UnitState> States { get; set; }
		public List<DepartmentGroup> Groups { get; set; }
		public List<CustomState> UnitStatuses { get; set; }
		public bool CanUserAddUnit { get; set; }
		public bool IsUserAdminOrGroupAdmin { get; set; }
		public string TreeData { get; set; }

		public Dictionary<int, CustomState> UnitCustomStates { get; set; }
	}
}
