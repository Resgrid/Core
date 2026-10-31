using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Who may decide a shift trade request: pick which applicant takes the slot, and approve a pick that waits for
	/// approval. Department admins (and department-wide shift managers) always may; the rest is the department's choice.
	/// Stored as the <see cref="DepartmentSettingTypes.ShiftTradeApprovers"/> setting; the default lets group admins and
	/// the person who asked for the trade decide.
	/// </summary>
	public class ShiftTradeApproverSettings
	{
		/// <summary>Admins of the slot's group (and the groups above it).</summary>
		public bool GroupAdmins { get; set; } = true;

		/// <summary>The person trading out picks who takes their slot.</summary>
		public bool Requester { get; set; } = true;

		/// <summary>Members of these personnel roles, for any trade in the department.</summary>
		public List<int> RoleIds { get; set; } = new List<int>();

		public static ShiftTradeApproverSettings Default()
		{
			return new ShiftTradeApproverSettings();
		}

		public bool HasRoles => RoleIds != null && RoleIds.Any();

		/// <summary>The user holds one of the approver roles.</summary>
		public bool IsRoleApprover(IEnumerable<int> userRoleIds)
		{
			return HasRoles && userRoleIds != null && userRoleIds.Any(x => RoleIds.Contains(x));
		}
	}
}
