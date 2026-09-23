namespace Resgrid.Model.Helpers
{
	/// <summary>
	/// Membership-state predicates shared by the automated paths. Two tiers, on purpose:
	/// <list type="bullet">
	/// <item><see cref="IsCurrentMember"/> — not deleted, not disabled. Who may still act in the department
	/// (perform an assigned checklist, log labor on a work order).</item>
	/// <item><see cref="IsActiveMember"/> — current and not hidden. Who automation addresses and who reports
	/// name: reminders, escalations, digests, notifications, compliance rows, pickers.</item>
	/// </list>
	/// The nullable flags read null as false.
	/// </summary>
	public static class DepartmentMemberStateHelper
	{
		public static bool IsCurrentMember(DepartmentMember member, int departmentId) =>
			member != null && member.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(member.UserId) &&
			!member.IsDeleted && !member.IsDisabled.GetValueOrDefault();

		public static bool IsActiveMember(DepartmentMember member, int departmentId) =>
			IsCurrentMember(member, departmentId) && !member.IsHidden.GetValueOrDefault();
	}
}
