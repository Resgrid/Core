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
		public List<PersonnelRole> PersonnelRoles { get; set; }
		public Dictionary<int, UnitRoleStaffingResult> UnitStaffing { get; set; }

		/// <summary>Returns the display name of a personnel role by id, or null.</summary>
		public string GetPersonnelRoleName(int? personnelRoleId)
		{
			if (!personnelRoleId.HasValue || PersonnelRoles == null)
				return null;

			return PersonnelRoles.FirstOrDefault(x => x.PersonnelRoleId == personnelRoleId.Value)?.Name;
		}

		/// <summary>Returns the computed staffing result for a unit, or null if not available.</summary>
		public UnitRoleStaffingResult GetStaffingForUnit(int unitId)
		{
			if (UnitStaffing != null && UnitStaffing.TryGetValue(unitId, out var result))
				return result;

			return null;
		}

		/// <summary>Does the given member currently hold the given personnel role? (drives the UI warning)</summary>
		public bool DoesUserHoldPersonnelRole(string userId, int personnelRoleId)
		{
			if (string.IsNullOrWhiteSpace(userId) || Users == null)
				return false;

			var user = Users.FirstOrDefault(x => x.UserId == userId);

			return user != null && user.RoleList != null && user.RoleList.Contains(personnelRoleId);
		}

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
