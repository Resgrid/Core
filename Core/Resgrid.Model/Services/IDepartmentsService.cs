using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Custom;
using Resgrid.Model.Identity;

namespace Resgrid.Model.Services
{
	public interface IDepartmentsService
	{

		Task<List<Department>> GetAllAsync();

		Task<bool> DoesDepartmentExistAsync(string name);

		Task<Department> GetDepartmentByNameAsync(string name);

		Task<Department> GetDepartmentByIdAsync(int departmentId, bool bypassCache = true);

		Task<Department> SaveDepartmentAsync(Department department,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> InvalidateAllDepartmentsCache(int departmentId);

		void InvalidateDepartmentInCache(int departmentId);

		void InvalidateDepartmentMemberInCache(string userId, int departmentId);

		void InvalidateDepartmentUsersInCache(int departmentId);

		void InvalidateDepartmentMembers();

		void InvalidatePersonnelNamesInCache(int departmentId);

		void InvalidateDepartmentUserInCache(string userId, IdentityUser user = null);

		Task<Department> CreateDepartmentAsync(string name, string userId, string type, string affiliateCode,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<Department> UpdateDepartmentAsync(Department department,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<string> GetUserIdForDeletedUserInDepartmentAsync(int departmentId, string email);

		/// <summary>
		/// Brings a removed membership back: not deleted, disabled or hidden, and never an admin (admin standing is granted
		/// again deliberately, not restored from the removed row). Audited as UserReactivated; null when there is no row.
		/// </summary>
		Task<DepartmentMember> ReactivateUserAsync(int departmentId, string userId, string reactivatingUserId,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<DepartmentMember> AddExistingUserAsync(int departmentId, string userId,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<DepartmentMember> DeleteUserAsync(int departmentId, string userIdToDelete, string deletingUserId,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<DepartmentMember> JoinDepartmentAsync(int departmentId, string userId,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> SetActiveDepartmentForUserAsync(string userId, int departmentId, IdentityUser user,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> SetDefaultDepartmentForUserAsync(string userId, int departmentId, IdentityUser user,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> IsMemberOfDepartmentAsync(int departmentId, string userId);

		Task<DepartmentMember> AddUserToDepartmentAsync(int departmentId, string userId, bool isAdmin = false,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<Department> GetDepartmentForUserAsync(string userName);

		Task<Department> GetDepartmentByUserIdAsync(string userId, bool bypassCache = false);

		/// <summary>
		/// Resolves the department to use for a user's SMS/chatbot operations: their ACTIVE (then default,
		/// then first) membership. No plan-based auto-picking — if the active department's plan doesn't
		/// support SMS the downstream plan gate handles it (offering a department switch when the user has
		/// other supported memberships). Null when the user has no non-deleted memberships.
		/// </summary>
		Task<Department> GetActiveSmsDepartmentForUserAsync(string userId, bool bypassCache = false);

		/// <summary>
		/// The user's non-deleted memberships restricted to departments whose plan supports SMS/chatbot
		/// (non-free), in a stable order (active first, then by department id) so a numeric pick from a
		/// displayed list maps to the same membership across stateless SMS requests.
		/// </summary>
		Task<List<DepartmentMember>> GetSmsSupportedMembershipsForUserAsync(string userId);

		Task<ValidateUserForDepartmentResult> GetValidateUserForDepartmentInfoAsync(string userName,
			bool bypassCache = true);

		Task<bool> ValidateUserAndDepartmentByUserAsync(string userName, int departmentId, string departmentCode);

		Task<List<IdentityUser>> GetAllUsersForDepartment(int departmentId, bool retrieveHidden = false, bool bypassCache = false);

		Task<List<IdentityUser>> GetAllUsersForDepartmentAsync(int departmentId, bool retrieveHidden = false,
			bool bypassCache = false);

		Task<List<PersonName>> GetAllPersonnelNamesForDepartmentAsync(int departmentId);

		/// <summary>
		/// The names a person picker may offer: active members only (removed, disabled and hidden excluded), taken from the
		/// unlimited roster and ordered by name. <see cref="GetAllPersonnelNamesForDepartmentAsync"/> still labels historical
		/// rows and the value already stored on an edit form, which may belong to someone who has since gone inactive.
		/// </summary>
		Task<List<PersonName>> GetSelectablePersonnelNamesAsync(int departmentId);

		/// <summary>The department's admins and managing user; removed and disabled memberships are excluded.</summary>
		Task<List<IdentityUser>> GetAllAdminsForDepartmentAsync(int departmentId);

		/// <summary>
		/// <see cref="GetAllAdminsForDepartmentAsync"/> without hidden memberships either: the recipients of automated admin
		/// digests and sweep notices (certifications, compliance documents, finance and MARS reminders, pay-data readiness).
		/// </summary>
		Task<List<IdentityUser>> GetActiveAdminsForDepartmentAsync(int departmentId);

		Task<List<DepartmentMember>> GetAllMembersForDepartmentAsync(int departmentId);

		Task<List<IdentityUser>> GetAllUsersForDepartmentUnlimitedAsync(int departmentId, bool bypassCache = false);

		Task<List<IdentityUser>> GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(int departmentId,
			bool bypassCache = false);

		Task<List<DepartmentMember>> GetAllMembersForDepartmentUnlimitedAsync(int departmentId,
			bool bypassCache = false);

		Task<DepartmentMember> GetDepartmentMemberAsync(string userId, int departmentId, bool bypassCache = true);

		Task<DepartmentMember> SaveDepartmentMemberAsync(DepartmentMember departmentMember,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<DepartmentCallEmail> GetDepartmentEmailSettingsAsync(int departmentId);

		Task<DepartmentCallEmail> SaveDepartmentEmailSettingsAsync(DepartmentCallEmail emailSettings,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> DeleteDepartmentEmailSettingsAsync(int departmentId,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<List<DepartmentCallEmail>> GetAllDepartmentEmailSettingsAsync();

		Task<DepartmentCallPruning> GetDepartmentCallPruningSettingsAsync(int departmentId);

		Task<List<DepartmentCallPruning>> GetAllDepartmentCallPruningsAsync();

		Task<DepartmentCallPruning> SaveDepartmentCallPruningAsync(DepartmentCallPruning callPruning,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<List<string>> GetAllDisabledOrHiddenUsersAsync(int departmentId);

		Task<bool> IsUserDisabledAsync(string userId, int departmentId);

		Task<bool> IsUserHiddenAsync(string userId, int departmentId);

		Task<bool> IsUserInDepartmentAsync(int departmentId, string userId);

		/// <summary>
		/// Returns which of the supplied user ids are members of the department, resolved in a single query
		/// (no per-user round trips). Use to batch-validate membership before bulk operations.
		/// </summary>
		Task<HashSet<string>> GetMemberUserIdsInDepartmentAsync(int departmentId, IEnumerable<string> userIds);

		/// <summary>
		/// The user ids of the department's active members: deleted, disabled and hidden memberships are excluded.
		/// Unlimited (never truncated to the plan's personnel limit) and read in one query, uncached. This is the set
		/// automated sweeps, digests, notifications and personnel reports address; a stored user id (an assignee, a
		/// certificate holder, a report subject) outside it belongs to someone who has left or been switched off.
		/// </summary>
		Task<HashSet<string>> GetActiveMemberUserIdsAsync(int departmentId);

		Task<List<string>> GetAllDepartmentNamesAsync();

		Task<List<DepartmentMember>> GetAllDepartmentsForUserAsync(string userId);

		Task<DepartmentReport> GetDepartmentSetupReportAsync(int departmentId);

		string ConvertDepartmentCodeToDigitPin(string departmentCode);

		decimal GenerateSetupScore(DepartmentReport report);

		/// <summary>
		/// Gets user department stats by department id and user id asynchronous.
		/// </summary>
		/// <param name="departmentId">the department id</param>
		/// <param name="userId">your userid</param>
		/// <returns></returns>
		Task<DepartmentStats> GetDepartmentStatsByDepartmentUserIdAsync(int departmentId, string userId);
	}
}
