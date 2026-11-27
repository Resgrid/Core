using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Helpers
{
	/// <summary>
	/// The seat rules (§17.4) as pure functions, shared by the gate and the seat service. Only active members hold a counted seat;
	/// when a department holds more enabled active members than purchased seats (a member came back from being disabled, or packs
	/// were bought back lower after a lapse), seats are licensed in the order they were enabled and the rest show as not licensed.
	/// User ids compare case-insensitively.
	/// </summary>
	public static class LmsSeatRules
	{
		/// <summary>The active members' seats, in licensing order (enabled first, then row id).</summary>
		public static List<LmsMemberSeat> CountedSeats(IEnumerable<LmsMemberSeat> seats, ISet<string> activeUserIds)
		{
			var active = new HashSet<string>(activeUserIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			return (seats ?? Enumerable.Empty<LmsMemberSeat>())
				.Where(s => s != null && !string.IsNullOrWhiteSpace(s.UserId) && active.Contains(s.UserId))
				.GroupBy(s => s.UserId, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.OrderBy(s => s.EnabledOn).ThenBy(s => s.LmsMemberSeatId).First())
				.OrderBy(s => s.EnabledOn).ThenBy(s => s.LmsMemberSeatId)
				.ToList();
		}

		/// <summary>The members licensed now: counted seats within <paramref name="purchasedSeats"/>, and none without a working add-on.</summary>
		public static HashSet<string> Licensed(IEnumerable<LmsMemberSeat> seats, ISet<string> activeUserIds, int purchasedSeats, bool addonEntitled)
		{
			var licensed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (!addonEntitled || purchasedSeats <= 0)
				return licensed;

			foreach (var seat in CountedSeats(seats, activeUserIds).Take(purchasedSeats))
				licensed.Add(seat.UserId);
			return licensed;
		}

		/// <summary>
		/// Round 10 A7 (owner decision 2026-11-26): the members who may act as learners on department LMS content now, before the lapse rules
		/// decide what still runs. <paramref name="capacity"/> is the seats in force: the purchased seats while the add-on works, the seats a paying
		/// sponsorship leaves the department, or the packs last paid for while the add-on is lapsed (lapse rule 1: seats stay enabled, so assigned
		/// work stays takeable for the members who held one, rule 4). Null when no figure is known (billing cannot say, or a sponsorship stopped
		/// paying): every counted seat, since the lapse windows already limit what runs. Licensing order as <see cref="Licensed"/>.
		/// </summary>
		public static HashSet<string> LearnerSeatHolders(IEnumerable<LmsMemberSeat> seats, ISet<string> activeUserIds, int? capacity)
		{
			var holders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var counted = CountedSeats(seats, activeUserIds);
			foreach (var seat in capacity.HasValue ? counted.Take(Math.Max(0, capacity.Value)) : counted)
				holders.Add(seat.UserId);
			return holders;
		}

		/// <summary>The packs needed to seat <paramref name="members"/> people beyond <paramref name="purchasedSeats"/> (0 when they fit).</summary>
		public static int PacksNeeded(int members, int purchasedSeats, int packSize)
		{
			var missing = members - Math.Max(0, purchasedSeats);
			return missing <= 0 || packSize <= 0 ? 0 : (int)Math.Ceiling(missing / (double)packSize);
		}
	}

	/// <summary>
	/// Lapse rule 12 (§17.3, R-L7) as pure functions: the reactivation re-base of one paused due date. Every lapse that began before
	/// the (already shifted) due moment and ended after the item was assigned shifts it by the part of the lapse after the assignment,
	/// the way <see cref="TrainingDueDates.ApplyLeave"/> shifts a member's due date by their leave. After the department's last
	/// reactivation nothing that was running across a lapse becomes overdue or enforceable until the reactivation grace has passed:
	/// a shifted due date that would still fall inside the grace moves to its end, and every item assigned before the reactivation
	/// carries the hold. A due date already overdue before the lapse began is not moved (time lapsed does not count toward lateness;
	/// it does not forgive lateness earned before), but it is still held from enforcement until the grace ends. An item assigned
	/// after the last reactivation was never paused and is returned unchanged.
	/// </summary>
	public static class LmsLapseRebase
	{
		public static LmsRebasedDue Rebase(LmsDueItem item, IEnumerable<LmsAddonLapse> lapses, TimeSpan reactivationGrace)
		{
			if (item == null)
				throw new ArgumentNullException(nameof(item));

			var closed = (lapses ?? Enumerable.Empty<LmsAddonLapse>())
				.Where(l => l != null && l.EndedOn.HasValue && l.EndedOn.Value > l.StartedOn)
				.OrderBy(l => l.StartedOn)
				.ToList();

			var due = item.DueUtc;
			var from = item.AssignedOnUtc ?? DateTime.MinValue;
			var shifted = false;

			foreach (var lapse in closed)
			{
				// A lapse after the due moment, or over before the item was assigned, never touched it.
				if (lapse.StartedOn >= due || lapse.EndedOn.Value <= from)
					continue;

				var start = lapse.StartedOn > from ? lapse.StartedOn : from;
				due = due.Add(lapse.EndedOn.Value - start);
				shifted = true;
			}

			DateTime? hold = null;
			var lastReactivation = closed.Count == 0 ? (DateTime?)null : closed.Max(l => l.EndedOn.Value);
			if (lastReactivation.HasValue && from < lastReactivation.Value)
			{
				hold = lastReactivation.Value.Add(reactivationGrace);
				if (shifted && due < hold.Value)
					due = hold.Value;
			}

			return new LmsRebasedDue
			{
				Key = item.Key,
				OriginalDueUtc = item.DueUtc,
				DueUtc = due,
				HoldUntilUtc = hold
			};
		}

		/// <summary>The lapse time between <paramref name="fromUtc"/> and <paramref name="toUtc"/> (time that does not count toward lateness).</summary>
		public static TimeSpan LapsedBetween(IEnumerable<LmsAddonLapse> lapses, DateTime fromUtc, DateTime toUtc)
		{
			var total = TimeSpan.Zero;
			foreach (var lapse in lapses ?? Enumerable.Empty<LmsAddonLapse>())
			{
				if (lapse == null)
					continue;
				var start = lapse.StartedOn > fromUtc ? lapse.StartedOn : fromUtc;
				var end = (lapse.EndedOn ?? toUtc) < toUtc ? (lapse.EndedOn ?? toUtc) : toUtc;
				if (end > start)
					total += end - start;
			}

			return total;
		}
	}
}
