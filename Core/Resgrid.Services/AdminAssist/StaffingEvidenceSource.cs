using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Uses the owning roster builder for approved signups/trades. Counts are administrative evidence, not qualification clearance.</summary>
	public sealed class StaffingEvidenceSource(IShiftsService shifts, IDepartmentsService departments,
		IDepartmentGroupsRepository groups, IAuthorizationService authorization, IRecordsAuthorizationService membership) : IAdminAssistEvidenceSource
	{
		public string SourceId => "ResolvedShiftRoster";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "groupsWithoutShiftCoverage", "upcomingOpenShiftSlots", "upcomingShiftCount", "overlappingShiftPersonnel", "unfilledShiftTrades", "singlePersonShiftGroups" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var department = await departments.GetDepartmentByIdAsync(actor.DepartmentId, true) ?? throw new InvalidOperationException("Department unavailable.");
			var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById(department.TimeZone));
			var bound = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
			var schedules = await shifts.ReadSchedulesForAdministrationAsync(actor.DepartmentId, localNow.Date.AddDays(-3), localNow.Date.AddDays(7), now, bound, ct)
				?? throw new InvalidOperationException("Roster unavailable.");
			var groupRows = (await groups.GetAllGroupsByDepartmentIdAsync(actor.DepartmentId))?.ToList() ?? throw new InvalidOperationException("Groups unavailable.");
			if (groupRows.Count + groupRows.Sum(g => g.Members?.Count ?? 0) > bound || groupRows.Any(g => g.DepartmentId != actor.DepartmentId || g.Members == null))
				throw new InvalidOperationException("Incomplete group evidence.");
			var people = schedules.SelectMany(s => s.Roster).Select(r => r.UserId).Concat(groupRows.SelectMany(g => g.Members).Select(m => m.UserId)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			if (people.Length > bound) throw new InvalidOperationException("Roster bound exceeded.");
			foreach (var userId in people)
			{
				ct.ThrowIfCancellationRequested();
				if (!await authorization.CanUserViewPersonAsync(actor.UserId, userId, actor.DepartmentId)) throw new UnauthorizedAccessException();
				// Disabled/stale roster references require source cleanup; they cannot prove coverage.
				if (!await membership.IsAssignableMemberAsync(userId, actor.DepartmentId)) throw new InvalidOperationException("Roster membership requires review.");
			}
			var active = schedules.Where(s => s.Day.Start <= localNow && s.Day.End > localNow).SelectMany(s => s.Roster).Where(r => r.IsOnDuty()).ToArray();
			var counts = groupRows.Select(g => active.Where(r => r.DepartmentGroupId == g.DepartmentGroupId ||
				(!r.DepartmentGroupId.HasValue && g.Members.Any(m => string.Equals(m.UserId, r.UserId, StringComparison.OrdinalIgnoreCase)))).Select(r => r.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count()).ToArray();
			var upcoming = schedules.Where(s => s.Day.End > localNow && s.Day.Start < localNow.AddDays(7)).ToArray();
			var assignments = upcoming.SelectMany(s => s.Roster.Where(r => r.IsOnDuty()).Select(r => new { r.UserId, s.Day.ShiftDayId, s.Day.Start, s.Day.End }));
			var overlapping = assignments.GroupBy(a => a.UserId, StringComparer.OrdinalIgnoreCase).Count(g =>
				g.Any(a => g.Any(b => a.ShiftDayId != b.ShiftDayId && a.Start < b.End && b.Start < a.End)));
			ConfigurationEvidence Count(string id, int value) => new(id, EvidenceState.Known, SourceId, "roster-v1", now, Number: value);
			return new[] { Count("groupsWithoutShiftCoverage", counts.Count(c => c == 0)), Count("singlePersonShiftGroups", counts.Count(c => c == 1)),
				Count("upcomingShiftCount", upcoming.Length), Count("upcomingOpenShiftSlots", upcoming.Sum(s => s.OpenSlots())),
				Count("overlappingShiftPersonnel", overlapping), Count("unfilledShiftTrades", upcoming.SelectMany(s => s.Trades).Where(t => !t.Denied && !t.IsTradeComplete()).Select(t => t.ShiftSignupTradeId).Distinct().Count()) };
		}
	}
}
