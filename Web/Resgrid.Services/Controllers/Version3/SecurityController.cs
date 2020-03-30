using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Http.Cors;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Security;
using System.Net.Http;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against the security sub-system
	/// </summary>
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
		public DepartmentRightsResult GetCurrentUsersRights()
		{
			var result = new DepartmentRightsResult();
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);
			var departmentMembership = _departmentsService.GetDepartmentMember(UserId, DepartmentId);
			var roles = _personnelRolesService.GetRolesForUser(UserId, DepartmentId);

			if (departmentMembership == null)
				throw HttpStatusCode.Unauthorized.AsException();

			if (departmentMembership.IsAdmin.HasValue)
				result.Adm = departmentMembership.IsAdmin.Value;

			if (department.ManagingUserId == UserId)
				result.Adm = true;

			bool isGroupAdmin = false;
			result.Grps = new List<GroupRight>();

			var group = _departmentGroupsService.GetGroupForUser(UserId, DepartmentId);

			if (group != null)
			{
				var groupRight = new GroupRight();
				groupRight.Gid = group.DepartmentGroupId;
				groupRight.Adm = group.IsUserGroupAdmin(UserId);

				if (groupRight.Adm)
					isGroupAdmin = true;

				result.Grps.Add(groupRight);
			}

			var createCallPermission = _permissionsService.GetPermisionByDepartmentType(DepartmentId, PermissionTypes.CreateCall);
			var viewPIIPermission = _permissionsService.GetPermisionByDepartmentType(DepartmentId, PermissionTypes.ViewPersonalInfo);
			var createNotePermission = _permissionsService.GetPermisionByDepartmentType(DepartmentId, PermissionTypes.CreateNote);
			var createMessagePermission = _permissionsService.GetPermisionByDepartmentType(DepartmentId, PermissionTypes.CreateMessage);

			result.VPii = _permissionsService.IsUserAllowed(viewPIIPermission, result.Adm, isGroupAdmin, roles);
			result.CCls = _permissionsService.IsUserAllowed(createCallPermission, result.Adm, isGroupAdmin, roles);
			result.ANot = _permissionsService.IsUserAllowed(createNotePermission, result.Adm, isGroupAdmin, roles);
			result.CMsg = _permissionsService.IsUserAllowed(createMessagePermission, result.Adm, isGroupAdmin, roles);

			if (!String.IsNullOrWhiteSpace(Config.FirebaseConfig.ResponderJsonFile) && !String.IsNullOrWhiteSpace(Config.FirebaseConfig.ResponderProjectEmail))
			{
				result.FirebaseApiToken = _firebaseService.CreateToken(UserId, null);
			}

			return result;
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}
}
