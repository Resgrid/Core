using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Custom;
using Resgrid.Model.Identity;

namespace Resgrid.Model.Services
{
	public interface IDepartmentsService
	{
		List<Department> GetAll();
		bool DoesDepartmentExist(string name);
		Department GetDepartmentByName(string name);
		Department GetDepartmentById(int departmentId, bool bypassCache = true);
		Department CreateDepartment(string name, string userId, string type);
		//DepartmentMember AddUserToDepartment(string name, string userId);
		Department UpdateDepartment(Department department);
		Department GetDepartmentForUser(string userName);
		Department GetDepartmentByUserId(string userId, bool bypassCache = false);
		List<IdentityUser> GetAllUsersForDepartment(int departmentId, bool retrieveHidden = false, bool bypassCache = false);
		DepartmentMember GetDepartmentMember(string userId, int departmentId, bool bypassCache = true);
		DepartmentMember SaveDepartmentMember(DepartmentMember departmentMember);
		Department GetDepartmentByApiKey(string apiKey);
		DepartmentBreakdown GetDepartmentBreakdown();
		Dictionary<string, int> GetNewDepartmentCountForLast5Days();
		List<DepartmentMember> GetAllMembersForDepartment(int departmentId);
		Department SaveDepartment(Department department);
		List<DepartmentMember> GetAllMembersForDepartmentUnlimited(int departmentId, bool bypassCache = false);
		List<IdentityUser> GetAllUsersForDepartmentUnlimited(int departmentId, bool bypassCache = false);
		DepartmentCallEmail GetDepartmentEmailSettings(int departmentId);
		DepartmentCallEmail SaveDepartmentEmailSettings(DepartmentCallEmail emailSettings);
		void DeleteDepartmentEmailSettings(int departmentId);
		List<DepartmentCallEmail> GetAllDepartmentEmailSettings();
		DepartmentCallPruning GetDepartmentCallPruningSettings(int departmentId);
		List<DepartmentCallPruning> GetAllDepartmentCallPrunings();
		DepartmentCallPruning SavelDepartmentCallPruning(DepartmentCallPruning callPruning);
		bool IsUserDisabled(string userId, int departmentId);
		bool IsUserHidden(string userId, int departmentId);
		List<IdentityUser> GetAllUsersForDepartmentUnlimitedMinusDisabled(int departmentId, bool bypassCache = false);
		List<string> GetAllDepartmentNames();
		bool IsUserInDepartment(int departmentId, string userId);
		Department CreateDepartment(string name, string userId, string type, string affiliateCode);
		DepartmentMember AddUserToDepartment(int departmentId, string userId);
		List<string> GetAllDisabledOrHiddenUsers(int departmentId);
		List<IdentityUser> GetAllAdminsForDepartment(int departmentId);
		Department GetDepartmentByIdNoAdmins(int departmentId);
		bool ValidateUserAndDepartmentByUser(string userName, int departmentId, string departmentCode);
		DepartmentReport GetDepartmentSetupReport(int departmentId);
		decimal GenerateSetupScore(DepartmentReport report);
		List<PersonName> GetAllPersonnelNamesForDepartment(int departmentId);
		void InvalidateDepartmentInCache(int departmentId);
		ValidateUserForDepartmentResult GetValidateUserForDepartmentInfo(string userName, bool bypassCache = true);
		void InvalidateDepartmentUserInCache(string userId, IdentityUser user = null);
		void InvalidateDepartmentMemberInCache(string userId, int departmentId);
		void InvalidateDepartmentUsersInCache(int departmentId);
		void InvalidatePersonnelNamesInCache(int departmentId);
		List<DepartmentMember> GetAllDepartmentMembers(bool bypassCache = false);
		void InvalidateDepartmentMembers();
		void InvalidateAllDepartmentsCache(int departmentId);
		List<DepartmentMember> GetAllDepartmentsForUser(string userId);
		DepartmentMember JoinDepartment(int departmentId, string userId);
		bool IsMemberOfDepartment(int departmentId, string userId);
		void SetActiveDepartmentForUser(string userId, int departmentId, IdentityUser user);
		DepartmentMember DeleteUser(int departmentId, string userIdToDelete, string deletingUserId);
		string GetUserIdForDeletedUserInDepartment(int departmentId, string email);
		void ReactivateUser(int departmentId, string userId);
		void AddExistingUser(int departmentId, string userId);
		DepartmentMember GetDepartmentMember(string userId, int departmentId);
		Department GetDepartmentEF(int departmentId);
		void SetDefaultDepartmentForUser(string userId, int departmentId, IdentityUser user);
		Task<Department> GetDepartmentForUserAsync(string userName);
		Task<List<IdentityUser>> GetAllUsersForDepartmentAsync(int departmentId, bool retrieveHidden = false, bool bypassCache = false);
	}
}
