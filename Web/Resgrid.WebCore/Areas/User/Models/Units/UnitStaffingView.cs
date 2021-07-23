using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.WebCore.Areas.User.Models.Units
{
	public class UnitStaffingView
	{
		public bool IsDepartmentAdmin { get; set; }
		public int GroupId { get; set; }
		public string Note { get; set; }

		public List<Unit> Units { get; set; }
		public List<UserGroupRole> Users { get; set; }
		public List<UnitActiveRole> ActiveRoles { get; set; }
	}
}
