using System;

namespace Resgrid.Model
{
	/// <summary>
	/// A role membership change refused by <c>IPersonnelRolesService</c>: the message is the domain code the callers
	/// already match (<c>certifications_role_requirements_unmet</c>, <c>roles_member_not_in_department</c>) and
	/// <see cref="UserId"/> names the one member the refusal is about, so a caller reports that member rather than
	/// every addition it sent.
	/// </summary>
	public sealed class RoleMembershipException : InvalidOperationException
	{
		public const string RequirementsUnmet = "certifications_role_requirements_unmet";
		public const string NotInDepartment = "roles_member_not_in_department";

		public RoleMembershipException(string code, string userId) : base(code)
		{
			UserId = userId;
		}

		/// <summary>The member the change was refused for.</summary>
		public string UserId { get; }
	}
}
