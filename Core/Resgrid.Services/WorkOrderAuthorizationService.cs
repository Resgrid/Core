using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
	public sealed class WorkOrderAuthorizationService : IWorkOrderAuthorizationService
	{
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly IPermissionsService _permissions;
		private readonly IUnitsService _units;
		private readonly IAuthorizationService _resources;
		private readonly IChecklistAssignmentService _assignments;
		private readonly IChecklistAssetSource _assets;
		public WorkOrderAuthorizationService(IDepartmentsService departments, IDepartmentGroupsService groups, IPersonnelRolesService roles, IPermissionsService permissions,
			IUnitsService units, IAuthorizationService resources, IChecklistAssignmentService assignments, IChecklistAssetSource assets = null)
		{ _departments = departments; _groups = groups; _roles = roles; _permissions = permissions; _units = units; _resources = resources; _assignments = assignments; _assets = assets; }
		public async Task RequireMemberAsync(ChecklistActor actor) => await MemberAsync(actor);
		private async Task<DepartmentMember> MemberAsync(ChecklistActor actor)
		{
			if (actor == null || actor.DepartmentId <= 0 || string.IsNullOrWhiteSpace(actor.UserId)) throw new WorkOrderException(403, "MembershipRequired");
			var m = await _departments.GetDepartmentMemberAsync(actor.UserId, actor.DepartmentId, true);
			if (m?.DepartmentId != actor.DepartmentId || m.IsDeleted || m.IsDisabled == true) throw new WorkOrderException(403, "MembershipRequired");
			return m;
		}
		private sealed class ActorContext
		{
			public ChecklistActor Actor;
			public bool Admin;
			public DepartmentGroup Group;
			public List<PersonnelRole> Roles;
			public readonly Dictionary<PermissionTypes, Permission> Permissions = new();
		}
		private async Task<ActorContext> ContextAsync(ChecklistActor actor)
		{
			var member = await MemberAsync(actor);
			var department = await _departments.GetDepartmentByIdAsync(actor.DepartmentId, true);
			return new ActorContext { Actor = actor, Admin = member.IsAdmin == true || department?.ManagingUserId == actor.UserId,
				Group = await _groups.GetGroupForUserAsync(actor.UserId, actor.DepartmentId), Roles = await _roles.GetRolesForUserAsync(actor.UserId, actor.DepartmentId) };
		}
		private async Task<bool> AllowedAsync(ActorContext context, PermissionTypes type, int? groupId)
		{
			var actor = context.Actor; var admin = context.Admin; var group = context.Group;
			if (!context.Permissions.TryGetValue(type, out var permission))
				context.Permissions[type] = permission = await _permissions.GetPermissionByDepartmentTypeAsync(actor.DepartmentId, type);
			var fallback = type == PermissionTypes.ManageWorkOrders ? PermissionActions.DepartmentAdminsOnly : PermissionActions.DepartmentAndGroupAdmins;
			if (!RecordPermissionEvaluation.IsSatisfied(permission?.Action ?? (int)fallback, permission?.Data, admin, group?.IsUserGroupAdmin(actor.UserId) == true, context.Roles)) return false;
			return admin || !(permission?.LockToGroup ?? type == PermissionTypes.ViewAllWorkOrders) || groupId.HasValue && groupId == group?.DepartmentGroupId;
		}
		public async Task<bool> CanManageAsync(ChecklistActor actor, int? groupId) => await AllowedAsync(await ContextAsync(actor), PermissionTypes.ManageWorkOrders, groupId);
		public async Task<WorkOrderReadScope> ScopeAsync(ChecklistActor actor)
		{
			var context = await ContextAsync(actor); var group = context.Group;
			var all = await AllowedAsync(context, PermissionTypes.ViewAllWorkOrders, null) || await AllowedAsync(context, PermissionTypes.ManageWorkOrders, null);
			var groupAllowed = group != null && (await AllowedAsync(context, PermissionTypes.ViewAllWorkOrders, group.DepartmentGroupId) || await AllowedAsync(context, PermissionTypes.ManageWorkOrders, group.DepartmentGroupId));
			return new WorkOrderReadScope { UserId = actor.UserId, All = all, GroupId = groupAllowed ? group.DepartmentGroupId : null,
				RoleIds = context.Roles.Where(r => r.DepartmentId == actor.DepartmentId).Select(r => r.PersonnelRoleId).ToArray() };
		}
		public async Task<bool> CanContributeAsync(ChecklistActor actor, WorkOrder row)
		{
			var context = await ContextAsync(actor);
			if (row?.DepartmentId != actor.DepartmentId) return false;
			if (await AllowedAsync(context, PermissionTypes.ManageWorkOrders, row.TargetGroupId)) return true;
			if (row.AssignedToUserId != null) return row.AssignedToUserId == actor.UserId;
			return row.AssignedToRoleId.HasValue && (await _assignments.MembersAsync(actor.DepartmentId, 2, row.AssignedToRoleId.Value.ToString())).Contains(actor.UserId);
		}
		public async Task ValidateTargetAsync(ChecklistActor actor, WorkOrderInput input)
		{
			await RequireMemberAsync(actor);
			if (input.TargetUnitId.HasValue)
			{
				var unit = await _units.GetUnitByIdAsync(input.TargetUnitId.Value);
				if (unit?.DepartmentId != actor.DepartmentId || !await _resources.CanUserViewUnitAsync(actor.UserId, unit.UnitId)) throw new WorkOrderException(404, "TargetUnavailable");
				if (input.TargetGroupId.HasValue && input.TargetGroupId != unit.StationGroupId) throw new WorkOrderException(400, "TargetUnavailable");
				input.TargetGroupId = unit.StationGroupId;
			}
			if (input.TargetGroupId.HasValue)
			{
				var group = await _groups.GetGroupByIdAsync(input.TargetGroupId.Value, true);
				var own = await _groups.GetGroupForUserAsync(actor.UserId, actor.DepartmentId);
				if (group?.DepartmentId != actor.DepartmentId || own?.DepartmentGroupId != group.DepartmentGroupId && !await CanManageAsync(actor, group.DepartmentGroupId)) throw new WorkOrderException(404, "TargetUnavailable");
			}
			if (!string.IsNullOrEmpty(input.InventoryAssetId))
			{
				try
				{
					if (!Guid.TryParseExact(input.InventoryAssetId, "D", out _) || _assets == null || !await _assets.IsAvailableAsync(actor.DepartmentId)) throw new WorkOrderException(404, "TargetUnavailable");
					var asset = await _assets.GetAsync(actor, input.InventoryAssetId);
					if (asset?.DepartmentId != actor.DepartmentId || asset.Id != input.InventoryAssetId || input.TargetUnitId.HasValue && input.TargetUnitId != asset.UnitId || input.TargetGroupId.HasValue && input.TargetGroupId != asset.GroupId) throw new WorkOrderException(404, "TargetUnavailable");
					input.TargetUnitId = asset.UnitId; input.TargetGroupId = asset.GroupId;
				}
				catch (ChecklistException ex) { throw new WorkOrderException(ex.StatusCode, ex.Message); }
			}
		}
		public async Task ValidateAssignmentAsync(ChecklistActor actor, WorkOrder row, string userId, int? roleId)
		{
			await RequireMemberAsync(actor);
			if ((string.IsNullOrEmpty(userId) ? 0 : 1) + (roleId.HasValue ? 1 : 0) != 1) throw new WorkOrderException(400, "AssignmentRequired");
			try { await _assignments.ValidateAsync(actor.DepartmentId, roleId.HasValue ? 2 : 1, roleId?.ToString() ?? userId); }
			catch (ChecklistException) { throw new WorkOrderException(400, "AssignmentRequired"); }
			if (userId != null && !await _resources.CanUserViewPersonAsync(actor.UserId, userId, actor.DepartmentId)) throw new WorkOrderException(403, "PermissionRequired");
		}
		public async Task<WorkOrderChoices> ChoicesAsync(ChecklistActor actor)
		{
			var context = await ContextAsync(actor); var result = new WorkOrderChoices();
			foreach (var c in await _assignments.ChoicesAsync(actor))
			{
				var choice = new WorkOrderChoice { Id = c.Id, Name = c.Name };
				if (c.Type == 1 && await _resources.CanUserViewPersonAsync(actor.UserId, c.Id, actor.DepartmentId)) result.Users.Add(choice);
				if (c.Type == 2) result.Roles.Add(choice);
				if (c.Type == 3 && (context.Group?.DepartmentGroupId.ToString() == c.Id || await AllowedAsync(context, PermissionTypes.ManageWorkOrders, int.Parse(c.Id)))) result.Groups.Add(choice);
				if (c.Type == 4 && await _resources.CanUserViewUnitAsync(actor.UserId, int.Parse(c.Id))) result.Units.Add(choice);
			}
			try
			{
				if (_assets != null && await _assets.IsAvailableAsync(actor.DepartmentId)) result.Assets = (await _assets.ListAsync(actor)).Where(a => a.DepartmentId == actor.DepartmentId).Select(a => new WorkOrderChoice { Id = a.Id, Name = a.Name }).ToList();
			}
			catch (ChecklistException ex) { throw new WorkOrderException(ex.StatusCode, ex.Message); }
			return result;
		}
		public async Task<List<string>> RecipientsAsync(int departmentId, WorkOrder row)
		{
			if (row?.DepartmentId != departmentId) return new List<string>();
			var members = row.AssignedToUserId != null ? await _assignments.MembersAsync(departmentId, 1, row.AssignedToUserId) : row.AssignedToRoleId.HasValue ? await _assignments.MembersAsync(departmentId, 2, row.AssignedToRoleId.Value.ToString()) : new HashSet<string>();
			return members.OrderBy(id => id, StringComparer.Ordinal).ToList();
		}
	}
}
