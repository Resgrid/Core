using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IDispatchScopeService" />
	public class DispatchScopeService : IDispatchScopeService
	{
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _callsService;

		/// <summary>
		/// The department's groups and units, loaded at most once per operation so a list filter doesn't
		/// reload them for every call. Deliberately not a field: worker code resolves services from the
		/// root container, where an instance cache would never see a group or boundary change.
		/// </summary>
		private sealed class ScopeData
		{
			public List<DepartmentGroup> Groups { get; set; }

			public List<Unit> Units { get; set; }
		}

		public DispatchScopeService(IDepartmentSettingsService departmentSettingsService, IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService,
			IUnitsService unitsService, ICallsService callsService)
		{
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_unitsService = unitsService;
			_callsService = callsService;
		}

		public async Task<DispatchScope> GetScopeForUserAsync(int departmentId, string userId)
		{
			var config = await _departmentSettingsService.GetGroupDispatchScopeConfigAsync(departmentId);

			if (config == null || !config.Enabled)
				return DispatchScope.DepartmentWide(departmentId, userId, DispatchScopeReasons.ScopingDisabled);

			var scope = new DispatchScope { DepartmentId = departmentId, UserId = userId, Reason = DispatchScopeReasons.NoGroup };

			if (string.IsNullOrWhiteSpace(userId))
				return scope;

			// Scoping on means the open default is gone: a non-member or disabled member gets an empty scope.
			var membership = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, false);
			if (membership == null || membership.IsDisabled.GetValueOrDefault() || membership.IsDeleted)
				return scope;

			var isDepartmentAdmin = membership.IsAdmin.GetValueOrDefault();

			if (!isDepartmentAdmin)
			{
				// The managing user is always an admin, the same carve-out the permission gates make.
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
				isDepartmentAdmin = department != null && string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase);
			}

			if (isDepartmentAdmin)
				return DispatchScope.DepartmentWide(departmentId, userId, DispatchScopeReasons.DepartmentAdmin);

			if (config.DepartmentWideRoleIds != null && config.DepartmentWideRoleIds.Any())
			{
				// Read uncached on purpose: assigning the dispatch-center role at shift change has to
				// widen the view on the next request.
				var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId) ?? new List<PersonnelRole>();

				if (roles.Any(r => r != null && config.DepartmentWideRoleIds.Contains(r.PersonnelRoleId)))
					return DispatchScope.DepartmentWide(departmentId, userId, DispatchScopeReasons.DepartmentWideRole);
			}

			var groups = await GetGroupsAsync(departmentId, new ScopeData());
			var memberships = groups.Where(g => g.IsUserInGroup(userId)).ToList();

			if (!memberships.Any())
				return scope;

			// Membership is one group per user in the UI, but take every row the data holds so a
			// stray second membership widens the scope instead of silently hiding calls.
			var adminOf = memberships.FirstOrDefault(g => g.IsUserGroupAdmin(userId));
			scope.Reason = adminOf != null ? DispatchScopeReasons.GroupAdmin : DispatchScopeReasons.GroupMember;
			scope.AnchorGroupId = (adminOf ?? memberships.First()).DepartmentGroupId;

			foreach (var group in memberships)
				scope.GroupIds.UnionWith(DepartmentGroupHierarchy.GetSelfAndDescendantIds(groups, group.DepartmentGroupId));

			return scope;
		}

		public Task<bool> IsCallInScopeAsync(DispatchScope scope, Call call)
		{
			return IsCallInScopeAsync(scope, call, new ScopeData());
		}

		private async Task<bool> IsCallInScopeAsync(DispatchScope scope, Call call, ScopeData data)
		{
			if (scope == null || call == null || call.DepartmentId != scope.DepartmentId)
				return false;

			if (scope.IsDepartmentWide)
				return true;

			if (!string.IsNullOrWhiteSpace(scope.UserId) && string.Equals(call.ReportingUserId, scope.UserId, StringComparison.OrdinalIgnoreCase))
				return true;

			var groups = scope.GroupIds.Count > 0 ? await GetGroupsAsync(scope.DepartmentId, data) : new List<DepartmentGroup>();

			// Cheapest test first: a call located inside one of the scope's boundaries needs no dispatch lookups.
			var point = GeoMath.ParseLatLonString(call.GeoLocationData);
			if (point.HasValue && groups.Any(g => scope.GroupIds.Contains(g.DepartmentGroupId)
				&& GeoMath.IsPointInPolygon(point.Value.Latitude, point.Value.Longitude, GeoMath.ParseGeofence(g.Geofence))))
				return true;

			await _callsService.PopulateCallData(call, true, false, false, true, true, false, false, false, false);

			if (call.Dispatches != null && call.Dispatches.Any(d => string.Equals(d.UserId, scope.UserId, StringComparison.OrdinalIgnoreCase)))
				return true;

			if (scope.GroupIds.Count == 0)
				return false;

			if (call.GroupDispatches != null && call.GroupDispatches.Any(d => scope.GroupIds.Contains(d.DepartmentGroupId)))
				return true;

			if (call.UnitDispatches != null && call.UnitDispatches.Any())
			{
				var units = await GetUnitsAsync(scope.DepartmentId, data);
				var scopedUnitIds = new HashSet<int>(units.Where(u => scope.IncludesGroup(u.StationGroupId)).Select(u => u.UnitId));

				if (call.UnitDispatches.Any(d => scopedUnitIds.Contains(d.UnitId)))
					return true;
			}

			if (call.Dispatches != null && call.Dispatches.Any())
			{
				var scopedMemberIds = new HashSet<string>(groups
					.Where(g => scope.GroupIds.Contains(g.DepartmentGroupId) && g.Members != null)
					.SelectMany(g => g.Members)
					.Where(m => !string.IsNullOrWhiteSpace(m.UserId))
					.Select(m => m.UserId), StringComparer.OrdinalIgnoreCase);

				if (call.Dispatches.Any(d => d.UserId != null && scopedMemberIds.Contains(d.UserId)))
					return true;
			}

			return false;
		}

		public async Task<bool> CanUserAccessCallAsync(int departmentId, string userId, Call call)
		{
			return await IsCallInScopeAsync(await GetScopeForUserAsync(departmentId, userId), call);
		}

		public async Task<List<Call>> FilterCallsAsync(DispatchScope scope, List<Call> calls)
		{
			if (calls == null)
				return new List<Call>();

			if (scope != null && scope.IsDepartmentWide)
				return calls;

			var inScope = new List<Call>();
			var data = new ScopeData();

			foreach (var call in calls)
			{
				if (await IsCallInScopeAsync(scope, call, data))
					inScope.Add(call);
			}

			return inScope;
		}

		public async Task<List<Call>> FilterCallsForUserAsync(int departmentId, string userId, List<Call> calls)
		{
			return await FilterCallsAsync(await GetScopeForUserAsync(departmentId, userId), calls);
		}

		private async Task<List<DepartmentGroup>> GetGroupsAsync(int departmentId, ScopeData data)
		{
			if (data.Groups == null)
				data.Groups = (await _departmentGroupsService.GetAllGroupsForDepartmentUnlimitedAsync(departmentId) ?? new List<DepartmentGroup>())
					.Where(g => g != null).ToList();

			return data.Groups;
		}

		private async Task<List<Unit>> GetUnitsAsync(int departmentId, ScopeData data)
		{
			if (data.Units == null)
				data.Units = (await _unitsService.GetUnitsForDepartmentUnlimitedAsync(departmentId) ?? new List<Unit>())
					.Where(u => u != null).ToList();

			return data.Units;
		}
	}
}
