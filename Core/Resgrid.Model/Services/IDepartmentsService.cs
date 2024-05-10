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

		Task<DepartmentMember> ReactivateUserAsync(int departmentId, string userId,
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

		Task<ValidateUserForDepartmentResult> GetValidateUserForDepartmentInfoAsync(string userName,
			bool bypassCache = true);

		Task<bool> ValidateUserAndDepartmentByUserAsync(string userName, int departmentId, string departmentCode);

		Task<List<IdentityUser>> GetAllUsersForDepartment(int departmentId, bool retrieveHidden = false, bool bypassCache = false);

		Task<List<IdentityUser>> GetAllUsersForDepartmentAsync(int departmentId, bool retrieveHidden = false,
			bool bypassCache = false);

		Task<List<PersonName>> GetAllPersonnelNamesForDepartmentAsync(int departmentId);

		Task<List<IdentityUser>> GetAllAdminsForDepartmentAsync(int departmentId);

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
