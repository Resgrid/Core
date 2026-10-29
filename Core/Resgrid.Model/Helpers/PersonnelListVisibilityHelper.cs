namespace Resgrid.Model.Helpers
{
	/// <summary>
	/// Who sees a hidden or disabled member on the personnel list (GitHub #311): a department admin sees every one, a group
	/// admin sees the ones in the group they administer, and nobody else sees them. A member who is neither hidden nor
	/// disabled is not decided here; the visibility matrix (Security > View Group Users) still applies first.
	/// </summary>
	public static class PersonnelListVisibilityHelper
	{
		public static bool IsHiddenOrDisabled(DepartmentMember member) =>
			member != null && (member.IsHidden.GetValueOrDefault() || member.IsDisabled.GetValueOrDefault());

		/// <param name="member">The listed person's membership in <paramref name="department"/>.</param>
		/// <param name="department">The department the list is for.</param>
		/// <param name="memberGroup">The group the listed person is in, or null when they are ungrouped.</param>
		/// <param name="viewerUserId">The user looking at the list.</param>
		public static bool CanViewMember(DepartmentMember member, Department department, DepartmentGroup memberGroup, string viewerUserId)
		{
			if (!IsHiddenOrDisabled(member))
				return true;

			// Department.IsUserAnAdmin compares against ManagingUserId, so a missing viewer must not reach it.
			if (string.IsNullOrWhiteSpace(viewerUserId))
				return false;

			if (department != null && department.IsUserAnAdmin(viewerUserId))
				return true;

			return memberGroup != null && memberGroup.IsUserGroupAdmin(viewerUserId);
		}

		/// <summary>
		/// The pages about one person (their profile and what hangs off it). Same rule as the list, except that a hidden or
		/// disabled person can still open their own.
		/// </summary>
		public static bool CanViewMemberProfile(DepartmentMember member, Department department, DepartmentGroup memberGroup, string viewerUserId)
		{
			if (member != null && !string.IsNullOrWhiteSpace(viewerUserId) && member.UserId == viewerUserId)
				return true;

			return CanViewMember(member, department, memberGroup, viewerUserId);
		}
	}
}
