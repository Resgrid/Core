using Resgrid.Model;
using System.Collections.Generic;
using System.Linq;

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

		public int GetTotalRoles()
		{
			if (Units != null && Units.Any())
			{
				int total = 0;
				foreach (var unit in Units)
				{
					if (unit.Roles != null && unit.Roles.Any())
					{
						total += unit.Roles.Count;
					}
				}

				return total;
			}

			return 0;
		}
	}
}
