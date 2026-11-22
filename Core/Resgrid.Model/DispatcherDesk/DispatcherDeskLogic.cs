using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;

namespace Resgrid.Model
{
	/// <summary>
	/// Who is on a shift day's dispatch roster, and when (pure; DispatcherDesk services feed it). A day row decides for its day
	/// (its latest row wins: Active on, PendingApproval listed, Denied off); without one, the shift's standing (no-day) Active rows
	/// put the dispatcher on every day.
	/// </summary>
	public static class DispatcherDutyCalculator
	{
		public static List<DispatcherDutyWindow> Build(IEnumerable<ShiftDayTimesRow> days, IEnumerable<ShiftDispatcher> roster, Department department)
		{
			var result = new List<DispatcherDutyWindow>();
			var rows = (roster ?? Enumerable.Empty<ShiftDispatcher>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.UserId)).ToList();

			foreach (var day in (days ?? Enumerable.Empty<ShiftDayTimesRow>()).Where(x => x != null))
			{
				var (localStart, localEnd) = ShiftTimeWindow.GetWindow(day.Day, day.StartTime, day.EndTime, day.Hours);
				var startUtc = ToUtc(localStart, department);
				var endUtc = ToUtc(localEnd, department);

				var dayRows = rows.Where(x => x.ShiftId == day.ShiftId && x.ShiftDayId == day.ShiftDayId)
					.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
					.Select(g => g.OrderByDescending(x => x.ReviewedOn ?? x.AddedOn).ThenByDescending(x => x.AddedOn).First())
					.ToDictionary(x => x.UserId, StringComparer.OrdinalIgnoreCase);

				var standing = rows.Where(x => x.ShiftId == day.ShiftId && !x.ShiftDayId.HasValue && x.IsActive())
					.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
					.Select(g => g.OrderByDescending(x => x.AddedOn).First());

				foreach (var row in standing)
				{
					if (dayRows.ContainsKey(row.UserId))
						continue;

					result.Add(Window(day, row, startUtc, endUtc, pending: false));
				}

				foreach (var row in dayRows.Values)
				{
					if (row.Status == (int)ShiftDispatcherStatuses.Denied)
						continue;

					result.Add(Window(day, row, startUtc, endUtc, pending: row.Status == (int)ShiftDispatcherStatuses.PendingApproval));
				}
			}

			return result.OrderBy(x => x.StartUtc).ThenBy(x => x.UserId, StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>Approved windows covering the instant, one per dispatcher (the one ending last).</summary>
		public static List<DispatcherDutyWindow> OnDuty(IEnumerable<DispatcherDutyWindow> windows, DateTime utcNow)
		{
			return (windows ?? Enumerable.Empty<DispatcherDutyWindow>()).Where(x => x.Covers(utcNow))
				.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.OrderByDescending(x => x.EndUtc).ThenByDescending(x => x.IsSupervisor).First())
				.ToList();
		}

		private static DispatcherDutyWindow Window(ShiftDayTimesRow day, ShiftDispatcher row, DateTime startUtc, DateTime endUtc, bool pending)
		{
			return new DispatcherDutyWindow
			{
				UserId = row.UserId,
				ShiftId = day.ShiftId,
				ShiftDayId = day.ShiftDayId,
				ShiftName = day.ShiftName,
				ShiftColor = day.Color,
				Day = day.Day.Date,
				StartUtc = startUtc,
				EndUtc = endUtc,
				IsSupervisor = row.IsSupervisor,
				Source = row.Source,
				IsPending = pending,
				ShiftDispatcherId = row.ShiftDispatcherId
			};
		}

		public static DateTime ToUtc(DateTime local, Department department)
		{
			return DateTime.SpecifyKind(DateTime.SpecifyKind(local, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc);
		}
	}

