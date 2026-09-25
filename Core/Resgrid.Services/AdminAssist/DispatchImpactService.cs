using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Request-local routing simulation. No communication, workflow or configuration write dependency.</summary>
	public sealed class DispatchImpactService(IAdminAssistAccessService access, IAdminAssistRepository repository,
		IAdminAssistCatalog catalog, ICallsService calls, IDepartmentsService departments, IDepartmentGroupsRepository groups,
		IUnitsRepository units, IUnitStateRoleRepository crews, IPersonnelRolesRepository roles, IPersonnelRoleUsersRepository roleMembers,
		IDepartmentSettingsRepository settings, IShiftsService shifts, IAuthorizationService visibility,
		IRecordsAuthorizationService authorization, TimeProvider clock, ICallDispatchesRepository directRoutes,
		ICallDispatchGroupRepository groupRoutes, ICallDispatchUnitRepository unitRoutes, ICallDispatchRoleRepository roleRoutes) : IDispatchImpactService
	{
		private sealed record Inputs(IReadOnlyList<DispatchRoute> Routes, bool Shift, bool Crew, bool UnitGroup);
		private static readonly string[] Limits = { "Impact.NoMutation", "Impact.DispatchScope", "Impact.DispatchTime", "Impact.DispatchDelivery", "Impact.Window" };
		public async Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, DispatchImpactRequest request, CancellationToken ct = default)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			var now = clock.GetUtcNow().UtcDateTime;
			if (request == null || request.CallId <= 0 || request.SimulationTimeUtc.Kind != DateTimeKind.Utc ||
				(request.SimulationTimeUtc - now).Duration() > TimeSpan.FromDays(7)) throw new ArgumentException("Choose an explicit UTC time within seven days of now.");
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.SnapshotTimeoutSeconds, 1, 60)));
			ct = timeout.Token;
			var revision = (await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture);
			if (revision != request.ExpectedRevision) throw new AdminAssistConcurrencyException();
			var entry = catalog.Settings.Single(s => s.Id == "setting.DispatchShiftInsteadOfGroup");
			var metrics = new List<ConfigurationImpactMetric>();
			try
			{
				var input = await ReadAsync(actor, request, ct);
				var before = Resolve(input, request.SimulationTimeUtc, input.Shift, input.Crew, input.UnitGroup);
				var after = Resolve(input, request.SimulationTimeUtc, request.ShiftInsteadOfGroup, request.UnitCrew, request.UnitGroup);
				// Source routes, memberships, crews and schedules are not all in the configuration journal yet.
				// Compare a second fresh source vector, rather than claim revision-only consistency.
				if (Fingerprint(input) != Fingerprint(await ReadAsync(actor, request, ct))) throw new AdminAssistConcurrencyException();
				var oldPeople = before.SelectedUserIds.ToHashSet(StringComparer.Ordinal);
				var newPeople = after.SelectedUserIds.ToHashSet(StringComparer.Ordinal);
				void Count(string key, int oldCount, int newCount) => metrics.Add(new("Impact." + key, EvidenceState.Known, oldCount, newCount));
				Count("DispatchPeople", oldPeople.Count, newPeople.Count);
				Count("DispatchAttempts", before.SelectedUserIds.Count, after.SelectedUserIds.Count);
				Count("DispatchAdded", 0, newPeople.Except(oldPeople).Count());
				Count("DispatchRemoved", 0, oldPeople.Except(newPeople).Count());
				var emptyShiftGroups = input.Routes.Where(r => r.Kind == DispatchRouteKind.Group && r.OnDutyMembers.Count == 0).Select(r => r.SourceId).Distinct().Count();
				Count("DispatchFallback", input.Shift ? emptyShiftGroups : 0, request.ShiftInsteadOfGroup ? emptyShiftGroups : 0);
				Count("DispatchSample", 1, 1);
			}
			catch (AdminAssistConcurrencyException) { throw; }
			catch (UnauthorizedAccessException) { throw; }
			catch (OperationCanceledException) { throw; }
			catch (Exception)
			{
				metrics.Clear();
				metrics.Add(new("Impact.DispatchPeople", EvidenceState.Unknown, null, null, "SourceUnavailable"));
			}
			if ((await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture) != revision) throw new AdminAssistConcurrencyException();
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			return new(entry.Id, revision, request.SimulationTimeUtc, "dispatch-impact-v1/resolver-" + DispatchRecipientResolver.Version,
				entry.Impact, metrics, Array.Empty<ConfigurationImpactRule>(), Limits, entry.Location.Url);
		}

		private async Task<Inputs> ReadAsync(AdminAssistActor actor, DispatchImpactRequest request, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			var call = await calls.GetCallByIdAsync(request.CallId, true).WaitAsync(ct);
			if (call == null || call.DepartmentId != actor.DepartmentId || !await authorization.CanReadSourceCallAsync(actor.UserId, actor.DepartmentId, call)) throw new UnauthorizedAccessException();
			// Fresh route-only reads: the legacy population helper turns null sources into empty routes.
			// Never mutate its returned call or expose narrative/attachment/protected payloads in this report.
			call = new Call { CallId = call.CallId, DepartmentId = call.DepartmentId,
				Dispatches = (await directRoutes.GetCallDispatchesByCallIdAsync(call.CallId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException(),
				GroupDispatches = (await groupRoutes.GetAllCallDispatchGroupByCallIdAsync(call.CallId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException(),
				UnitDispatches = (await unitRoutes.GetCallUnitDispatchesByCallIdAsync(call.CallId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException(),
				RoleDispatches = (await roleRoutes.GetCallRoleDispatchesByCallIdAsync(call.CallId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException() };
			var budget = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
			void Consume(int count) { budget -= count; if (budget < 0) throw new InvalidOperationException("Routing evidence bound exceeded."); }
			Consume(call.Dispatches.Count + call.GroupDispatches.Count + call.UnitDispatches.Count + call.RoleDispatches.Count);
			var options = (await settings.GetAllByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
			bool Setting(DepartmentSettingTypes type)
			{
				var row = options.SingleOrDefault(s => s.DepartmentId == actor.DepartmentId && s.SettingType == (int)type);
				return row == null ? false : bool.Parse(row.Setting);
			}
			var currentShift = Setting(DepartmentSettingTypes.DispatchShiftInsteadOfGroup);
			var currentCrew = Setting(DepartmentSettingTypes.UnitDispatchAlsoDispatchToAssignedPersonnel);
			var currentGroup = Setting(DepartmentSettingTypes.UnitDispatchAlsoDispatchToGroup);
			var ownedGroups = (await groups.GetAllGroupsByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
			Consume(ownedGroups.Count + ownedGroups.Sum(g => g.Members?.Count ?? 0));
			var groupMap = ownedGroups.ToDictionary(g => g.DepartmentGroupId);
			string[] Members(int id)
			{
				if (!groupMap.TryGetValue(id, out var group) || group.DepartmentId != actor.DepartmentId) throw new UnauthorizedAccessException();
				if (group.Members == null) throw new InvalidOperationException();
				Consume(group.Members.Count);
				return group.Members.Select(m => m.UserId).ToArray();
			}
			var active = new List<ShiftDayRosterEntry>();
			if ((currentShift || request.ShiftInsteadOfGroup) && call.GroupDispatches.Count > 0)
			{
				var department = await departments.GetDepartmentByIdAsync(actor.DepartmentId, true).WaitAsync(ct) ?? throw new InvalidOperationException();
				var local = TimeZoneInfo.ConvertTimeFromUtc(request.SimulationTimeUtc, TimeZoneInfo.FindSystemTimeZoneById(department.TimeZone));
				var schedules = await shifts.ReadSchedulesForAdministrationAsync(actor.DepartmentId, local.Date.AddDays(-3), local.Date, request.SimulationTimeUtc, budget, ct) ?? throw new InvalidOperationException();
				active = schedules.Where(s => s.IsActive).SelectMany(s => s.Roster).ToList();
				Consume(active.Count);
			}
			var routes = new List<DispatchRoute>();
			foreach (var dispatch in call.Dispatches) routes.Add(new(DispatchRouteKind.Direct, "direct", new[] { dispatch.UserId }));
			foreach (var dispatch in call.GroupDispatches)
			{
				var members = Members(dispatch.DepartmentGroupId);
				routes.Add(new(DispatchRouteKind.Group, dispatch.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), members,
					OnDutyMembers: ShiftRosterGroups.Select(dispatch.DepartmentGroupId, active, members)));
			}
			foreach (var dispatch in call.UnitDispatches)
			{
				var unit = await units.GetByIdAsync(dispatch.UnitId).WaitAsync(ct);
				if (unit == null || unit.DepartmentId != actor.DepartmentId || !await visibility.CanUserViewUnitAsync(actor.UserId, unit.UnitId)) throw new UnauthorizedAccessException();
				if (currentCrew || request.UnitCrew)
				{
					var members = (await crews.GetCurrentRolesForUnitAsync(unit.UnitId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
					Consume(members.Count);
					routes.Add(new(DispatchRouteKind.UnitCrew, unit.UnitId.ToString(CultureInfo.InvariantCulture), members.Select(m => m.UserId).ToArray()));
				}
				if ((currentGroup || request.UnitGroup) && unit.StationGroupId.HasValue)
					routes.Add(new(DispatchRouteKind.UnitGroup, unit.StationGroupId.Value.ToString(CultureInfo.InvariantCulture), Members(unit.StationGroupId.Value)));
			}
			foreach (var dispatch in call.RoleDispatches)
			{
				var role = await roles.GetRoleByRoleIdAsync(dispatch.RoleId).WaitAsync(ct);
				if (role == null || role.DepartmentId != actor.DepartmentId) throw new UnauthorizedAccessException();
				var members = (await roleMembers.GetAllMembersOfRoleAsync(dispatch.RoleId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
				Consume(members.Count);
				routes.Add(new(DispatchRouteKind.Role, dispatch.RoleId.ToString(CultureInfo.InvariantCulture), members.Select(m => m.UserId).ToArray()));
			}
			foreach (var userId in routes.SelectMany(r => r.Members.Concat(r.OnDutyMembers ?? Array.Empty<string>())).Distinct(StringComparer.Ordinal))
			{
				ct.ThrowIfCancellationRequested();
				if (string.IsNullOrWhiteSpace(userId) || !await authorization.IsAssignableMemberAsync(userId, actor.DepartmentId) ||
					!await visibility.CanUserViewPersonAsync(actor.UserId, userId, actor.DepartmentId)) throw new UnauthorizedAccessException();
			}
			return new(routes, currentShift, currentCrew, currentGroup);
		}
		private static DispatchResolution Resolve(Inputs input, DateTime time, bool shift, bool crew, bool group) =>
			DispatchRecipientResolver.Resolve(time, input.Routes.Where(r => (r.Kind != DispatchRouteKind.UnitCrew || crew) && (r.Kind != DispatchRouteKind.UnitGroup || group))
				.Select(r => r.Kind == DispatchRouteKind.Group ? r with { UseResolvedShift = shift } : r));
		private static string Fingerprint(Inputs value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value))));
	}
}
