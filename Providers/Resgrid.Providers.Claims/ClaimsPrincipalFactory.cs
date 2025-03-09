using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using IdentityRole = Resgrid.Model.Identity.IdentityRole;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Providers.Claims
{
	public class ClaimsPrincipalFactory<TUser, TRole> : UserClaimsPrincipalFactory<TUser, TRole>
	where TUser : IdentityUser
	where TRole : IdentityRole
	{
		private IdentityOptions options;
		private IUsersService _usersService;
		private IDepartmentsService _departmentsService;
		private IDepartmentGroupsService _departmentGroupsService;
		private IUserProfileService _userProfileService;
		private IPermissionsService _permissionsService;
		private IPersonnelRolesService _personnelRolesService;
		private IClaimsRepository _claimsRepository;

		public ClaimsPrincipalFactory(UserManager<TUser> userManager, RoleManager<TRole> roleManager, IOptions<IdentityOptions> optionsAccessor,
			IUsersService usersService, IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService, IUserProfileService userProfileService, 
			IPermissionsService permissionsService, IPersonnelRolesService personnelRolesService, IClaimsRepository claimsRepository
			) : base(userManager, roleManager, optionsAccessor)
		{
			_usersService = usersService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_userProfileService = userProfileService;
			_permissionsService = permissionsService;
			_personnelRolesService = personnelRolesService;
			_claimsRepository = claimsRepository;

			options = optionsAccessor.Value;
		}

		public override async Task<ClaimsPrincipal> CreateAsync(TUser user)
		{
			if (user == null)
			{
				throw new ArgumentNullException("user");
			}

			var userId = await UserManager.GetUserIdAsync(user);
			var userName = await UserManager.GetUserNameAsync(user);

			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			var id = new ClaimsIdentity(
				CookieAuthenticationDefaults.AuthenticationScheme,
				Options.ClaimsIdentity.UserNameClaimType,
				Options.ClaimsIdentity.RoleClaimType
				);

			id.AddClaim(new Claim(Options.ClaimsIdentity.UserIdClaimType, userId));
			id.AddClaim(new Claim(Options.ClaimsIdentity.UserNameClaimType, userName));

			if (UserManager.SupportsUserSecurityStamp)
			{
				id.AddClaim(new Claim(Options.ClaimsIdentity.SecurityStampClaimType,
					await UserManager.GetSecurityStampAsync(user)));
			}

			if (UserManager.SupportsUserRole)
			{
				var roles = await _claimsRepository.GetRolesAsync(user);
				foreach (var roleName in roles)
				{
					id.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, roleName));
				}
			}

			ClaimsPrincipal principal = new ClaimsPrincipal(id);

			if (principal.Identity is ClaimsIdentity)
			{
				ClaimsIdentity identity = (ClaimsIdentity) principal.Identity;

				if (profile != null)
				{
					Claim displayNameClaim = new Claim("DisplayName", profile.FullName.AsFirstNameLastName);
					if (!identity.HasClaim(displayNameClaim.Type, displayNameClaim.Value))
					{
						identity.AddClaim(displayNameClaim);
					}
				}

				Claim emailClaim = new Claim(ClaimTypes.Email, user.Email);
				if (!identity.HasClaim(emailClaim.Type, emailClaim.Value))
				{
					identity.AddClaim(emailClaim);
				}

				if (_usersService.IsUserInRole(user.Id, _usersService.AdminRoleId))
				{
					ClaimsLogic.AddSystemAdminClaims(id, userName, user.Id, "System Admin");
				}
				else if (_usersService.IsUserInRole(user.Id, _usersService.AffiliateRoleId))
				{
					ClaimsLogic.AddAffiliteClaims(id, userName,
						user.Id, profile.FullName.AsFirstNameLastName);
				}
				else
				{
					var department = await _departmentsService.GetDepartmentForUserAsync(userName);

					if (department == null)
						return null;

					var group = await _departmentGroupsService.GetGroupForUserAsync(user.Id, department.DepartmentId);
					var departmentAdmin = department.IsUserAnAdmin(user.Id);
					var permissions = await _permissionsService.GetAllPermissionsForDepartmentAsync(department.DepartmentId);
					var roles = await _personnelRolesService.GetRolesForUserAsync(user.Id, department.DepartmentId);

					ClaimsLogic.AddDepartmentClaim(id, department.DepartmentId,
						departmentAdmin);
					//ClaimsLogic.DepartmentName = department.Name;

					DateTime signupDate;
					if (department.CreatedOn.HasValue)
						signupDate = department.CreatedOn.Value;
					else
						signupDate = DateTime.UtcNow;

					//ClaimsLogic.DepartmentId = department.DepartmentId;

					var name = user.UserName;
					if (profile != null && !String.IsNullOrWhiteSpace(profile.LastName))
						name = profile.FullName.AsFirstNameLastName;

					ClaimsLogic.AddGeneralClaims(id, userName,
						user.Id, name, department.DepartmentId, department.Name, user.Email,
						signupDate);

					bool isGroupAdmin = false;

					if (group != null)
						isGroupAdmin = group.IsUserGroupAdmin(user.Id);

					//if (departmentAdmin)
					//{
					//	var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(department.DepartmentId);
					//	if (groups != null)
					//	{
					//		foreach (var departmentGroup in groups)
					//		{
					//			ClaimsLogic.AddGroupClaim(id, departmentGroup.DepartmentGroupId, true);
					//		}
					//	}
					//}
					//else
					//{
						if (group != null)
							ClaimsLogic.AddGroupClaim(id, group.DepartmentGroupId, isGroupAdmin);
					//}

					string timeZone = "Pacific Standard Time";
					if (!String.IsNullOrWhiteSpace(department.TimeZone))
						timeZone = department.TimeZone;

					Claim timeZoneClaim = new Claim(ResgridClaimTypes.Data.TimeZone, timeZone);
					if (!id.HasClaim(timeZoneClaim.Type, timeZoneClaim.Value))
					{
						id.AddClaim(timeZoneClaim);
					}

					ClaimsLogic.AddCallClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
					ClaimsLogic.AddActionClaims(id);
					ClaimsLogic.AddLogClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
					ClaimsLogic.AddStaffingClaims(id);
					ClaimsLogic.AddPersonnelClaims(id, departmentAdmin, permissions, isGroupAdmin,roles);
					ClaimsLogic.AddUnitClaims(id, departmentAdmin);
					ClaimsLogic.AddUnitLogClaims(id);
					ClaimsLogic.AddMessageClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
					ClaimsLogic.AddRoleClaims(id, departmentAdmin);
					ClaimsLogic.AddProfileClaims(id);
					ClaimsLogic.AddReportsClaims(id);
					ClaimsLogic.AddGenericGroupClaims(id, departmentAdmin);
					ClaimsLogic.AddDocumentsClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
					ClaimsLogic.AddNotesClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
					ClaimsLogic.AddScheduleClaims(id, departmentAdmin, permissions,	isGroupAdmin, roles);
					ClaimsLogic.AddShiftClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
					ClaimsLogic.AddTrainingClaims(id, departmentAdmin, permissions,	isGroupAdmin, roles);
					ClaimsLogic.AddPIIClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
					ClaimsLogic.AddInventoryClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
					ClaimsLogic.AddConnectClaims(id, departmentAdmin);
					ClaimsLogic.AddCommandClaims(id, departmentAdmin);
					ClaimsLogic.AddProtocolClaims(id, departmentAdmin);
					ClaimsLogic.AddFormsClaims(id, departmentAdmin);
					ClaimsLogic.AddVoiceClaims(id, departmentAdmin);
					ClaimsLogic.AddCustomStateClaims(id, departmentAdmin);
					ClaimsLogic.AddContactsClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
				}
			}

			return principal;
		}
	}
}
