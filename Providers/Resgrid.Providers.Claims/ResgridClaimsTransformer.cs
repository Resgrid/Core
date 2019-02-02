using System;
using System.IdentityModel.Services;
using System.IdentityModel.Tokens;
using System.Security.Claims;
using System.Web.Profile;
using Microsoft.Practices.ServiceLocation;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Providers.Claims
{
	public class ResgridClaimsTransformer : ClaimsAuthenticationManager
	{
		private IUsersService _usersService;
		private IDepartmentsService _departmentsService;
		private IDepartmentGroupsService _departmentGroupsService;
		private IUserProfileService _userProfileService;
		private IPermissionsService _permissionsService;
		private IPersonnelRolesService _personnelRolesService;

		public override ClaimsPrincipal Authenticate(string resourceName, ClaimsPrincipal incomingPrincipal)
		{
			if (!incomingPrincipal.Identity.IsAuthenticated)
				return base.Authenticate(resourceName, incomingPrincipal);

			ClaimsPrincipal transformedPrincipal = DressUpPrincipal(incomingPrincipal.Identity.Name);

			CreateSession(transformedPrincipal);

			return transformedPrincipal;
		}

		private ClaimsPrincipal DressUpPrincipal(String userName)
		{
			if (String.IsNullOrWhiteSpace(userName))
				return null;

			var identiy = new ResgridIdentity();

			_usersService = ServiceLocator.Current.GetInstance<IUsersService>();

			var user = _usersService.GetUserByName(userName);

			if (user == null)
				return null;

			_departmentsService = ServiceLocator.Current.GetInstance<IDepartmentsService>();
			_departmentGroupsService = ServiceLocator.Current.GetInstance<IDepartmentGroupsService>();
			_userProfileService = ServiceLocator.Current.GetInstance<IUserProfileService>();
			_permissionsService = ServiceLocator.Current.GetInstance<IPermissionsService>();
			_personnelRolesService = ServiceLocator.Current.GetInstance<IPersonnelRolesService>();

			var profile = _userProfileService.GetProfileByUserId(user.UserId);
			
			string fullName = String.Empty;
			if (profile == null || String.IsNullOrWhiteSpace(profile.FirstName))
			{
				var pfile = ProfileBase.Create(user.UserName, true);
				fullName = pfile.GetPropertyValue("Name").ToString();
			}
			else
			{
				fullName = string.Format("{0} {1}", profile.FirstName, profile.LastName);
			}

			identiy.FullName = fullName;
			identiy.UserName = userName;
			identiy.UserId = user.UserId;

			if (_usersService.IsUserInRole(user.UserId, _usersService.AdminRoleId))
			{
				identiy.AddSystemAdminClaims(userName, user.UserId, fullName);
			}
			else if (_usersService.IsUserInRole(user.UserId, _usersService.AffiliateRoleId))
			{
				identiy.AddAffiliteClaims(userName, user.UserId, fullName);
			}
			else
			{
				var department = _departmentsService.GetDepartmentForUser(userName);

				if (department == null)
					return null;

				var group = _departmentGroupsService.GetGroupForUser(user.UserId, department.DepartmentId);
				var departmentAdmin = department.IsUserAnAdmin(user.UserId);
				var permissions = _permissionsService.GetAllPermissionsForDepartment(department.DepartmentId);
				var roles = _personnelRolesService.GetRolesForUser(user.UserId, department.DepartmentId);

				identiy.AddDepartmentClaim(department.DepartmentId, departmentAdmin);
				identiy.DepartmentName = department.Name;

				DateTime signupDate;
				if (department.CreatedOn.HasValue)
					signupDate = department.CreatedOn.Value;
				else
					signupDate = DateTime.UtcNow;

				identiy.DepartmentId = department.DepartmentId;

				string email = String.Empty;

				if (profile != null && !String.IsNullOrWhiteSpace(profile.MembershipEmail))
					email = profile.MembershipEmail;
				else if (!String.IsNullOrWhiteSpace(user.Email))
					email = user.Email;

				identiy.AddGeneralClaims(userName, user.UserId, fullName, department.DepartmentId, department.Name, email, signupDate);

				bool isGroupAdmin = false;

				if (group != null)
					isGroupAdmin = group.IsUserGroupAdmin(user.UserId);

				if (departmentAdmin)
				{
					var groups = _departmentGroupsService.GetAllGroupsForDepartment(department.DepartmentId);
					if (groups != null)
					{
						foreach (var departmentGroup in groups)
						{
							identiy.AddGroupClaim(departmentGroup.DepartmentGroupId, true);
						}
					}
				}
				else
				{
					if (group != null)
						identiy.AddGroupClaim(group.DepartmentGroupId, isGroupAdmin);
				}

				identiy.AddCallClaims(departmentAdmin, permissions, isGroupAdmin, roles);
				identiy.AddActionClaims();
				identiy.AddLogClaims(departmentAdmin, permissions, isGroupAdmin, roles);
				identiy.AddStaffingClaims();
				identiy.AddPersonnelClaims(departmentAdmin);
				identiy.AddUnitClaims(departmentAdmin);
				identiy.AddUnitLogClaims();
				identiy.AddMessageClaims();
				identiy.AddRoleClaims(departmentAdmin);
				identiy.AddProfileClaims();
				identiy.AddReportsClaims();
				identiy.AddGenericGroupClaims(departmentAdmin);
				identiy.AddDocumentsClaims(departmentAdmin, permissions, isGroupAdmin, roles);
				identiy.AddNotesClaims(departmentAdmin, permissions, isGroupAdmin, roles);
				identiy.AddScheduleClaims(departmentAdmin, permissions, isGroupAdmin, roles);
				identiy.AddShiftClaims(departmentAdmin, permissions, isGroupAdmin, roles);
				identiy.AddTrainingClaims(departmentAdmin, permissions, isGroupAdmin, roles);
				identiy.AddPIIClaims(departmentAdmin, permissions, isGroupAdmin, roles);
				identiy.AddInventoryClaims(departmentAdmin, permissions, isGroupAdmin, roles);
			}

			return new ClaimsPrincipal(identiy);
		}

		private void CreateSession(ClaimsPrincipal transformedPrincipal)
		{
			if (transformedPrincipal != null)
			{
				var sessionSecurityToken = new SessionSecurityToken(transformedPrincipal, TimeSpan.FromHours(24));
				FederatedAuthentication.SessionAuthenticationModule.WriteSessionTokenToCookie(sessionSecurityToken);
			}
		}
	}
}