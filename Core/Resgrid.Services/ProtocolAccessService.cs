using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The protocol read rule (R-P4) for every path. The reader's rights come from the department's permission rows (Author 128,
	/// Approve 129, Manage Acknowledgments 130; no row = department admins only), never from token claims, so the web, the API,
	/// search and workers decide alike. Memberships come from the shared audience resolver (T0), not a second one.
	/// </summary>
	public class ProtocolAccessService : IProtocolAccessService
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IPermissionsService _permissionsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IContentAudienceService _contentAudienceService;
		private readonly IDispatchAccessService _dispatchAccessService;
		private readonly IProtocolAudiencesRepository _audiences;
		private readonly IDispatchProtocolRepository _protocols;

		public ProtocolAccessService(IDepartmentsService departmentsService, IPermissionsService permissionsService, IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService, IContentAudienceService contentAudienceService, IDispatchAccessService dispatchAccessService,
			IProtocolAudiencesRepository audiences, IDispatchProtocolRepository protocols)
		{
			_departmentsService = departmentsService;
			_permissionsService = permissionsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_contentAudienceService = contentAudienceService;
			_dispatchAccessService = dispatchAccessService;
			_audiences = audiences;
			_protocols = protocols;
		}

		public async Task<ProtocolReader> GetReaderAsync(int departmentId, string userId)
		{
			var reader = new ProtocolReader { DepartmentId = departmentId, UserId = userId };
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return reader;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			if (department == null || department.DepartmentId != departmentId)
				return reader;

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, false);
			var owner = string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase);
			reader.IsCurrentMember = owner || DepartmentMemberStateHelper.IsCurrentMember(member, departmentId);
			if (!reader.IsCurrentMember)
				return reader;

			reader.IsDepartmentAdmin = owner || department.IsUserAnAdmin(userId);

			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var isGroupAdmin = group != null && group.DepartmentId == departmentId && group.IsUserGroupAdmin(userId);
			var roleIds = ((await _personnelRolesService.GetRolesForUserAsync(userId, departmentId)) ?? new List<PersonnelRole>())
				.Where(r => r != null && r.DepartmentId == departmentId).Select(r => r.PersonnelRoleId).ToList();

			reader.CanAuthor = PermissionRights.Holds(await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.AuthorProtocols), reader.IsDepartmentAdmin, isGroupAdmin, roleIds);
			reader.CanApprove = PermissionRights.Holds(await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ApproveProtocols), reader.IsDepartmentAdmin, isGroupAdmin, roleIds);
			reader.CanManageAcknowledgments = PermissionRights.Holds(await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ManageProtocolAcknowledgments), reader.IsDepartmentAdmin, isGroupAdmin, roleIds);
			reader.CanReviewDispatchCases = PermissionRights.Holds(await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ReviewDispatchCases), reader.IsDepartmentAdmin, isGroupAdmin, roleIds);

			try
			{
				reader.IsDispatcher = _dispatchAccessService != null && await _dispatchAccessService.CanUseDispatchAsync(departmentId, userId);
			}
			catch (Exception ex)
			{
				// Without the dispatch verdict the member still reads what their audience gives them.
				Logging.LogException(ex, "Dispatch access could not be read for the protocol library.");
			}

			reader.Facts = await _contentAudienceService.GetMemberFactsAsync(departmentId, userId) ?? new ContentAudienceMemberFacts { UserId = userId };
			reader.Facts.RoleIds ??= new HashSet<int>();
			foreach (var roleId in roleIds)
				reader.Facts.RoleIds.Add(roleId);

			return reader;
		}

		public async Task<List<DispatchProtocol>> FilterReadableAsync(ProtocolReader reader, IEnumerable<DispatchProtocol> protocols)
		{
			var list = (protocols ?? Enumerable.Empty<DispatchProtocol>()).Where(p => p != null).ToList();
			if (reader == null || list.Count == 0 || !reader.IsCurrentMember)
				return new List<DispatchProtocol>();

			if (reader.IsManager)
				return list.Where(p => p.DepartmentId == reader.DepartmentId).ToList();

			var audiences = (await _audiences.GetForDepartmentAsync(reader.DepartmentId) ?? new List<ProtocolAudience>())
				.Where(a => a.DepartmentId == reader.DepartmentId)
				.ToLookup(a => a.DispatchProtocolId);

			return list.Where(p => ProtocolAccessRules.CanRead(reader, p, audiences[p.DispatchProtocolId])).ToList();
		}

		public async Task<bool> CanReadAsync(ProtocolReader reader, DispatchProtocol protocol)
		{
			if (reader == null || protocol == null)
				return false;

			if (reader.IsManager)
				return ProtocolAccessRules.CanRead(reader, protocol, null);

			if (!protocol.IsPublished || protocol.DepartmentId != reader.DepartmentId)
				return false;

			var audience = protocol.Audiences ?? await _audiences.GetForProtocolAsync(reader.DepartmentId, protocol.DispatchProtocolId) ?? new List<ProtocolAudience>();
			return ProtocolAccessRules.CanRead(reader, protocol, audience.Where(a => a.DispatchProtocolId == protocol.DispatchProtocolId));
		}

		public async Task<DispatchProtocol> GetReadableAsync(int departmentId, string userId, int protocolId)
		{
			if (protocolId <= 0)
				return null;

			var protocol = await _protocols.GetByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != departmentId)
				return null;

			var reader = await GetReaderAsync(departmentId, userId);
			return await CanReadAsync(reader, protocol) ? protocol : null;
		}
	}
}
