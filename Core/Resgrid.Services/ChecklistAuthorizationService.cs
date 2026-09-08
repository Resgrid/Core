using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ChecklistAuthorizationService : IChecklistAuthorizationService
	{
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly IPermissionsService _permissions;
		private readonly IUnitsService _units;
		private readonly IAuthorizationService _authorization;
		private readonly IUserProfileService _profiles;
		public ChecklistAuthorizationService(IDepartmentsService departments, IDepartmentGroupsService groups, IPersonnelRolesService roles, IPermissionsService permissions, IUnitsService units, IAuthorizationService authorization, IUserProfileService profiles)
		{ _departments = departments; _groups = groups; _roles = roles; _permissions = permissions; _units = units; _authorization = authorization; _profiles = profiles; }
		public async Task RequireMemberAsync(ChecklistActor actor)
		{
			if (actor == null || actor.DepartmentId <= 0 || string.IsNullOrWhiteSpace(actor.UserId)) throw new ChecklistException(403, "Active department membership is required.");
			var member = await _departments.GetDepartmentMemberAsync(actor.UserId, actor.DepartmentId, true);
			if (member == null || member.IsDeleted || member.IsDisabled.GetValueOrDefault()) throw new ChecklistException(403, "Active department membership is required.");
		}
		private async Task<bool> AllowedAsync(ChecklistActor actor, PermissionTypes type, PermissionActions fallback, int? targetGroup = null)
		{
			await RequireMemberAsync(actor);
			var member = await _departments.GetDepartmentMemberAsync(actor.UserId, actor.DepartmentId, true);
			var department = await _departments.GetDepartmentByIdAsync(actor.DepartmentId, true);
			var admin = member.IsAdmin.GetValueOrDefault() || department?.ManagingUserId == actor.UserId;
			var group = await _groups.GetGroupForUserAsync(actor.UserId, actor.DepartmentId);
			var permission = await _permissions.GetPermissionByDepartmentTypeAsync(actor.DepartmentId, type);
			if (!RecordPermissionEvaluation.IsSatisfied(permission?.Action ?? (int)fallback, permission?.Data, admin, group?.IsUserGroupAdmin(actor.UserId) == true, await _roles.GetRolesForUserAsync(actor.UserId, actor.DepartmentId))) return false;
			var lockToGroup = permission?.LockToGroup ?? type == PermissionTypes.ViewChecklistResults;
			return admin || !lockToGroup || targetGroup.HasValue && targetGroup == group?.DepartmentGroupId;
		}
		public Task<bool> CanManageAsync(ChecklistActor actor) => AllowedAsync(actor, PermissionTypes.ManageChecklists, PermissionActions.DepartmentAdminsOnly);
		public async Task<bool> CanReadAsync(ChecklistActor actor, ChecklistCompletion completion)
		{
			await RequireMemberAsync(actor);
			if (completion == null || completion.DepartmentId != actor.DepartmentId) return false;
			if (completion.CreatedBy == actor.UserId || completion.WitnessUserId == actor.UserId) return true;
			int? groupId = completion.TargetGroupId;
			if (completion.TargetType == (int)ChecklistTargetType.Group && int.TryParse(completion.TargetId, out var id)) groupId = id;
			return await AllowedAsync(actor, PermissionTypes.ViewChecklistResults, PermissionActions.DepartmentAndGroupAdmins, groupId);
		}
		public async Task<ChecklistTarget> TargetAsync(ChecklistActor actor, ChecklistTargetType type, string id)
		{
			await RequireMemberAsync(actor);
			if (id == null || id.Length > 128) throw new ChecklistException(400, "Select a target.");
			string name = null; int? targetGroupId = null;
			if (type == ChecklistTargetType.Department && id == actor.DepartmentId.ToString()) name = (await _departments.GetDepartmentByIdAsync(actor.DepartmentId, true))?.Name;
			else if (type == ChecklistTargetType.Unit && int.TryParse(id, out var unitId))
			{
				var unit = await _units.GetUnitByIdAsync(unitId);
				if (unit?.DepartmentId == actor.DepartmentId && await _authorization.CanUserViewUnitAsync(actor.UserId, unitId)) { name = unit.Name; targetGroupId = unit.StationGroupId; }
			}
			else if (type == ChecklistTargetType.Group && int.TryParse(id, out var groupId))
			{
				var group = await _groups.GetGroupByIdAsync(groupId, true);
				var own = await _groups.GetGroupForUserAsync(actor.UserId, actor.DepartmentId);
				if (group?.DepartmentId == actor.DepartmentId && (groupId == own?.DepartmentGroupId || await CanManageAsync(actor))) { name = group.Name; targetGroupId = group.DepartmentGroupId; }
			}
			else if (type == ChecklistTargetType.Personnel)
			{
				var member = await _departments.GetDepartmentMemberAsync(id, actor.DepartmentId, true);
				if (member != null && !member.IsDeleted && !member.IsDisabled.GetValueOrDefault() && (id == actor.UserId || await _authorization.CanUserViewPersonAsync(actor.UserId, id, actor.DepartmentId))) { name = (await _profiles.GetProfileByUserIdAsync(id))?.FullName?.AsFirstNameLastName ?? id; targetGroupId = (await _groups.GetGroupForUserAsync(id, actor.DepartmentId))?.DepartmentGroupId; }
			}
			if (name == null) throw new ChecklistException(404, "Target is unavailable.");
			return new ChecklistTarget { Type = type, Id = id, Name = name, GroupId = targetGroupId };
		}
		public async Task<List<ChecklistTarget>> TargetsAsync(ChecklistActor actor, ChecklistTargetType type)
		{
			await RequireMemberAsync(actor);
			IEnumerable<string> ids;
			switch (type)
			{
				case ChecklistTargetType.Department: ids = new[] { actor.DepartmentId.ToString() }; break;
				case ChecklistTargetType.Unit: ids = (await _units.GetUnitsForDepartmentAsync(actor.DepartmentId)).Select(u => u.UnitId.ToString()); break;
				case ChecklistTargetType.Group: ids = (await _groups.GetAllGroupsForDepartmentAsync(actor.DepartmentId)).Select(g => g.DepartmentGroupId.ToString()); break;
				case ChecklistTargetType.Personnel: ids = (await _departments.GetAllMembersForDepartmentAsync(actor.DepartmentId)).Where(m => !m.IsDeleted && !m.IsDisabled.GetValueOrDefault()).Select(m => m.UserId); break;
				default: return new List<ChecklistTarget>();
			}
			var result = new List<ChecklistTarget>();
			foreach (var id in ids)
				try { result.Add(await TargetAsync(actor, type, id)); } catch (ChecklistException ex) when (ex.StatusCode == 404) { }
			return result;
		}
	}
}
