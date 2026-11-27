using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Workforce Pro seats (shifts-scheduling-modernization-plan.md §7.2 seat rules, the LMS seat model of §17.4): purchased seats are the
	/// running interval's packs × the pack size (WorkforceProAddonConfig.PackSize). Only active members (inactive-member policy: not deleted,
	/// disabled or hidden) hold a counted seat; switching one on beyond the purchased seats is refused with the packs to add; seats are per
	/// department. Switching a seat off is always allowed while Workforce Pro is offered (even lapsed) and never deletes a record: the
	/// member's wellness records stay readable, only no longer editable. Only department administrators switch seats (enforced here, not only
	/// in the controller). Two admins enabling the last seat at the same moment can both succeed; the licensing order (enabled first) then
	/// decides who is licensed until a pack is added.
	/// </summary>
	public class WorkforceProSeatService : IWorkforceProSeatService
	{
		private readonly IWorkforceProSeatsRepository _seats;
		private readonly IWorkforceProAccessService _access;
		private readonly IDepartmentsService _departments;
		private readonly IEventAggregator _events;

		public WorkforceProSeatService(IWorkforceProSeatsRepository seats, IWorkforceProAccessService access, IDepartmentsService departments, IEventAggregator events)
		{
			_seats = seats;
			_access = access;
			_departments = departments;
			_events = events;
		}

		private static int PackSize => Math.Max(1, WorkforceProAddonConfig.PackSize);

		private async Task<(List<WorkforceProSeat> Rows, HashSet<string> Active)> ReadAsync(int departmentId)
		{
			var rows = await _seats.GetForDepartmentAsync(departmentId) ?? new List<WorkforceProSeat>();
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			return (rows.Where(r => r != null && r.DepartmentId == departmentId).ToList(), new HashSet<string>(active, StringComparer.OrdinalIgnoreCase));
		}

		private static WorkforceProSeatSummary Summarize(WorkforceProAccess access, List<WorkforceProSeat> rows, HashSet<string> active)
		{
			var counted = WorkforceProSeatRules.CountedSeats(rows, active);
			var holders = rows.Select(r => r.UserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
			// A failed renewal inside its dunning grace keeps the packs last paid for (seats can still be switched on).
			var running = WorkforceProSubscription.IsEntitled(access.State);
			var packs = running ? access.Packs : 0;
			return new WorkforceProSeatSummary
			{
				Offered = access.Offered,
				State = access.State,
				PaidThroughUtc = access.PaidThroughUtc,
				GraceEndsUtc = access.GraceEndsUtc,
				PurchasedPacks = packs,
				PackSize = PackSize,
				EnabledSeats = counted.Count,
				DormantSeats = Math.Max(0, holders - counted.Count),
				EnabledUserIds = new HashSet<string>(counted.Select(s => s.UserId), StringComparer.OrdinalIgnoreCase),
				LicensedUserIds = WorkforceProSeatRules.Licensed(rows, active, packs * PackSize, access.Licensed),
				MinimumPacks = WorkforceProSeatRules.MinimumPacks(counted.Count, PackSize, WorkforceProAddonConfig.MinimumPacks)
			};
		}

		public async Task<WorkforceProSeatSummary> GetSummaryAsync(int departmentId)
		{
			var access = await _access.GetAccessAsync(departmentId);
			if (!access.Offered)
				return new WorkforceProSeatSummary { Offered = false, State = access.State, PackSize = PackSize, MinimumPacks = Math.Max(1, WorkforceProAddonConfig.MinimumPacks) };

			var (rows, active) = await ReadAsync(departmentId);
			return Summarize(access, rows, active);
		}

		public async Task<int> GetMinimumPacksAsync(int departmentId)
		{
			var (rows, active) = await ReadAsync(departmentId);
			return WorkforceProSeatRules.MinimumPacks(WorkforceProSeatRules.CountedSeats(rows, active).Count, PackSize, WorkforceProAddonConfig.MinimumPacks);
		}

		private async Task<bool> IsDepartmentAdminAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;
			var member = await _departments.GetDepartmentMemberAsync(userId, departmentId, true);
			if (!DepartmentMemberStateHelper.IsCurrentMember(member, departmentId))
				return false;
			if (member.IsAdmin.GetValueOrDefault())
				return true;
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			return department != null && string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase);
		}

		public async Task<WorkforceProSeatChangeResult> SetSeatAsync(int departmentId, string userId, bool enabled, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new WorkforceProSeatChangeResult { Outcome = WorkforceProSeatOutcomes.NotOffered };

			var access = await _access.GetAccessAsync(departmentId);
			if (!access.Offered)
				return new WorkforceProSeatChangeResult { Outcome = WorkforceProSeatOutcomes.NotOffered };
			if (!await IsDepartmentAdminAsync(departmentId, actingUserId))
				return new WorkforceProSeatChangeResult { Outcome = WorkforceProSeatOutcomes.NotAllowed };

			var (rows, active) = await ReadAsync(departmentId);
			var held = rows.Where(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();

			if (!enabled)
			{
				if (held.Count == 0)
					return new WorkforceProSeatChangeResult { Outcome = WorkforceProSeatOutcomes.Unchanged, Summary = Summarize(access, rows, active) };

				// Only the seat row goes: the member's wellness records are untouched (read-only from now on).
				await _seats.RemoveAsync(departmentId, new[] { userId }, cancellationToken);
				_access.Invalidate(departmentId);
				Audit(departmentId, actingUserId, new { enabled = true, userId }, new { enabled = false, userId });
				var remaining = rows.Where(r => !string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
				return new WorkforceProSeatChangeResult { Outcome = WorkforceProSeatOutcomes.Disabled, Summary = Summarize(access, remaining, active) };
			}

			// Only an active member can hold a seat; the membership is read fresh, not from the cached roster.
			var member = await _departments.GetDepartmentMemberAsync(userId, departmentId, true);
			if (!DepartmentMemberStateHelper.IsActiveMember(member, departmentId))
				return new WorkforceProSeatChangeResult { Outcome = WorkforceProSeatOutcomes.NotActiveMember, Summary = Summarize(access, rows, active) };
			active.Add(member.UserId);

			var summary = Summarize(access, rows, active);
			if (held.Count > 0)
				return new WorkforceProSeatChangeResult { Outcome = WorkforceProSeatOutcomes.Unchanged, Summary = summary };

			// Enabling needs a running add-on: nothing is seated free, and a lapsed department buys again first.
			if (!access.Licensed)
				return new WorkforceProSeatChangeResult { Outcome = WorkforceProSeatOutcomes.AddonInactive, Summary = summary };

			if (summary.EnabledSeats + 1 > summary.PurchasedSeats)
				return new WorkforceProSeatChangeResult
				{
					Outcome = WorkforceProSeatOutcomes.SeatsFull,
					Summary = summary,
					PacksNeeded = WorkforceProSeatRules.PacksNeeded(summary.EnabledSeats + 1, summary.PurchasedSeats, PackSize)
				};

			var seat = new WorkforceProSeat { DepartmentId = departmentId, UserId = member.UserId, EnabledOn = DateTime.UtcNow, EnabledByUserId = actingUserId };
			await _seats.AddAsync(seat, cancellationToken);
			_access.Invalidate(departmentId);
			Audit(departmentId, actingUserId, new { enabled = false, userId = member.UserId }, new { enabled = true, userId = member.UserId });
			rows.Add(seat);
			return new WorkforceProSeatChangeResult { Outcome = WorkforceProSeatOutcomes.Enabled, Summary = Summarize(access, rows, active) };
		}

		private void Audit(int departmentId, string userId, object before, object after)
		{
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.WorkforceProSeatsChanged,
				Before = JsonConvert.SerializeObject(before),
				After = JsonConvert.SerializeObject(after),
				Successful = true
			});
		}
	}
}
