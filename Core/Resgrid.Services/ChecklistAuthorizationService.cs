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
		private readonly IChecklistAssetSource _assets;
		public ChecklistAuthorizationService(IDepartmentsService departments, IDepartmentGroupsService groups, IPersonnelRolesService roles, IPermissionsService permissions, IUnitsService units, IAuthorizationService authorization, IUserProfileService profiles, IChecklistAssetSource assets = null)
		{ _departments = departments; _groups = groups; _roles = roles; _permissions = permissions; _units = units; _authorization = authorization; _profiles = profiles; _assets = assets; }
		public async Task RequireMemberAsync(ChecklistActor actor)
		{
			if (actor == null || actor.DepartmentId <= 0 || string.IsNullOrWhiteSpace(actor.UserId)) throw new ChecklistException(403, "Active department membership is required.");
			var member = await _departments.GetDepartmentMemberAsync(actor.UserId, actor.DepartmentId, true);
			if (member == null || member.IsDeleted || member.IsDisabled.GetValueOrDefault()) throw new ChecklistException(403, "Active department membership is required.");
		}
		private async Task<bool> AllowedAsync(ChecklistActor actor, PermissionTypes type, PermissionActions fallback, int? targetGroup = null)
			=> (await PermissionFilterAsync(actor, type, fallback))(targetGroup);
		private async Task<Func<int?, bool>> PermissionFilterAsync(ChecklistActor actor, PermissionTypes type, PermissionActions fallback)
		{
			await RequireMemberAsync(actor);
			var member = await _departments.GetDepartmentMemberAsync(actor.UserId, actor.DepartmentId, true);
			var department = await _departments.GetDepartmentByIdAsync(actor.DepartmentId, true);
			var admin = member.IsAdmin.GetValueOrDefault() || department?.ManagingUserId == actor.UserId;
			var group = await _groups.GetGroupForUserAsync(actor.UserId, actor.DepartmentId);
			var permission = await _permissions.GetPermissionByDepartmentTypeAsync(actor.DepartmentId, type);
			var allowed = RecordPermissionEvaluation.IsSatisfied(permission?.Action ?? (int)fallback, permission?.Data, admin, group?.IsUserGroupAdmin(actor.UserId) == true, await _roles.GetRolesForUserAsync(actor.UserId, actor.DepartmentId));
			var lockToGroup = permission?.LockToGroup ?? type == PermissionTypes.ViewChecklistResults;
			var groupId = group?.DepartmentGroupId;
			return targetGroup => allowed && (admin || !lockToGroup || targetGroup.HasValue && targetGroup == groupId);
		}
		public Task<bool> CanManageAsync(ChecklistActor actor) => AllowedAsync(actor, PermissionTypes.ManageChecklists, PermissionActions.DepartmentAdminsOnly);
		public async Task<bool> CanReadAsync(ChecklistActor actor, ChecklistCompletion completion)
			=> await (await ReadFilterAsync(actor))(completion);
		public async Task<Func<ChecklistCompletion, Task<bool>>> ReadFilterAsync(ChecklistActor actor)
		{
			await RequireMemberAsync(actor);
			// This snapshot belongs to one read operation. Later commands recheck membership and permissions.
			Task<Func<int?, bool>> permission = null;
			return async completion =>
			{
				if (completion == null || completion.DepartmentId != actor.DepartmentId) return false;
				if (completion.CreatedBy == actor.UserId || completion.WitnessUserId == actor.UserId) return true;
				int? groupId = completion.TargetGroupId;
				if (completion.TargetType == (int)ChecklistTargetType.Group && int.TryParse(completion.TargetId, out var id)) groupId = id;
				var allowed = await (permission ??= PermissionFilterAsync(actor, PermissionTypes.ViewChecklistResults, PermissionActions.DepartmentAndGroupAdmins));
				return allowed(groupId);
			};
		}
		public async Task<ChecklistTarget> TargetAsync(ChecklistActor actor, ChecklistTargetType type, string id)
		{
			await RequireMemberAsync(actor);
			return await TargetCoreAsync(actor, type, id, () => _groups.GetGroupForUserAsync(actor.UserId, actor.DepartmentId), () => CanManageAsync(actor));
		}
		private async Task<ChecklistTarget> TargetCoreAsync(ChecklistActor actor, ChecklistTargetType type, string id,
			Func<Task<DepartmentGroup>> ownGroup, Func<Task<bool>> canManage)
		{
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
				var own = await ownGroup();
				if (group?.DepartmentId == actor.DepartmentId && (groupId == own?.DepartmentGroupId || await canManage())) { name = group.Name; targetGroupId = group.DepartmentGroupId; }
			}
			else if (type == ChecklistTargetType.Personnel)
			{
				var member = await _departments.GetDepartmentMemberAsync(id, actor.DepartmentId, true);
				if (member != null && !member.IsDeleted && !member.IsDisabled.GetValueOrDefault() && (id == actor.UserId || await _authorization.CanUserViewPersonAsync(actor.UserId, id, actor.DepartmentId))) { name = (await _profiles.GetProfileByUserIdAsync(id))?.FullName?.AsFirstNameLastName ?? id; targetGroupId = (await _groups.GetGroupForUserAsync(id, actor.DepartmentId))?.DepartmentGroupId; }
			}
			else if (type == ChecklistTargetType.InventoryAsset && Guid.TryParse(id, out _) && _assets != null && await _assets.IsAvailableAsync(actor.DepartmentId))
			{
				var asset = await _assets.GetAsync(actor, id);
				if (asset?.DepartmentId == actor.DepartmentId && asset.Id == id) { name = asset.Name; targetGroupId = asset.GroupId; }
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
				case ChecklistTargetType.InventoryAsset:
					if (_assets == null || !await _assets.IsAvailableAsync(actor.DepartmentId)) return new List<ChecklistTarget>();
					ids = (await _assets.ListAsync(actor)).Where(a => a.DepartmentId == actor.DepartmentId).Select(a => a.Id); break;
				default: return new List<ChecklistTarget>();
			}
			var result = new List<ChecklistTarget>();
			// Share only actor state within this listing. Recheck each target and every later command.
			Task<DepartmentGroup> ownGroup = null;
			Task<bool> canManage = null;
			foreach (var id in ids)
				try { result.Add(await TargetCoreAsync(actor, type, id,
					() => ownGroup ??= _groups.GetGroupForUserAsync(actor.UserId, actor.DepartmentId),
					() => canManage ??= CanManageAsync(actor))); } catch (ChecklistException ex) when (ex.StatusCode == 404) { }
			return result;
		}
	}
}
