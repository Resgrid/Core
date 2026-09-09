using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public sealed class UnavailableChecklistAssetSource : IChecklistAssetSource
	{
		public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(false);
		public Task<List<ChecklistAssetTarget>> ListAsync(ChecklistActor actor) => Task.FromResult(new List<ChecklistAssetTarget>());
		public Task<ChecklistAssetTarget> GetAsync(ChecklistActor actor, string id) => Task.FromResult<ChecklistAssetTarget>(null);
		public Task<ChecklistAssetTarget> RoutingAsync(int departmentId, string id) => Task.FromResult<ChecklistAssetTarget>(null);
		public Task<bool> CanReceiveReminderAsync(int departmentId, string userId, string id) => Task.FromResult(false);
	}
	public sealed class ChecklistAssignmentService : IChecklistAssignmentService
	{
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUnitsService _units;
		private readonly IPersonnelRolesService _roles;
		public ChecklistAssignmentService(IDepartmentsService departments, IDepartmentGroupsService groups, IUnitsService units, IPersonnelRolesService roles)
		{ _departments = departments; _groups = groups; _units = units; _roles = roles; }
		public async Task ValidateAsync(int departmentId, int type, string id)
		{
			if (type == (int)ChecklistAssignmentType.Automatic && string.IsNullOrEmpty(id)) return;
			if (!Enum.IsDefined(typeof(ChecklistAssignmentType), type) || string.IsNullOrWhiteSpace(id) || id.Length > 128) throw new ChecklistException(400, "AssignmentUnavailable");
			var valid = false;
			if (type == (int)ChecklistAssignmentType.User) { var m = await _departments.GetDepartmentMemberAsync(id, departmentId, true); valid = m?.DepartmentId == departmentId && !m.IsDeleted && m.IsDisabled != true; }
			else if (int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric))
			{
				valid = (ChecklistAssignmentType)type switch { ChecklistAssignmentType.Role => (await _roles.GetRoleByIdAsync(numeric))?.DepartmentId == departmentId,
					ChecklistAssignmentType.Group => (await _groups.GetGroupByIdAsync(numeric, true))?.DepartmentId == departmentId, ChecklistAssignmentType.Unit => (await _units.GetUnitByIdAsync(numeric))?.DepartmentId == departmentId, _ => false };
			}
			if (!valid) throw new ChecklistException(400, "AssignmentUnavailable");
		}
		public async Task<HashSet<string>> MembersAsync(int departmentId, int type, string id)
		{
			try { await ValidateAsync(departmentId, type, id); } catch (ChecklistException) { return new HashSet<string>(); }
			var current = (await _departments.GetAllMembersForDepartmentUnlimitedAsync(departmentId, true)).Where(m => m.DepartmentId == departmentId && !m.IsDeleted && m.IsDisabled != true).Select(m => m.UserId).ToHashSet(StringComparer.Ordinal);
			IEnumerable<string> assigned = (ChecklistAssignmentType)type switch
			{
				ChecklistAssignmentType.Automatic => current, ChecklistAssignmentType.User => new[] { id },
				ChecklistAssignmentType.Role => (await _roles.GetAllMembersOfRoleAsync(int.Parse(id, CultureInfo.InvariantCulture))).Select(m => m.UserId),
				ChecklistAssignmentType.Group => (await _groups.GetAllMembersForGroupAsync(int.Parse(id, CultureInfo.InvariantCulture))).Where(m => m.DepartmentId == departmentId).Select(m => m.UserId),
				ChecklistAssignmentType.Unit => (await _units.GetActiveRolesForUnitAsync(int.Parse(id, CultureInfo.InvariantCulture))).Where(m => m.DepartmentId == departmentId).Select(m => m.UserId),
				_ => Array.Empty<string>()
			};
			current.IntersectWith(assigned); return current;
		}
		public async Task<bool> CanPerformAsync(ChecklistActor actor, ChecklistSchedule schedule) => schedule != null && schedule.DepartmentId == actor.DepartmentId && (schedule.AssignmentType == 0 || (await MembersAsync(actor.DepartmentId, schedule.AssignmentType, schedule.AssignmentId)).Contains(actor.UserId));
		public async Task<List<ChecklistAssignmentChoice>> ChoicesAsync(ChecklistActor actor)
		{
			var choices = new List<ChecklistAssignmentChoice>();
			foreach (var member in (await _departments.GetAllMembersForDepartmentUnlimitedAsync(actor.DepartmentId, true)).Where(m => m.DepartmentId == actor.DepartmentId && !m.IsDeleted && m.IsDisabled != true))
				choices.Add(new ChecklistAssignmentChoice { Type = 1, Id = member.UserId, Name = member.User?.UserName ?? member.UserId });
			foreach (var role in (await _roles.GetRolesForDepartmentUnlimitedAsync(actor.DepartmentId)).Where(r => r.DepartmentId == actor.DepartmentId)) choices.Add(new ChecklistAssignmentChoice { Type = 2, Id = role.PersonnelRoleId.ToString(CultureInfo.InvariantCulture), Name = role.Name });
			foreach (var group in (await _groups.GetAllGroupsForDepartmentUnlimitedThinAsync(actor.DepartmentId)).Where(g => g.DepartmentId == actor.DepartmentId)) choices.Add(new ChecklistAssignmentChoice { Type = 3, Id = group.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), Name = group.Name });
			foreach (var unit in (await _units.GetUnitsForDepartmentAsync(actor.DepartmentId)).Where(u => u.DepartmentId == actor.DepartmentId)) choices.Add(new ChecklistAssignmentChoice { Type = 4, Id = unit.UnitId.ToString(CultureInfo.InvariantCulture), Name = unit.Name });
			return choices;
		}
	}
}
