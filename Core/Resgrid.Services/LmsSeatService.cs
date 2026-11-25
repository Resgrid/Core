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
	/// LMS seats (§17.4 seat rules; decision 7 revised by §25.4a item 5): purchased seats are the running interval's packs × the pack
	/// size (LmsAddonConfig.PackSize). Only active members (inactive-member policy: not deleted, disabled or hidden) hold a counted
	/// seat; enabling beyond the purchased seats is refused with the packs to add; seats are per department; nobody needs a seat to
	/// author, teach, evaluate or report. Each call asks the gate (ILmsAccessService) at most once; nothing here runs from a sweep.
	/// Two admins enabling the last seat at the same moment can both succeed; the licensing order (enabled first) then decides who is
	/// licensed until a pack is added.
	/// </summary>
	public class LmsSeatService : ILmsSeatService
	{
		private readonly ILmsMemberSeatsRepository _seats;
		private readonly ILmsAccessService _access;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IEventAggregator _events;

		public LmsSeatService(ILmsMemberSeatsRepository seats, ILmsAccessService access, IDepartmentsService departments, IDepartmentGroupsService groups,
			IEventAggregator events)
		{
			_seats = seats;
			_access = access;
			_departments = departments;
			_groups = groups;
			_events = events;
		}

		private static int PackSize => Math.Max(1, LmsAddonConfig.PackSize);

		private async Task<(List<LmsMemberSeat> Rows, HashSet<string> Active)> ReadAsync(int departmentId)
		{
			var rows = await _seats.GetForDepartmentAsync(departmentId) ?? new List<LmsMemberSeat>();
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			return (rows.Where(r => r != null && r.DepartmentId == departmentId).ToList(), new HashSet<string>(active, StringComparer.OrdinalIgnoreCase));
		}

		private static LmsSeatSummary Summarize(LmsSubscriptionSnapshot subscription, List<LmsMemberSeat> rows, HashSet<string> active)
		{
			var counted = LmsSeatRules.CountedSeats(rows, active);
			var holders = rows.Select(r => r.UserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
			var packs = subscription?.Packs ?? 0;
			return new LmsSeatSummary
			{
				State = subscription?.State ?? LmsSubscriptionStates.Unknown,
				GraceEndsUtc = subscription?.GraceEndsUtc,
				PaidThroughUtc = subscription?.PaidThroughUtc,
				PurchasedPacks = packs,
				PackSize = PackSize,
				EnabledSeats = counted.Count,
				DormantSeats = Math.Max(0, holders - counted.Count),
				EnabledUserIds = new HashSet<string>(counted.Select(s => s.UserId), StringComparer.OrdinalIgnoreCase),
				LicensedUserIds = LmsSeatRules.Licensed(rows, active, packs * PackSize, subscription?.IsEntitled == true),
				MinimumPacks = Math.Max(LmsAddonConfig.MinimumPacks, (int)Math.Ceiling(counted.Count / (double)PackSize))
			};
		}

		public async Task<LmsSeatSummary> GetSummaryAsync(int departmentId)
		{
			var subscription = await _access.GetSubscriptionAsync(departmentId);
			var (rows, active) = await ReadAsync(departmentId);
			return Summarize(subscription, rows, active);
		}

		public async Task<HashSet<string>> GetLicensedUserIdsAsync(int departmentId) => (await GetSummaryAsync(departmentId)).LicensedUserIds;

		public async Task<int> GetMinimumPacksAsync(int departmentId)
		{
			var (rows, active) = await ReadAsync(departmentId);
			return Math.Max(LmsAddonConfig.MinimumPacks, (int)Math.Ceiling(LmsSeatRules.CountedSeats(rows, active).Count / (double)PackSize));
		}

		public async Task<LmsSeatChangeResult> CanEnableAsync(int departmentId, int additional = 1)
		{
			if (!await _access.IsFlagEnabledAsync(departmentId))
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.NotOffered };

			var summary = await GetSummaryAsync(departmentId);
			if (summary.State != LmsSubscriptionStates.Active && summary.State != LmsSubscriptionStates.DunningGrace)
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.AddonInactive, Summary = summary };

			var wanted = summary.EnabledSeats + Math.Max(1, additional);
			return wanted > summary.PurchasedSeats
				? new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.SeatsFull, Summary = summary, PacksNeeded = LmsSeatRules.PacksNeeded(wanted, summary.PurchasedSeats, PackSize) }
				: new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.Enabled, Summary = summary };
		}

		public async Task<LmsSeatChangeResult> SetSeatAsync(int departmentId, string userId, bool enabled, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || !await _access.IsFlagEnabledAsync(departmentId))
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.NotOffered };

			var (rows, active) = await ReadAsync(departmentId);
			var held = rows.Where(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();

			if (!enabled)
			{
				if (held.Count == 0)
					return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.Unchanged };

				await _seats.RemoveAsync(departmentId, new[] { userId }, cancellationToken);
				Audit(departmentId, actingUserId, new { enabled = true, userIds = new[] { userId } }, new { enabled = false, userIds = new[] { userId } });
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.Disabled };
			}

			// Only an active member can hold a seat; the membership is read fresh, not from the cached roster.
			var member = await _departments.GetDepartmentMemberAsync(userId, departmentId, true);
			if (!DepartmentMemberStateHelper.IsActiveMember(member, departmentId))
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.NotActiveMember };
			active.Add(member.UserId);

			if (held.Count > 0)
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.Unchanged };

			var subscription = await _access.GetSubscriptionAsync(departmentId);
			var summary = Summarize(subscription, rows, active);
			if (!subscription.IsEntitled)
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.AddonInactive, Summary = summary };

			if (summary.EnabledSeats + 1 > summary.PurchasedSeats)
				return new LmsSeatChangeResult
				{
					Outcome = LmsSeatOutcomes.SeatsFull,
					Summary = summary,
					PacksNeeded = LmsSeatRules.PacksNeeded(summary.EnabledSeats + 1, summary.PurchasedSeats, PackSize)
				};

			await _seats.AddAsync(new LmsMemberSeat { DepartmentId = departmentId, UserId = member.UserId, EnabledOn = DateTime.UtcNow, EnabledByUserId = actingUserId }, cancellationToken);
			Audit(departmentId, actingUserId, new { enabled = false, userIds = new[] { member.UserId } }, new { enabled = true, userIds = new[] { member.UserId } });
			return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.Enabled };
		}

		public async Task<LmsSeatBulkResult> SetSeatsForGroupAsync(int departmentId, int departmentGroupId, bool enabled, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || !await _access.IsFlagEnabledAsync(departmentId))
				return new LmsSeatBulkResult { Outcome = LmsSeatOutcomes.NotOffered };

			var group = await _groups.GetGroupByIdAsync(departmentGroupId);
			if (group == null || group.DepartmentId != departmentId)
				return new LmsSeatBulkResult { Outcome = LmsSeatOutcomes.Unchanged };

			var memberIds = (await _groups.GetAllMembersForGroupAsync(departmentGroupId) ?? new List<DepartmentGroupMember>())
				.Where(m => m != null && !string.IsNullOrWhiteSpace(m.UserId))
				.Select(m => m.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			var (rows, active) = await ReadAsync(departmentId);
			var holders = new HashSet<string>(rows.Select(r => r.UserId).Where(id => id != null), StringComparer.OrdinalIgnoreCase);

			if (!enabled)
			{
				var remove = memberIds.Where(holders.Contains).ToList();
				if (remove.Count > 0)
				{
					await _seats.RemoveAsync(departmentId, remove, cancellationToken);
					Audit(departmentId, actingUserId, new { enabled = true, groupId = departmentGroupId, userIds = remove }, new { enabled = false, groupId = departmentGroupId, userIds = remove });
				}

				return new LmsSeatBulkResult
				{
					Outcome = remove.Count > 0 ? LmsSeatOutcomes.Disabled : LmsSeatOutcomes.Unchanged,
					Changed = remove.Count,
					AlreadySet = memberIds.Count - remove.Count
				};
			}

			var subscription = await _access.GetSubscriptionAsync(departmentId);
			var summary = Summarize(subscription, rows, active);
			if (!subscription.IsEntitled)
				return new LmsSeatBulkResult { Outcome = LmsSeatOutcomes.AddonInactive, Summary = summary };

			var inactive = memberIds.Count(id => !active.Contains(id));
			var already = memberIds.Count(id => active.Contains(id) && holders.Contains(id));
			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);
			var candidates = memberIds.Where(id => active.Contains(id) && !holders.Contains(id))
				.OrderBy(id => names.TryGetValue(id, out var name) ? name : id, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
				.ToList();

			var capacity = Math.Max(0, summary.PurchasedSeats - summary.EnabledSeats);
			var enable = candidates.Take(capacity).ToList();
			var refused = candidates.Count - enable.Count;
			var now = DateTime.UtcNow;
			foreach (var userId in enable)
				await _seats.AddAsync(new LmsMemberSeat { DepartmentId = departmentId, UserId = userId, EnabledOn = now, EnabledByUserId = actingUserId }, cancellationToken);

			if (enable.Count > 0)
				Audit(departmentId, actingUserId, new { enabled = false, groupId = departmentGroupId, userIds = enable }, new { enabled = true, groupId = departmentGroupId, userIds = enable });

			return new LmsSeatBulkResult
			{
				Outcome = refused > 0 ? LmsSeatOutcomes.SeatsFull : enable.Count > 0 ? LmsSeatOutcomes.Enabled : LmsSeatOutcomes.Unchanged,
				Changed = enable.Count,
				AlreadySet = already,
				SkippedInactive = inactive,
				RefusedForSeats = refused,
				PacksNeeded = LmsSeatRules.PacksNeeded(summary.EnabledSeats + candidates.Count, summary.PurchasedSeats, PackSize),
				Summary = summary
			};
		}

		private void Audit(int departmentId, string userId, object before, object after)
		{
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.LmsSeatsChanged,
				Before = JsonConvert.SerializeObject(before),
				After = JsonConvert.SerializeObject(after),
				Successful = true
			});
		}
	}
}
