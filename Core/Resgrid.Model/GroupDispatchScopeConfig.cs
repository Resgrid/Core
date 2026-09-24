using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Department switch for group-scoped dispatch, stored serialized in
	/// <see cref="DepartmentSettingTypes.GroupDispatchScopeConfig"/>. When enabled, a user's
	/// dispatch view (active call lists, single-call access, the nearest unit board) is limited
	/// to the calls and resources of their group and every group beneath it, unless the user is
	/// a department admin or holds one of <see cref="DepartmentWideRoleIds"/>.
	/// <para>
	/// Scope is resolved from the user's roles and group membership on every request and is
	/// never written onto calls, so handing dispatch from area supervisors to a central
	/// dispatch center at shift change is a role assignment, not a data move.
	/// </para>
	/// </summary>
	[ProtoContract]
	public class GroupDispatchScopeConfig
	{
		public GroupDispatchScopeConfig()
		{
			Enabled = false;
			DepartmentWideRoleIds = new List<int>();
		}

		/// <summary>Off by default: every member keeps today's department-wide call view.</summary>
		[ProtoMember(1)]
		public bool Enabled { get; set; }

		/// <summary>
		/// Personnel roles whose holders dispatch department-wide (e.g. a central dispatch center)
		/// even while scoping is on. Department admins are always department-wide.
		/// </summary>
		[ProtoMember(2)]
		public List<int> DepartmentWideRoleIds { get; set; }
	}
}