	/// <summary>Coverage slots and warnings from duty windows and breaks (pure).</summary>
	public static class DispatcherCoverageCalculator
	{
		/// <param name="minimumFor">The minimum available for a slot, given the duty windows covering it (shift override or department).</param>
		public static DispatcherCoverage Build(DateTime fromUtc, DateTime toUtc, int slotMinutes, IEnumerable<DispatcherDutyWindow> duty,
			IEnumerable<DispatcherBreak> breaks, Func<IReadOnlyCollection<DispatcherDutyWindow>, int> minimumFor, DateTime utcNow)
		{
			slotMinutes = Math.Max(1, Math.Min(slotMinutes <= 0 ? 5 : slotMinutes, 240));
			if (toUtc <= fromUtc)
				toUtc = fromUtc.AddMinutes(slotMinutes);

			var windows = (duty ?? Enumerable.Empty<DispatcherDutyWindow>()).Where(x => x != null && x.EndUtc > fromUtc && x.StartUtc < toUtc).ToList();
			var breakList = (breaks ?? Enumerable.Empty<DispatcherBreak>()).Where(x => x != null).ToList();
			var approved = windows.Where(x => !x.IsPending).ToList();

			var breakWindows = breakList.Select(x => (Break: x, Window: x.CoverageWindow(utcNow)))
				.Where(x => x.Window.HasValue).Select(x => (x.Break, Start: x.Window.Value.Start, End: x.Window.Value.End)).ToList();

			var coverage = new DispatcherCoverage { FromUtc = fromUtc, ToUtc = toUtc, SlotMinutes = slotMinutes, Duty = windows, Breaks = breakList };

			for (var start = fromUtc; start < toUtc; start = start.AddMinutes(slotMinutes))
			{
				var end = start.AddMinutes(slotMinutes);
				if (end > toUtc)
					end = toUtc;

				var mid = start + TimeSpan.FromTicks((end - start).Ticks / 2);
				var covering = approved.Where(x => x.StartUtc <= mid && x.EndUtc > mid).ToList();
				var onDuty = covering.Select(x => x.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				var onBreak = breakWindows.Where(x => x.Start <= mid && x.End > mid && onDuty.Contains(x.Break.UserId, StringComparer.OrdinalIgnoreCase))
					.Select(x => x.Break.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				coverage.Slots.Add(new DispatcherCoverageSlot
				{
					StartUtc = start,
					EndUtc = end,
					OnDuty = onDuty.Count,
					OnBreak = onBreak.Count,
					Minimum = covering.Count == 0 ? 0 : Math.Max(0, minimumFor?.Invoke(covering) ?? 0)
				});
			}

			coverage.Warnings.AddRange(MergeWarnings(coverage.Slots, breakWindows.Select(x => (x.Break.UserId, x.Start, x.End)).ToList()));
			return coverage;
		}

		private static IEnumerable<DispatcherDeskWarning> MergeWarnings(List<DispatcherCoverageSlot> slots, List<(string UserId, DateTime Start, DateTime End)> breaks)
		{
			DispatcherDeskWarning open = null;
			DispatcherDeskWarning overlap = null;

			foreach (var slot in slots)
			{
				var kind = slot.OnDuty > 0 && slot.Available == 0 && slot.Minimum > 0 ? DispatcherDeskWarningKinds.NoCoverage
					: slot.BelowMinimum ? DispatcherDeskWarningKinds.CoverageBelowMinimum : (DispatcherDeskWarningKinds?)null;

				if (kind.HasValue && open != null && open.Kind == kind.Value && open.EndUtc == slot.StartUtc)
				{
					open.EndUtc = slot.EndUtc;
					open.Available = Math.Min(open.Available ?? int.MaxValue, slot.Available);
				}
				else
				{
					if (open != null)
						yield return open;

					open = kind.HasValue
						? new DispatcherDeskWarning { Kind = kind.Value, StartUtc = slot.StartUtc, EndUtc = slot.EndUtc, Available = slot.Available, Minimum = slot.Minimum }
						: null;
				}

				if (slot.BreaksOverlap)
				{
					var mid = slot.StartUtc + TimeSpan.FromTicks((slot.EndUtc - slot.StartUtc).Ticks / 2);
					var users = breaks.Where(x => x.Start <= mid && x.End > mid).Select(x => x.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

					if (overlap != null && overlap.EndUtc == slot.StartUtc)
					{
						overlap.EndUtc = slot.EndUtc;
						foreach (var user in users.Where(u => !overlap.UserIds.Contains(u, StringComparer.OrdinalIgnoreCase)))
							overlap.UserIds.Add(user);
					}
					else
					{
						if (overlap != null)
							yield return overlap;

						overlap = new DispatcherDeskWarning { Kind = DispatcherDeskWarningKinds.BreaksOverlap, StartUtc = slot.StartUtc, EndUtc = slot.EndUtc, UserIds = users };
					}
				}
				else if (overlap != null)
				{
					yield return overlap;
					overlap = null;
				}
			}

			if (open != null)
				yield return open;

			if (overlap != null)
				yield return overlap;
		}
	}

	/// <summary>
	/// Places a break plan for a day's dispatchers (pure): each dispatcher's breaks near their targets, staggered so that breaks
	/// do not overlap where the day allows (within <see cref="NoOverlapDriftMinutes"/> of the target), and never so that more
	/// dispatchers are on break at once than the minimum allows; never overlapping their own other breaks.
	/// </summary>
	public static class DispatcherBreakPlanner
	{
		public const int StepMinutes = 5;

		/// <summary>
		/// How far a break may move from its target to find a time when nobody else is on break. Past that, a time that only keeps
		/// the minimum on the desk (breaks overlapping) is taken rather than moving a meal hours away from when it was planned.
		/// </summary>
		public const int NoOverlapDriftMinutes = 120;

		/// <summary>No break in the first or last part of a duty window.</summary>
		public const int EdgeMarginMinutes = 30;

		/// <summary>Gap kept between one dispatcher's own breaks.</summary>
		public const int OwnGapMinutes = 30;

		public class PlannedBreak
		{
			public string UserId { get; set; }
			public int BreakType { get; set; }
			public int DurationMinutes { get; set; }
			public DateTime StartUtc { get; set; }
			public int ShiftId { get; set; }
			public int ShiftDayId { get; set; }

			/// <summary>No placement kept the minimum; this one overlaps more breaks than allowed.</summary>
			public bool BreaksMinimum { get; set; }
		}

		/// <param name="dispatchers">The day's approved duty windows (one per dispatcher).</param>
		/// <param name="existing">Breaks already on the day (Scheduled / Active / Completed) that the plan must work around.</param>
		/// <param name="minimumAvailable">Dispatchers that must stay available; the number who may be on break at once is on duty − minimum (at least 1).</param>
		/// <param name="notBeforeUtc">
		/// No break before this instant (planning part-way through a shift): a target already past is placed from here on,
		/// staggered like the rest, instead of being dropped.
		/// </param>
		public static List<PlannedBreak> Plan(IReadOnlyList<DispatcherDutyWindow> dispatchers, IReadOnlyList<DispatcherBreakPlanEntry> plan,
			IEnumerable<(string UserId, DateTime Start, DateTime End)> existing, int minimumAvailable, DateTime? notBeforeUtc = null)
		{
			var result = new List<PlannedBreak>();
			if (dispatchers == null || dispatchers.Count == 0 || plan == null || plan.Count == 0)
				return result;

			var occupied = (existing ?? Enumerable.Empty<(string, DateTime, DateTime)>()).ToList();
			var allowedAtOnce = Math.Max(1, dispatchers.Count - Math.Max(0, minimumAvailable));
			var ordered = dispatchers.OrderBy(x => x.StartUtc).ThenBy(x => x.UserId, StringComparer.OrdinalIgnoreCase).ToList();

			for (var index = 0; index < plan.Count; index++)
			{
				var entry = plan[index];
				if (entry == null || entry.DurationMinutes <= 0)
					continue;

				foreach (var dispatcher in ordered)
				{
					var duration = TimeSpan.FromMinutes(entry.DurationMinutes);
					var earliest = dispatcher.StartUtc.AddMinutes(EdgeMarginMinutes);
					var latest = dispatcher.EndUtc.AddMinutes(-EdgeMarginMinutes) - duration;
					if (latest < earliest)
					{
						earliest = dispatcher.StartUtc;
						latest = dispatcher.EndUtc - duration;
						if (latest < earliest)
							continue;
					}

					if (notBeforeUtc.HasValue && earliest < notBeforeUtc.Value)
					{
						earliest = Round(notBeforeUtc.Value.AddMinutes(StepMinutes / 2.0));
						if (latest < earliest)
							continue;
					}

					var target = entry.TargetOffsetMinutes.HasValue
						? dispatcher.StartUtc.AddMinutes(entry.TargetOffsetMinutes.Value)
						: dispatcher.StartUtc + TimeSpan.FromTicks((dispatcher.EndUtc - dispatcher.StartUtc).Ticks * (index + 1) / (plan.Count + 1)) - TimeSpan.FromTicks(duration.Ticks / 2);
					target = Round(Clamp(target, earliest, latest));

					var placed = Find(dispatcher.UserId, target, duration, earliest, latest, occupied, allowedAtOnce, out var breaksMinimum);
					if (!placed.HasValue)
						continue;

					occupied.Add((dispatcher.UserId, placed.Value, placed.Value + duration));
					result.Add(new PlannedBreak
					{
						UserId = dispatcher.UserId,
						BreakType = entry.BreakType <= 0 ? (int)DispatcherBreakTypes.Break : entry.BreakType,
						DurationMinutes = entry.DurationMinutes,
						StartUtc = placed.Value,
						ShiftId = dispatcher.ShiftId,
						ShiftDayId = dispatcher.ShiftDayId,
						BreaksMinimum = breaksMinimum
					});
				}
			}

			return result;
		}

		private static DateTime? Find(string userId, DateTime target, TimeSpan duration, DateTime earliest, DateTime latest,
			List<(string UserId, DateTime Start, DateTime End)> occupied, int allowedAtOnce, out bool breaksMinimum)
		{
			breaksMinimum = false;

			// First choice: the nearest time near the target when nobody else is on break.
			var drift = TimeSpan.FromMinutes(NoOverlapDriftMinutes);
			var clear = Nearest(userId, target, duration, Max(earliest, target - drift), Min(latest, target + drift), occupied,
				(start, end) => MaxConcurrent(occupied, start, end) == 0);
			if (clear.HasValue)
				return clear;

			// Then the nearest time that keeps the minimum on the desk (breaks may overlap).
			var withinMinimum = Nearest(userId, target, duration, earliest, latest, occupied, (start, end) => MaxConcurrent(occupied, start, end) < allowedAtOnce);
			if (withinMinimum.HasValue)
				return withinMinimum;

			// No time keeps the minimum: the nearest that fits the dispatcher at all, flagged.
			var any = Nearest(userId, target, duration, earliest, latest, occupied, (_, _) => true);
			breaksMinimum = any.HasValue;
			return any;
		}

		/// <summary>The time nearest the target (later first, keeping breaks in plan order) that clears the dispatcher's own breaks and passes the test.</summary>
		private static DateTime? Nearest(string userId, DateTime target, TimeSpan duration, DateTime earliest, DateTime latest,
			List<(string UserId, DateTime Start, DateTime End)> occupied, Func<DateTime, DateTime, bool> accept)
		{
			if (latest < earliest)
				return null;

			var maxSteps = (int)Math.Ceiling(Math.Max((latest - target).TotalMinutes, (target - earliest).TotalMinutes) / StepMinutes) + 1;

			for (var step = 0; step <= maxSteps; step++)
			{
				foreach (var candidate in step == 0 ? new[] { target } : new[] { target.AddMinutes(step * StepMinutes), target.AddMinutes(-step * StepMinutes) })
				{
					if (candidate < earliest || candidate > latest)
						continue;

					var end = candidate + duration;
					if (occupied.Any(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
										  x.Start < end.AddMinutes(OwnGapMinutes) && x.End.AddMinutes(OwnGapMinutes) > candidate))
						continue;

					if (accept(candidate, end))
						return candidate;
				}
			}

			return null;
		}

		private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

		private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

		/// <summary>The most breaks overlapping any instant of [start, end).</summary>
		private static int MaxConcurrent(List<(string UserId, DateTime Start, DateTime End)> occupied, DateTime start, DateTime end)
		{
			var overlapping = occupied.Where(x => x.Start < end && x.End > start).ToList();
			if (overlapping.Count == 0)
				return 0;

			var points = overlapping.Select(x => x.Start < start ? start : x.Start).Distinct();
			return points.Max(p => overlapping.Count(x => x.Start <= p && x.End > p));
		}

		private static DateTime Clamp(DateTime value, DateTime min, DateTime max) => value < min ? min : value > max ? max : value;

		private static DateTime Round(DateTime value)
		{
			var ticks = TimeSpan.FromMinutes(StepMinutes).Ticks;
			return new DateTime((value.Ticks + ticks / 2) / ticks * ticks, DateTimeKind.Utc);
		}
	}

	/// <summary>Display state and routing order of dispatchers (pure).</summary>
	public static class DispatcherRoutingPolicy
	{
		public static DispatcherDisplayStates DisplayState(DispatcherPresence presence, int activeItems, int capacity)
		{
			if (presence == null || presence.State == (int)DispatcherPresenceStates.Offline)
				return DispatcherDisplayStates.Offline;

			switch ((DispatcherPresenceStates)presence.State)
			{
				case DispatcherPresenceStates.OnBreak:
					return DispatcherDisplayStates.OnBreak;
				case DispatcherPresenceStates.Away:
					return DispatcherDisplayStates.Away;
				default:
					return activeItems >= Math.Max(1, capacity) ? DispatcherDisplayStates.Busy : DispatcherDisplayStates.Available;
			}
		}

		/// <summary>
		/// Whether the member can be offered work now: on duty, signed in Available (not on break, not away), below capacity, no
		/// offer outstanding, and (when the department times out heartbeats) seen recently.
		/// </summary>
		public static bool IsRoutable(DispatcherDeskMember member, DateTime utcNow, int heartbeatTimeoutMinutes)
		{
			if (member?.Presence == null || !member.IsOnDuty)
				return false;

			if (member.Presence.State != (int)DispatcherPresenceStates.Available || member.ActiveBreak != null)
				return false;

			if (member.OfferedItemCount > 0 || member.ActiveItemCount >= Math.Max(1, member.Capacity))
				return false;

			if (heartbeatTimeoutMinutes > 0)
			{
				var seen = member.Presence.LastSeenOn ?? member.Presence.SignedInOn;
				if (!seen.HasValue || seen.Value < utcNow.AddMinutes(-heartbeatTimeoutMinutes))
					return false;
			}

			return true;
		}

		/// <summary>Least loaded first, then the longest since their last offer (never offered first), then name for stability.</summary>
		public static List<DispatcherDeskMember> Order(IEnumerable<DispatcherDeskMember> members)
		{
			return (members ?? Enumerable.Empty<DispatcherDeskMember>())
				.OrderBy(x => x.ActiveItemCount)
				.ThenBy(x => x.Presence?.LastOfferedOn ?? DateTime.MinValue)
				.ThenBy(x => x.Name ?? x.UserId, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		/// <summary>Queue order: higher priority first, then due time (scheduled calls), then age.</summary>
		public static List<DispatcherQueueItem> QueueOrder(IEnumerable<DispatcherQueueItem> items)
		{
			return (items ?? Enumerable.Empty<DispatcherQueueItem>())
				.OrderByDescending(x => x.Priority)
				.ThenBy(x => x.DueOn ?? x.CreatedOn)
				.ThenBy(x => x.CreatedOn)
				.ToList();
		}

		/// <summary>The base priority of a kind (live callers first); call priority adds on top.</summary>
		public static int KindPriority(DispatcherQueueItemKinds kind)
		{
			switch (kind)
			{
				case DispatcherQueueItemKinds.Voice: return 400;
				case DispatcherQueueItemKinds.Text: return 300;
				case DispatcherQueueItemKinds.Chat: return 300;
				case DispatcherQueueItemKinds.PendingCall: return 250;
				case DispatcherQueueItemKinds.Email: return 200;
				case DispatcherQueueItemKinds.ScheduledCall: return 100;
				default: return 150;
			}
		}

		/// <summary>Kind + normalized sender, for joining follow-up messages to the open conversation.</summary>
		/// <summary>The <see cref="DispatcherQueueItem.ExternalReference"/> that links an item to an intake text thread.</summary>
		public static string IntakeReference(string intakeConversationId) =>
			string.IsNullOrWhiteSpace(intakeConversationId) ? null : IntakeReferencePrefix + intakeConversationId.Trim();

		/// <summary>The intake text thread an item is linked to, or null.</summary>
		public static string IntakeConversationIdOf(DispatcherQueueItem item)
		{
			var reference = item?.ExternalReference;
			return reference != null && reference.StartsWith(IntakeReferencePrefix, StringComparison.Ordinal) && reference.Length > IntakeReferencePrefix.Length
				? reference.Substring(IntakeReferencePrefix.Length)
				: null;
		}

		public const string IntakeReferencePrefix = "IC:";

		public static string ConversationKey(DispatcherQueueItemKinds kind, string sourceAddress)
		{
			if (string.IsNullOrWhiteSpace(sourceAddress))
				return null;

			var address = sourceAddress.Trim().ToLowerInvariant();
			if (kind == DispatcherQueueItemKinds.Text || kind == DispatcherQueueItemKinds.Voice)
			{
				var digits = new string(address.Where(char.IsDigit).ToArray());
				if (digits.Length == 10)
					digits = "1" + digits;
				address = digits.Length == 0 ? address : "+" + digits;
			}

			// Voice and text from the same number are one conversation (a caller who hangs up and texts).
			var channel = kind == DispatcherQueueItemKinds.Voice ? DispatcherQueueItemKinds.Text : kind;
			var key = (int)channel + ":" + address;
			return key.Length > 300 ? key.Substring(0, 300) : key;
		}
	}
}
