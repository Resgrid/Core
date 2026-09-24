using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Why a user's dispatch scope resolved the way it did.</summary>
	public enum DispatchScopeReasons
	{
		/// <summary>The department has not turned group-scoped dispatch on; everyone is department-wide.</summary>
		ScopingDisabled = 0,

		/// <summary>Department admins always dispatch department-wide.</summary>
		DepartmentAdmin = 1,

		/// <summary>The user holds a role configured as department-wide (e.g. a central dispatch center).</summary>
		DepartmentWideRole = 2,

		/// <summary>The user administers a group; scope is that group and every group beneath it.</summary>
		GroupAdmin = 3,

		/// <summary>The user is a member of a group; scope is that group and every group beneath it.</summary>
		GroupMember = 4,

		/// <summary>The user is in no group and holds no department-wide role; only calls they are on are in scope.</summary>
		NoGroup = 5
	}

	/// <summary>
	/// The slice of a department a user dispatches right now. Computed per request from the
	/// user's roles and group membership (see <see cref="GroupDispatchScopeConfig"/>).
	/// </summary>
	public class DispatchScope
	{
		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public bool IsDepartmentWide { get; set; }

		public DispatchScopeReasons Reason { get; set; }

		/// <summary>The group the scope hangs from (the user's own group); null when department-wide or ungrouped.</summary>
		public int? AnchorGroupId { get; set; }

		/// <summary>The anchor group plus every descendant. Empty when department-wide (check <see cref="IsDepartmentWide"/>) or ungrouped.</summary>
		public HashSet<int> GroupIds { get; set; } = new HashSet<int>();

		public bool IncludesGroup(int? departmentGroupId)
		{
			if (IsDepartmentWide)
				return true;

			return departmentGroupId.HasValue && GroupIds.Contains(departmentGroupId.Value);
		}

		public static DispatchScope DepartmentWide(int departmentId, string userId, DispatchScopeReasons reason)
		{
			return new DispatchScope
			{
				DepartmentId = departmentId,
				UserId = userId,
				IsDepartmentWide = true,
				Reason = reason
			};
		}
	}
}
