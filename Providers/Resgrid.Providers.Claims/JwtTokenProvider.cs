using Microsoft.IdentityModel.Tokens;
using Resgrid.Config;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Resgrid.Providers.Claims
{
	public class JwtTokenProvider
	{
		private IUsersService _usersService;
		private IDepartmentsService _departmentsService;
		private IDepartmentGroupsService _departmentGroupsService;
		private IUserProfileService _userProfileService;
		private IPermissionsService _permissionsService;
		private IPersonnelRolesService _personnelRolesService;
		private IClaimsRepository _claimsRepository;

		public JwtTokenProvider(IUsersService usersService, IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService,
			IUserProfileService userProfileService, IPermissionsService permissionsService, IPersonnelRolesService personnelRolesService, IClaimsRepository claimsRepository)
		{
			_usersService = usersService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_userProfileService = userProfileService;
			_permissionsService = permissionsService;
			_personnelRolesService = personnelRolesService;
			_claimsRepository = claimsRepository;
		}

		public async Task<string> BuildTokenAsync(string userId, int departmentId)
		{
			ClaimsIdentity id = new ClaimsIdentity();

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (!department.IsUserInDepartment(userId))
				return null;

			var user = _usersService.GetUserById(userId, false);
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, department.DepartmentId);
			var departmentAdmin = department.IsUserAnAdmin(userId);
			var permissions = await _permissionsService.GetAllPermissionsForDepartmentAsync(department.DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, department.DepartmentId);

			id.AddClaim(new Claim(ResgridClaimTypes.Data.UserId, userId));
			id.AddClaim(new Claim(ResgridClaimTypes.Data.DisplayName, profile.FullName.AsFirstNameLastName));
			id.AddClaim(new Claim(ClaimTypes.Email, profile.MembershipEmail));
			ClaimsLogic.AddDepartmentClaim(id, department.DepartmentId, departmentAdmin);

			DateTime signupDate;
			if (department.CreatedOn.HasValue)
				signupDate = department.CreatedOn.Value;
			else
				signupDate = DateTime.UtcNow;

			var name = user.UserName;
			if (profile != null && !String.IsNullOrWhiteSpace(profile.LastName))
				name = profile.FullName.AsFirstNameLastName;

			ClaimsLogic.AddGeneralClaims(id, user.UserName,
				userId, name, department.DepartmentId, department.Name, profile.MembershipEmail,
				signupDate);

			Claim timeZoneClaim = new Claim(ResgridClaimTypes.Data.TimeZone, department.TimeZone);
			if (!id.HasClaim(timeZoneClaim.Type, timeZoneClaim.Value))
			{
				id.AddClaim(timeZoneClaim);
			}

			bool isGroupAdmin = false;

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(user.Id);

			if (departmentAdmin)
			{
				var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(department.DepartmentId);
				if (groups != null)
				{
					foreach (var departmentGroup in groups)
					{
						ClaimsLogic.AddGroupClaim(id, departmentGroup.DepartmentGroupId, true);
					}
				}
			}
			else
			{
				if (group != null)
					ClaimsLogic.AddGroupClaim(id, group.DepartmentGroupId, isGroupAdmin);
			}

			ClaimsLogic.AddCallClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
			ClaimsLogic.AddActionClaims(id);
			ClaimsLogic.AddLogClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
			ClaimsLogic.AddStaffingClaims(id);
			ClaimsLogic.AddPersonnelClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
			ClaimsLogic.AddUnitClaims(id, departmentAdmin);
			ClaimsLogic.AddUnitLogClaims(id);
			ClaimsLogic.AddMessageClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
			ClaimsLogic.AddRoleClaims(id, departmentAdmin);
			ClaimsLogic.AddProfileClaims(id);
			ClaimsLogic.AddReportsClaims(id);
			ClaimsLogic.AddGenericGroupClaims(id, departmentAdmin);
			ClaimsLogic.AddDocumentsClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
			ClaimsLogic.AddNotesClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
			ClaimsLogic.AddScheduleClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
			ClaimsLogic.AddShiftClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
			ClaimsLogic.AddTrainingClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
			ClaimsLogic.AddPIIClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
			ClaimsLogic.AddInventoryClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);
			ClaimsLogic.AddConnectClaims(id, departmentAdmin);
			ClaimsLogic.AddCommandClaims(id, departmentAdmin);
			ClaimsLogic.AddProtocolClaims(id, departmentAdmin);
			ClaimsLogic.AddFormsClaims(id, departmentAdmin);
			ClaimsLogic.AddVoiceClaims(id, departmentAdmin);
			ClaimsLogic.AddCustomStateClaims(id, departmentAdmin);
			ClaimsLogic.AddContactsClaims(id, departmentAdmin, permissions, isGroupAdmin, roles);

			var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtConfig.Key));
			var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature);
			var tokenDescriptor = new JwtSecurityToken(JwtConfig.Issuer, JwtConfig.Audience, id.Claims,
				expires: DateTime.Now.AddHours(JwtConfig.Duration), signingCredentials: credentials);
			return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
		}

		public bool IsTokenValid(string token)
		{
			var mySecret = Encoding.UTF8.GetBytes(JwtConfig.Key);
			var mySecurityKey = new SymmetricSecurityKey(mySecret);
			var tokenHandler = new JwtSecurityTokenHandler();
			try
			{
				tokenHandler.ValidateToken(token,
				new TokenValidationParameters
				{
					ValidateIssuerSigningKey = true,
					ValidateIssuer = true,
					ValidateAudience = true,
					ValidIssuer = JwtConfig.Issuer,
					ValidAudience = JwtConfig.Audience,
					IssuerSigningKey = mySecurityKey,
				}, out SecurityToken validatedToken);
			}
			catch
			{
				return false;
			}
			return true;
		}
	}
}
