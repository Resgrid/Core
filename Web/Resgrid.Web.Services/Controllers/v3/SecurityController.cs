using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Security;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;


namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against the security sub-system
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class SecurityController : V3AuthenticatedApiControllerbase
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPermissionsService _permissionsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IFirebaseService _firebaseService;

		/// <summary>
		/// Operations to perform against the security sub-system
		/// </summary>
		public SecurityController(IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService, 
			IPermissionsService permissionsService, IPersonnelRolesService personnelRolesService, IFirebaseService firebaseService)
		{
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_permissionsService = permissionsService;
			_personnelRolesService = personnelRolesService;
			_firebaseService = firebaseService;
		}

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
				result.Adm = departmentMembership.IsAdmin.Value;

			if (department.ManagingUserId == UserId)
				result.Adm = true;

			bool isGroupAdmin = false;
			result.Grps = new List<GroupRight>();

			var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);

			if (group != null)
			{
				var groupRight = new GroupRight();
				groupRight.Gid = group.DepartmentGroupId;
				groupRight.Adm = group.IsUserGroupAdmin(UserId);

				if (groupRight.Adm)
					isGroupAdmin = true;

				result.Grps.Add(groupRight);
			}

			var createCallPermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateCall);
			var viewPIIPermission = await  _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.ViewPersonalInfo);
			var createNotePermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateNote);
			var createMessagePermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateMessage);

			result.VPii = _permissionsService.IsUserAllowed(viewPIIPermission, result.Adm, isGroupAdmin, roles);
			result.CCls = _permissionsService.IsUserAllowed(createCallPermission, result.Adm, isGroupAdmin, roles);
			result.ANot = _permissionsService.IsUserAllowed(createNotePermission, result.Adm, isGroupAdmin, roles);
			result.CMsg = _permissionsService.IsUserAllowed(createMessagePermission, result.Adm, isGroupAdmin, roles);

			if (!String.IsNullOrWhiteSpace(Config.FirebaseConfig.ResponderJsonFile) && !String.IsNullOrWhiteSpace(Config.FirebaseConfig.ResponderProjectEmail))
			{
				result.FirebaseApiToken = await _firebaseService.CreateTokenAsync(UserId, null);
			}

			return result;
		}
	}
}
