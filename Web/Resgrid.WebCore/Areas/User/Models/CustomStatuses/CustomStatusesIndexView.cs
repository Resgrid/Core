using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CustomStatuses
{
	public class CustomStatusesIndexView
	{
		public CustomState PersonnelStatuses { get; set; }
		public CustomState PersonellStaffing { get; set; }
		public List<CustomState> UnitStates { get; set; }
	}
}