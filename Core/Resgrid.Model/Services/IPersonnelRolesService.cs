using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IPersonnelRolesService
	{
		/// <summary>
		/// Gets the roles for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelRole&gt;&gt;.</returns>
		Task<List<PersonnelRole>> GetRolesForDepartmentAsync(int departmentId);
		/// <summary>
		/// Gets the roles for department unlimited asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelRole&gt;&gt;.</returns>
		Task<List<PersonnelRole>> GetRolesForDepartmentUnlimitedAsync(int departmentId);

		/// <summary>
		/// Gets the role by identifier asynchronous.
		/// </summary>
		/// <param name="roleId">The role identifier.</param>
		/// <returns>Task&lt;PersonnelRole&gt;.</returns>
		Task<PersonnelRole> GetRoleByIdAsync(int roleId);

		/// <summary>
		/// Gets all roles for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelRole&gt;&gt;.</returns>
		Task<List<PersonnelRole>> GetAllRolesForDepartmentAsync(int departmentId);


		/// <summary>
		/// Saves the role asynchronous.
		/// </summary>
		/// <param name="role">The role.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;PersonnelRole&gt;.</returns>
		Task<PersonnelRole> SaveRoleAsync(PersonnelRole role, CancellationToken cancellationToken = default(CancellationToken), string actingUserId = null);

		/// <summary>
		/// Saves the role and replaces its membership with <paramref name="userIds"/> in one transaction: the certification
		/// gate (plan D4) runs against the members the role gains before anything is deleted, and a failure anywhere leaves
		/// the previous membership in place. Every gained member must belong to the role's department. Throws
		/// <see cref="RoleMembershipException"/> naming the member: roles_member_not_in_department for a stranger,
		/// certifications_role_requirements_unmet for a member blocked under Enforce.
		/// </summary>
		Task<PersonnelRole> ReplaceRoleMembersAsync(PersonnelRole role, IEnumerable<string> userIds, CancellationToken cancellationToken = default(CancellationToken), string actingUserId = null);

		/// <summary>
		/// Gets the role by department and name asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="name">The name.</param>
		/// <returns>Task&lt;PersonnelRole&gt;.</returns>
		Task<PersonnelRole> GetRoleByDepartmentAndNameAsync(int departmentId, string name);

		/// <summary>
		/// Deletes the role by identifier asynchronous.
		/// </summary>
		/// <param name="roleId">The role identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteRoleByIdAsync(int roleId, CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>
		/// Deletes the role users asynchronous.
		/// </summary>
		/// <param name="users">The users.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteRoleUsersAsync(List<PersonnelRoleUser> users, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Workforce &amp; Business Operations plan Phase D4: evaluates a member against each role's certification
		/// requirements under the department's enforcement mode. Enforce lists blocked roles, WarnOnly lists warnings,
		/// Off returns an empty check. Callers show the result before mutating membership; the mutation methods
		/// re-run it as a backstop and throw <see cref="RoleMembershipException"/> (certifications_role_requirements_unmet,
		/// naming the member) under Enforce.
		/// </summary>
		Task<RoleMembershipCheck> CheckRoleMembershipAsync(int departmentId, string userId, IEnumerable<int> roleIds);

		/// <summary>
		/// Gets the roles for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelRole&gt;&gt;.</returns>
		Task<List<PersonnelRole>> GetRolesForUserAsync(string userId, int departmentId);

		/// <summary>
		/// Gets all roles for users in department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;Dictionary&lt;System.String, List&lt;PersonnelRole&gt;&gt;&gt;.</returns>
		Task<Dictionary<string, List<PersonnelRole>>> GetAllRolesForUsersInDepartmentAsync(int departmentId);


		/// <summary>
		/// Removes the user from all roles asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> RemoveUserFromAllRolesAsync(string userId, int departmentId, CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>
		/// Sets the roles for user asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="roleIds">The role ids.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SetRolesForUserAsync(int departmentId, string userId, string[] roleIds, CancellationToken cancellationToken = default(CancellationToken), string actingUserId = null);

		/// <summary>
		/// Gets all members of role asynchronous.
		/// </summary>
		/// <param name="roleId">The role identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelRoleUser&gt;&gt;.</returns>
		Task<List<PersonnelRoleUser>> GetAllMembersOfRoleAsync(int roleId);
	}
}

namespace Resgrid.Model
{
	/// <summary>Outcome of a role-membership certification check (plan D4).</summary>
	public sealed class RoleMembershipCheck
	{
		public int EnforcementMode { get; set; }
		public List<RoleCertificationEvaluation> Evaluations { get; set; } = new List<RoleCertificationEvaluation>();
		/// <summary>Roles the member may not join (Enforce and a mandatory requirement fails).</summary>
		public List<RoleCertificationEvaluation> Blocked { get; set; } = new List<RoleCertificationEvaluation>();
		/// <summary>Roles the member may join with a warning (WarnOnly, or optional requirements failing).</summary>
		public List<RoleCertificationEvaluation> Warnings { get; set; } = new List<RoleCertificationEvaluation>();
		public bool IsBlocked => Blocked.Count > 0;
	}
}
