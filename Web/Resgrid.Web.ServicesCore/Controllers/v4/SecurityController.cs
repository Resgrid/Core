using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using System.Collections.Generic;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Security;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Call Priorities, for example Low, Medium, High. Call Priorities can be system provided ones or custom for a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class SecurityController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPermissionsService _permissionsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUserProfileService _userProfileService;
		private readonly INovuProvider _novuProvider;

		/// <summary>
		/// Operations to perform against the security sub-system
		/// </summary>
		public SecurityController(IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService,
			IPermissionsService permissionsService, IPersonnelRolesService personnelRolesService, IUserProfileService userProfileService,
			INovuProvider novuProvider)
		{
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_permissionsService = permissionsService;
			_personnelRolesService = personnelRolesService;
			_userProfileService = userProfileService;
			_novuProvider = novuProvider;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the current users department rights
		/// </summary>
		/// <returns>DepartmentRightsResult object with the department rights and group memberships</returns>
		[HttpGet("GetCurrentUsersRights")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<DepartmentRightsResult>> GetCurrentUsersRights()
		{
			var result = new DepartmentRightsResult();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var departmentMembership = await _departmentsService.GetDepartmentMemberAsync(UserId, DepartmentId, false);
			var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);

			if (departmentMembership == null)
				return Unauthorized();

			if (departmentMembership.IsAdmin.HasValue)
				result.Data.IsAdmin = departmentMembership.IsAdmin.Value;

			if (department.ManagingUserId == UserId)
				result.Data.IsAdmin = true;

			result.Data.DepartmentId = department.DepartmentId.ToString();
			result.Data.DepartmentName = department.Name;
			result.Data.DepartmentCode = department.Code;

			var profile = await _userProfileService.GetProfileByUserIdAsync(UserId);
			result.Data.EmailAddress = profile.MembershipEmail;
			result.Data.FullName = profile.FullName.AsFirstNameLastName;

			bool isGroupAdmin = false;
			result.Data.Groups = new List<GroupRightData>();

			var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);

			if (group != null)
			{
				var groupRight = new GroupRightData();
				groupRight.GroupId = group.DepartmentGroupId.ToString();
				groupRight.IsGroupAdmin = group.IsUserGroupAdmin(UserId);

				if (groupRight.IsGroupAdmin)
					isGroupAdmin = true;

				result.Data.Groups.Add(groupRight);
			}

			var createCallPermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateCall);
			var viewPIIPermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.ViewPersonalInfo);
			var createNotePermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateNote);
			var createMessagePermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateMessage);

			result.Data.CanViewPII = _permissionsService.IsUserAllowed(viewPIIPermission, result.Data.IsAdmin, isGroupAdmin, roles);
			result.Data.CanCreateCalls = _permissionsService.IsUserAllowed(createCallPermission, result.Data.IsAdmin, isGroupAdmin, roles);
			result.Data.CanAddNote = _permissionsService.IsUserAllowed(createNotePermission, result.Data.IsAdmin, isGroupAdmin, roles);
			result.Data.CanCreateMessage = _permissionsService.IsUserAllowed(createMessagePermission, result.Data.IsAdmin, isGroupAdmin, roles);

			var novuSuccess = await _novuProvider.CreateUserSubscriber(UserId, department.Code, DepartmentId, profile.MembershipEmail, profile.FirstName, profile.LastName);

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}
	}
}
