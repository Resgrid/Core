using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services.AdminAssist
{
	public sealed partial class AdminAssistDiagnosticSource
	{
		private async Task MapAsync(AdminAssistActor actor, DiagnosticRequest r, DateTime now, List<DiagnosticCheck> checks, CancellationToken ct)
		{
			// RequireScopeAsync checks the fresh owning permission policy before and after this read.
			var data = (await sources.Single(s => s.SourceId == "DepartmentSettings").ReadAsync(actor, now, ct)).ToDictionary(e => e.Id);
			var prefix = r.UnitId.HasValue ? "MappingUnit" : "MappingPersonnel";
			if (!data.TryGetValue(prefix + "LocationTTL", out var ttl) || !ttl.IsFresh(now, TimeSpan.FromMinutes(1)) || ttl.Number is null or < 0 or > 525600 ||
				!data.TryGetValue(prefix + "AllowStatusWithNoLocationToOverwrite", out var overwrite) || !overwrite.IsFresh(now, TimeSpan.FromMinutes(1)) || !overwrite.Boolean.HasValue) throw new InvalidOperationException();
			DateTime? pingOn, statusOn; bool hasLocation;
			if (r.UnitId.HasValue)
			{
				var pings = await units.ReadLatestLocationsForAdministrationAsync(actor.DepartmentId).WaitAsync(ct);
				if (pings == null || pings.Count > 2000 || pings.Any(p => p.DepartmentId != actor.DepartmentId)) throw new InvalidOperationException();
				pingOn = pings.SingleOrDefault(p => p.UnitId == r.UnitId)?.Timestamp;
				var status = await units.GetLastUnitStateByUnitIdAsync(r.UnitId.Value).WaitAsync(ct);
				if (status != null && status.UnitId != r.UnitId) throw new UnauthorizedAccessException();
				statusOn = status?.Timestamp; hasLocation = status?.HasLocation() == true;
			}
			else
			{
				var pings = await users.ReadLatestLocationsForAdministrationAsync(actor.DepartmentId).WaitAsync(ct);
				if (pings == null || pings.Count > 2000 || pings.Any(p => p.DepartmentId != actor.DepartmentId)) throw new InvalidOperationException();
				pingOn = pings.SingleOrDefault(p => p.UserId == r.MemberId)?.Timestamp;
				if (!data.TryGetValue("DisabledAutoAvailable", out var auto) || !auto.IsFresh(now, TimeSpan.FromMinutes(1)) || !auto.Boolean.HasValue) throw new InvalidOperationException();
				var states = (await actions.ReadLatestForAdministrationAsync(actor.DepartmentId, auto.Boolean.Value, now, 2000, ct)).ToArray();
				if (states.Length > 2000 || states.Any(s => s.DepartmentId != actor.DepartmentId)) throw new InvalidOperationException();
				var status = states.Where(s => s.UserId == r.MemberId).OrderByDescending(s => s.ActionLogId).FirstOrDefault();
				statusOn = status?.Timestamp; hasLocation = status?.HasLocation() == true;
			}
			if (pingOn > now || statusOn > now) throw new InvalidOperationException();
			var marker = MappingMarkerSelection.Select(pingOn, statusOn, hasLocation, (int)ttl.Number.Value, overwrite.Boolean.Value, now);
			checks.Add(Check("MapMarker", marker == MappingMarkerSource.None, now, source: "V4MarkerSelection", destination: "/User/Department/MappingSettings"));
			checks.Add(Check("PingAge", !pingOn.HasValue ? null : ttl.Number > 0 && now - pingOn.Value > TimeSpan.FromMinutes((double)ttl.Number.Value), now,
				value: pingOn.HasValue ? (decimal)Math.Floor((now - pingOn.Value).TotalMinutes) : null, source: "LocationMetadata"));
			checks.Add(Check("MapClientScope", null, now));
		}
		private async Task PermissionAsync(AdminAssistActor actor, DiagnosticRequest r, DateTime now, List<DiagnosticCheck> checks, CancellationToken ct)
		{
			var allowed = await permissionEvaluator.EvaluateCurrentAsync(actor, r.MemberId, r.Permission,
				r.UnitId?.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
			checks.Add(Check("EffectivePermission", allowed.HasValue ? !allowed.Value : null, now, source: "OwningPermissionPolicy", destination: "/User/Security/Permissions"));
			checks.Add(Check("MemberActive", !await membership.IsActiveMemberAsync(r.MemberId, actor.DepartmentId).WaitAsync(ct), now, source: "Membership"));
			checks.Add(Check("SessionFreshness", null, now));
		}
		private async Task CoverageAsync(AdminAssistActor actor, DiagnosticRequest r, DateTime now, List<DiagnosticCheck> checks, CancellationToken ct)
		{
			var ownedRoles = await roles.GetRolesForDepartmentUnlimitedAsync(actor.DepartmentId).WaitAsync(ct);
			if (ownedRoles == null || ownedRoles.Count > 1000 || !ownedRoles.Any(role => role.PersonnelRoleId == r.RoleId && role.DepartmentId == actor.DepartmentId)) throw new UnauthorizedAccessException();
			var department = await departments.GetDepartmentByIdAsync(actor.DepartmentId, true).WaitAsync(ct);
			var local = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById(department.TimeZone));
			var qualified = await qualifications.EvaluateRoleRequirementsAsync(actor.DepartmentId, r.RoleId.Value, local.Date).WaitAsync(ct);
			if (qualified == null || qualified.Count > 2000) throw new InvalidOperationException();
			foreach (var person in qualified)
				if (!await visibility.CanUserViewPersonAsync(actor.UserId, person.UserId, actor.DepartmentId).WaitAsync(ct)) throw new UnauthorizedAccessException();
			var schedule = await shifts.ReadSchedulesForAdministrationAsync(actor.DepartmentId, local.Date.AddDays(-3), local.Date.AddDays(1), now, 2000, ct);
			if (schedule == null) throw new InvalidOperationException();
			var active = schedule.Where(s => s.Day.Start <= local && s.Day.End > local).ToArray();
			var roster = active.SelectMany(s => s.Roster).Where(p => p.IsOnDuty() && (!r.GroupId.HasValue || p.DepartmentGroupId == r.GroupId)).ToArray();
			if (roster.Length > 2000) throw new InvalidOperationException();
			foreach (var person in roster)
				if (!await visibility.CanUserViewPersonAsync(actor.UserId, person.UserId, actor.DepartmentId).WaitAsync(ct) || !await membership.IsAssignableMemberAsync(person.UserId, actor.DepartmentId).WaitAsync(ct)) throw new UnauthorizedAccessException();
			var eligible = qualified.Where(p => p.Qualified).Select(p => p.UserId).Intersect(roster.Select(p => p.UserId), StringComparer.OrdinalIgnoreCase).Count();
			checks.Add(Check("QualifiedRoster", eligible == 0, now, value: eligible, source: "RosterAndQualifications"));
			var gaps = qualified.Count(p => !p.Qualified);
			checks.Add(Check("QualificationGaps", gaps > 0, now, value: gaps, source: "RoleQualifications"));
			var needs = active.SelectMany(s => s.Needs).Where(g => !r.GroupId.HasValue || g.Key == r.GroupId).Where(g => g.Value.ContainsKey(r.RoleId.Value)).Select(g => Math.Max(0, g.Value[r.RoleId.Value])).ToArray();
			checks.Add(Check("ScheduledRoleVacancies", needs.Length == 0 ? null : needs.Sum() > 0, now, value: needs.Length == 0 ? null : needs.Sum(), source: "ResolvedRosterNeeds"));
			var overlap = roster.GroupBy(p => p.UserId).Count(g => g.Count() > 1);
			checks.Add(Check("RosterOverlap", overlap > 0, now, value: overlap, source: "ResolvedRoster"));
			checks.Add(Check("LocalMinimum", null, now)); checks.Add(Check("ResponseAvailability", null, now));
		}
		private async Task EquipmentChecksAsync(AdminAssistActor actor, DiagnosticRequest r, DateTime now, List<DiagnosticCheck> checks, CancellationToken ct)
		{
			var subject = new ChecklistActor { DepartmentId = actor.DepartmentId, UserId = actor.UserId };
			var summary = await checklists.GetComplianceSummaryAsync(subject, new ChecklistReportQuery { FromUtc = r.FromUtc, UntilUtc = r.UntilUtc, TargetType = ChecklistTargetType.Unit, TargetId = r.UnitId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) }).WaitAsync(ct);
			if (summary == null || summary.IsRedacted || summary.UnavailableSources.Count > 0 || summary.Entries.Count > 2000) throw new UnauthorizedAccessException();
			var overdue = summary.Entries.Count(e => e.Expected && !e.Completed && !e.Skipped && e.DueUtc < now);
			var failed = summary.Entries.Count(e => e.Passed == false);
			checks.Add(Check("OverdueChecks", overdue > 0, now, value: overdue, source: "ChecklistSummary"));
			checks.Add(Check("FailedChecks", failed > 0, now, value: failed, source: "ChecklistSummary"));
		}
		private async Task MaintenanceAsync(AdminAssistActor actor, DiagnosticRequest r, DateTime now, List<DiagnosticCheck> checks, CancellationToken ct)
		{
			var subject = new ChecklistActor { DepartmentId = actor.DepartmentId, UserId = actor.UserId };
			var orderCount = 0; var overdueOrders = 0; var activeHolds = new HashSet<string>();
			for (var page = 0; page < 20; page++)
			{
				var list = await orders.ListAsync(subject, new WorkOrderFilter { UnitId = r.UnitId, Page = page }).WaitAsync(ct);
				if (list == null || (orderCount += list.Items.Count) > 500) throw new InvalidOperationException();
				foreach (var order in list.Items)
				{
					if (order.UnitId != r.UnitId) throw new UnauthorizedAccessException();
					if (order.DueOn < now && order.Status is WorkOrderStatus.Requested or WorkOrderStatus.Accepted or WorkOrderStatus.Assigned or WorkOrderStatus.InProgress or WorkOrderStatus.OnHold) overdueOrders++;
					var holds = await orderReports.GetWorkOrderHoldsAsync(subject, order.Id).WaitAsync(ct);
					if (holds == null || holds.NextAfterId != null) throw new InvalidOperationException();
					foreach (var hold in holds.Items.Where(h => !h.Hold.ReleasedOn.HasValue)) activeHolds.Add(hold.Hold.Id);
				}
				if (!list.HasMore) { checks.Add(Check("ActiveSafetyHolds", activeHolds.Count > 0, now, value: activeHolds.Count, source: "WorkOrderHolds")); break; }
				if (page == 19 || list.Items.Count == 0) throw new InvalidOperationException();
			}
			checks.Add(Check("OverdueMaintenance", overdueOrders > 0, now, value: overdueOrders, source: "VisibleWorkOrders"));
			checks.Add(Check("EquipmentRelease", null, now));
		}
	}
}
