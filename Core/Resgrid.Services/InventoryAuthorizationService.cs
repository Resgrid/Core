using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public sealed class InventoryAuthorizationService : IInventoryAuthorizationService
	{
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUnitsService _units;
		private readonly IAuthorizationService _resources;
		private readonly IPermissionsService _permissions;
		private readonly IPersonnelRolesService _roles;
		private readonly IDepartmentSettingsService _settings;
		public InventoryAuthorizationService(IDepartmentsService departments, IDepartmentGroupsService groups, IUnitsService units,
			IAuthorizationService resources, IPermissionsService permissions, IPersonnelRolesService roles, IDepartmentSettingsService settings)
		{ _departments = departments; _groups = groups; _units = units; _resources = resources; _permissions = permissions; _roles = roles; _settings = settings; }
		public async Task<bool> IsEnabledAsync(int departmentId) => departmentId > 0 && (await _settings.GetDepartmentModuleSettingsAsync(departmentId, true))?.InventoryDisabled != true;
		public async Task RequireAsync(InventoryActor actor, bool write = false, PermissionTypes? permission = null, int? groupId = null)
		{
			if (actor == null || actor.DepartmentId <= 0 || string.IsNullOrWhiteSpace(actor.UserId)) throw new InventoryException(403, "MembershipRequired");
			var member = await _departments.GetDepartmentMemberAsync(actor.UserId, actor.DepartmentId, true);
			if (member?.DepartmentId != actor.DepartmentId || member.IsDeleted || member.IsDisabled == true) throw new InventoryException(403, "MembershipRequired");
			if (write && !await IsEnabledAsync(actor.DepartmentId)) throw new InventoryException(409, "InventoryDisabled");
			if (!write && !permission.HasValue) return;
			var department = await _departments.GetDepartmentByIdAsync(actor.DepartmentId, true);
			var admin = member.IsAdmin == true || department?.ManagingUserId == actor.UserId;
			var group = await _groups.GetGroupForUserAsync(actor.UserId, actor.DepartmentId);
			var type = permission ?? PermissionTypes.AdjustInventory;
			var rule = await _permissions.GetPermissionByDepartmentTypeAsync(actor.DepartmentId, type);
			if (rule == null && (type == PermissionTypes.TransferInventory || type == PermissionTypes.IssueInventory))
				rule = await _permissions.GetPermissionByDepartmentTypeAsync(actor.DepartmentId, PermissionTypes.AdjustInventory);
			if (!RecordPermissionEvaluation.IsSatisfied(rule?.Action ?? (int)PermissionActions.DepartmentAdminsOnly, rule?.Data, admin, group?.IsUserGroupAdmin(actor.UserId) == true,
				await _roles.GetRolesForUserAsync(actor.UserId, actor.DepartmentId)) || !admin && rule?.LockToGroup == true && (!groupId.HasValue || groupId != group?.DepartmentGroupId))
				throw new InventoryException(403, "PermissionRequired");
		}
		public async Task<bool> CanLocationAsync(InventoryActor actor, InventoryLocation location)
		{
			if (location?.DepartmentId != actor.DepartmentId) return false;
			if (location.UnitId.HasValue) return (await _units.GetUnitByIdAsync(location.UnitId.Value))?.DepartmentId == actor.DepartmentId && await _resources.CanUserViewUnitAsync(actor.UserId, location.UnitId.Value);
			if (location.UserId != null) return location.UserId == actor.UserId || await _resources.CanUserViewPersonAsync(actor.UserId, location.UserId, actor.DepartmentId);
			if (location.GroupId.HasValue)
			{
				if ((await _groups.GetGroupForUserAsync(actor.UserId, actor.DepartmentId))?.DepartmentGroupId == location.GroupId) return true;
				try { await RequireAsync(actor, false, PermissionTypes.AdjustInventory, location.GroupId); return true; } catch (InventoryException) { return false; }
			}
			return location.LocationType != (int)InventoryLocationType.Container; // Container authorization requires resolving its effective holder in the service.
		}
		public async Task ValidateHolderAsync(InventoryActor actor, InventoryLocation location)
		{
			var holders = (location.GroupId.HasValue ? 1 : 0) + (location.UnitId.HasValue ? 1 : 0) + (location.UserId != null ? 1 : 0) + (location.ContainerAssetId != null ? 1 : 0);
			var type = (InventoryLocationType)location.LocationType;
			if (!Enum.IsDefined(type) || holders != (type is InventoryLocationType.Facility or InventoryLocationType.External ? 0 : 1)
				|| type == InventoryLocationType.Station && !location.GroupId.HasValue || type == InventoryLocationType.Unit && !location.UnitId.HasValue
				|| type == InventoryLocationType.Personnel && location.UserId == null || type == InventoryLocationType.Container && location.ContainerAssetId == null)
				throw new InventoryException(400, "HolderRequired");
			if (location.GroupId.HasValue && (await _groups.GetGroupByIdAsync(location.GroupId.Value, true))?.DepartmentId != actor.DepartmentId) throw new InventoryException(404, "LocationUnavailable");
			if (location.UnitId.HasValue && (await _units.GetUnitByIdAsync(location.UnitId.Value))?.DepartmentId != actor.DepartmentId) throw new InventoryException(404, "LocationUnavailable");
			if (location.UserId != null)
			{
				var member = await _departments.GetDepartmentMemberAsync(location.UserId, actor.DepartmentId, true);
				if (member?.DepartmentId != actor.DepartmentId || member.IsDeleted || member.IsDisabled == true) throw new InventoryException(404, "LocationUnavailable");
			}
			if (type != InventoryLocationType.Container && !await CanLocationAsync(actor, location)) throw new InventoryException(404, "LocationUnavailable");
		}
	}
}
