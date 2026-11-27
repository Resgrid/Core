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
using Resgrid.Model.Lms;
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
		private readonly ILmsExternalLearnersRepository _externalLearners;
		private readonly ILmsSponsorshipService _sponsorships;

		public LmsSeatService(ILmsMemberSeatsRepository seats, ILmsAccessService access, IDepartmentsService departments, IDepartmentGroupsService groups,
			IEventAggregator events, ILmsExternalLearnersRepository externalLearners, ILmsSponsorshipService sponsorships)
		{
			_externalLearners = externalLearners;
			_sponsorships = sponsorships;
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

		/// <summary>External learners counted for fair use (T5): invited or active.</summary>
		private async Task<int> CountLearnersAsync(int departmentId) => Math.Max(0, await _externalLearners.CountCountedAsync(departmentId));

		private static LmsSeatSummary Summarize(LmsSubscriptionSnapshot subscription, List<LmsMemberSeat> rows, HashSet<string> active, int learners,
			LmsSponsorshipGrant grant = null, int sponsoredSeats = 0)
		{
			var counted = LmsSeatRules.CountedSeats(rows, active);
			var holders = rows.Select(r => r.UserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
			var packs = subscription?.Packs ?? 0;
			if (grant?.Sponsorship != null)
			{
				// T5 item 2a: no add-on of its own; the sponsor's packs seat the department's members up to what the sponsorship leaves it.
				var capacity = Math.Max(0, grant.Capacity);
				return new LmsSeatSummary
				{
					State = LmsSubscriptionStates.Active,
					PurchasedPacks = 0,
					PackSize = PackSize,
					SponsoredCapacity = capacity,
					SponsorDepartmentId = grant.Sponsorship.SponsorDepartmentId,
					SponsorName = grant.SponsorName,
					EnabledSeats = counted.Count,
					DormantSeats = Math.Max(0, holders - counted.Count),
					EnabledUserIds = new HashSet<string>(counted.Select(s => s.UserId), StringComparer.OrdinalIgnoreCase),
					LicensedUserIds = LmsSponsorshipRules.Licensed(rows, active, capacity),
					ExternalLearnerSeats = 0,
					MinimumPacks = 0
				};
			}

			// The sponsor's own members come first; its sponsored departments' seats take what is left of its packs.
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
				ExternalLearnerSeats = LmsExternalLearnerRules.SeatsTaken(learners, packs * PackSize),
				SponsoredSeats = Math.Max(0, sponsoredSeats),
				MinimumPacks = LmsExternalLearnerRules.MinimumPacks(counted.Count + Math.Max(0, sponsoredSeats), learners, PackSize, LmsAddonConfig.MinimumPacks)
			};
		}

		/// <summary>
		/// The department's add-on state for seating: its own subscription; or, without an entitled add-on of its own, an active sponsorship whose
		/// sponsor's add-on is entitled (T5 item 2a); and, for a department on its own add-on, the seats its sponsored departments hold in it.
		/// </summary>
		private async Task<(LmsSubscriptionSnapshot Subscription, LmsSponsorshipGrant Grant, int SponsoredSeats)> ResolveAsync(int departmentId)
		{
			var subscription = await _access.GetSubscriptionAsync(departmentId);
			if (subscription.IsEntitled)
			{
				var sponsored = 0;
				try
				{
					sponsored = _sponsorships == null ? 0 : await _sponsorships.GetSponsoredSeatsAsync(departmentId);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, "Sponsored seats could not be counted; the sponsor's own seats stand.");
				}

				return (subscription, null, sponsored);
			}

			LmsSponsorshipGrant grant = null;
			try
			{
				grant = _sponsorships == null ? null : await _sponsorships.GetGrantAsync(departmentId);
			}
			catch (Exception ex)
			{
				// Fail closed: without the sponsorship read the department has no seats it does not pay for.
				Framework.Logging.LogException(ex, "The department's LMS sponsorship could not be read.");
			}

			return (grant?.Sponsorship != null ? new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.Active } : subscription, grant, 0);
		}

		public async Task<LmsSeatSummary> GetSummaryAsync(int departmentId)
		{
			var (subscription, grant, sponsored) = await ResolveAsync(departmentId);
			var (rows, active) = await ReadAsync(departmentId);
			return Summarize(subscription, rows, active, await CountLearnersAsync(departmentId), grant, sponsored);
		}

		public async Task<HashSet<string>> GetLicensedUserIdsAsync(int departmentId) => (await GetSummaryAsync(departmentId)).LicensedUserIds;

		public async Task<int> GetMinimumPacksAsync(int departmentId)
		{
			var (rows, active) = await ReadAsync(departmentId);
			var sponsored = 0;
			try
			{
				sponsored = _sponsorships == null ? 0 : await _sponsorships.GetSponsoredSeatsAsync(departmentId);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			// The seats the department sponsors are its own packs' too: a pack decrease never strands them.
			return LmsExternalLearnerRules.MinimumPacks(LmsSeatRules.CountedSeats(rows, active).Count + sponsored, await CountLearnersAsync(departmentId), PackSize, LmsAddonConfig.MinimumPacks);
		}

		public async Task<LmsSeatChangeResult> CanEnableAsync(int departmentId, int additional = 1)
		{
			if (!await _access.IsFlagEnabledAsync(departmentId))
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.NotOffered };

			var summary = await GetSummaryAsync(departmentId);
			if (summary.State != LmsSubscriptionStates.Active && summary.State != LmsSubscriptionStates.DunningGrace)
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.AddonInactive, Summary = summary };

			var wanted = summary.EnabledSeats + summary.ExternalLearnerSeats + summary.SponsoredSeats + Math.Max(1, additional);
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

				var removed = await _seats.RemoveAsync(departmentId, new[] { userId }, cancellationToken);
				Audit(departmentId, actingUserId, new { enabled = true, userIds = new[] { userId } }, new { enabled = false, userIds = new[] { userId } });
				RaiseSeatsChanged(departmentId, 0, removed, Remaining(rows, active, new[] { userId }), 0, actingUserId);
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.Disabled };
			}

			// Only an active member can hold a seat; the membership is read fresh, not from the cached roster.
			var member = await _departments.GetDepartmentMemberAsync(userId, departmentId, true);
			if (!DepartmentMemberStateHelper.IsActiveMember(member, departmentId))
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.NotActiveMember };
			active.Add(member.UserId);

			if (held.Count > 0)
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.Unchanged };

			var (subscription, grant, sponsoredSeats) = await ResolveAsync(departmentId);
			var summary = Summarize(subscription, rows, active, await CountLearnersAsync(departmentId), grant, sponsoredSeats);
			if (!subscription.IsEntitled)
				return new LmsSeatChangeResult { Outcome = LmsSeatOutcomes.AddonInactive, Summary = summary };

			if (summary.EnabledSeats + summary.ExternalLearnerSeats + summary.SponsoredSeats + 1 > summary.PurchasedSeats)
				return new LmsSeatChangeResult
				{
					Outcome = LmsSeatOutcomes.SeatsFull,
					Summary = summary,
					// A sponsored department cannot buy packs for its sponsor: it is told the sponsor's seats are full instead.
					PacksNeeded = grant != null ? 0 : LmsSeatRules.PacksNeeded(summary.EnabledSeats + summary.ExternalLearnerSeats + summary.SponsoredSeats + 1, summary.PurchasedSeats, PackSize)
				};

			var added = await _seats.AddAsync(new LmsMemberSeat { DepartmentId = departmentId, UserId = member.UserId, EnabledOn = DateTime.UtcNow, EnabledByUserId = actingUserId }, cancellationToken);
			Audit(departmentId, actingUserId, new { enabled = false, userIds = new[] { member.UserId } }, new { enabled = true, userIds = new[] { member.UserId } });
			if (added)
				RaiseSeatsChanged(departmentId, 1, 0, summary.EnabledSeats + 1, summary.PurchasedPacks, actingUserId);
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
					var removed = await _seats.RemoveAsync(departmentId, remove, cancellationToken);
					Audit(departmentId, actingUserId, new { enabled = true, groupId = departmentGroupId, userIds = remove }, new { enabled = false, groupId = departmentGroupId, userIds = remove });
					RaiseSeatsChanged(departmentId, 0, removed, Remaining(rows, active, remove), 0, actingUserId);
				}

				return new LmsSeatBulkResult
				{
					Outcome = remove.Count > 0 ? LmsSeatOutcomes.Disabled : LmsSeatOutcomes.Unchanged,
					Changed = remove.Count,
					AlreadySet = memberIds.Count - remove.Count
				};
			}

			var (subscription, grant, sponsoredSeats) = await ResolveAsync(departmentId);
			var summary = Summarize(subscription, rows, active, await CountLearnersAsync(departmentId), grant, sponsoredSeats);
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

			var capacity = Math.Max(0, summary.PurchasedSeats - summary.EnabledSeats - summary.ExternalLearnerSeats - summary.SponsoredSeats);
			var enable = candidates.Take(capacity).ToList();
			var refused = candidates.Count - enable.Count;
			var now = DateTime.UtcNow;
			var addedCount = 0;
			foreach (var userId in enable)
				if (await _seats.AddAsync(new LmsMemberSeat { DepartmentId = departmentId, UserId = userId, EnabledOn = now, EnabledByUserId = actingUserId }, cancellationToken))
					addedCount++;

			if (enable.Count > 0)
				Audit(departmentId, actingUserId, new { enabled = false, groupId = departmentGroupId, userIds = enable }, new { enabled = true, groupId = departmentGroupId, userIds = enable });
			if (addedCount > 0)
				RaiseSeatsChanged(departmentId, addedCount, 0, summary.EnabledSeats + addedCount, summary.PurchasedPacks, actingUserId);

			return new LmsSeatBulkResult
			{
				Outcome = refused > 0 ? LmsSeatOutcomes.SeatsFull : enable.Count > 0 ? LmsSeatOutcomes.Enabled : LmsSeatOutcomes.Unchanged,
				Changed = enable.Count,
				AlreadySet = already,
				SkippedInactive = inactive,
				RefusedForSeats = refused,
				PacksNeeded = grant != null ? 0 : LmsSeatRules.PacksNeeded(summary.EnabledSeats + summary.ExternalLearnerSeats + summary.SponsoredSeats + candidates.Count, summary.PurchasedSeats, PackSize),
				Summary = summary
			};
		}

		/// <summary>Member seats in use once the given members' seats are gone (read rows, no second query).</summary>
		private static int Remaining(List<LmsMemberSeat> rows, HashSet<string> active, IEnumerable<string> removedUserIds)
		{
			var removed = new HashSet<string>(removedUserIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			return LmsSeatRules.CountedSeats(rows.Where(r => !removed.Contains(r.UserId ?? string.Empty)).ToList(), active).Count;
		}

		/// <summary>Workflow trigger 274 after the seat rows were written: counts only, never which members.</summary>
		private void RaiseSeatsChanged(int departmentId, int added, int removed, int enabledSeats, int packs, string actingUserId)
		{
			if (added <= 0 && removed <= 0)
				return;
			ContentWorkflowContext.Publish(_events, LmsWorkflowEvents.SeatsChanged(departmentId, added, removed, enabledSeats, packs, null, actingUserId, DateTime.UtcNow));
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
