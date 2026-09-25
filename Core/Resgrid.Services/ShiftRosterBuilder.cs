using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>
	/// Works out who is on a shift day from data that is already loaded, with no I/O, so every caller (day views,
	/// calendars, needs, on-duty dispatch) agrees on the same answer.
	///
	/// The rules, in order:
	/// 1. Signups for the day that are not denied put their person on the day. A supervisor-assigned signup is a
	///    single-day roster edit. A signup waiting for approval is listed but flagged pending.
	/// 2. A completed trade moves a signup's slot: the source slot goes to the accepted user (or the owner of the
	///    swap-back signup), and a swap-back slot goes to the requester. The original owner is off for that day.
	/// 3. The standing roster (ShiftPersons) is on every day, except a person who has a denied signup for the day
	///    (a supervisor took them off that day) or whose slot was traded away. Someone who also has an on-duty signup
	///    for the day in the same group is represented by that signup instead; a signup for another group, or one still
	///    waiting for approval, leaves their standing slot alone.
	/// </summary>
	public static class ShiftRosterBuilder
	{
		/// <param name="shift">The shift, with Personnel loaded.</param>
		/// <param name="day">The shift day's date (department local); only the date part is used.</param>
		/// <param name="signups">Signups for this shift; other days and shifts are ignored.</param>
		/// <param name="trades">Trades touching this shift's signups, with Source/TargetShiftSignup populated.</param>
		public static List<ShiftDayRosterEntry> Build(Shift shift, DateTime day, IEnumerable<ShiftSignup> signups, IEnumerable<ShiftSignupTrade> trades)
		{
			var entries = new List<ShiftDayRosterEntry>();

			if (shift == null)
				return entries;

			var date = day.Date;
			var daySignups = (signups ?? Enumerable.Empty<ShiftSignup>())
				.Where(x => x != null && x.ShiftId == shift.ShiftId && x.ShiftDay.Date == date)
				.ToList();

			var replacements = GetTradeReplacements(trades);

			// People on the standing roster who are off for this day.
			var standingExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			// Group slots already filled, on duty, by the person's own signup for the day.
			var onDutySignupSlots = new HashSet<(string UserId, int? GroupId)>();

			foreach (var signup in daySignups.Where(x => x.Denied))
				standingExclusions.Add(signup.UserId);

			var standingRoster = new HashSet<string>((shift.Personnel ?? Enumerable.Empty<ShiftPerson>())
				.Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId)).Select(x => x.UserId), StringComparer.OrdinalIgnoreCase);

			foreach (var signup in daySignups.Where(x => !x.Denied).OrderBy(x => x.ShiftSignupId))
			{
				if (!signup.ApprovalPending)
					onDutySignupSlots.Add(SlotKey(signup.UserId, signup.DepartmentGroupId));

				if (replacements.TryGetValue(signup.ShiftSignupId, out var replacement))
				{
					standingExclusions.Add(signup.UserId);

					AddEntry(entries, new ShiftDayRosterEntry
					{
						UserId = replacement.UserId,
						DepartmentGroupId = signup.DepartmentGroupId,
						Source = ShiftRosterSources.Trade,
						ShiftSignupId = signup.ShiftSignupId,
						ApprovalPending = signup.ApprovalPending,
						TradedFromUserId = signup.UserId,
						ShiftSignupTradeId = replacement.ShiftSignupTradeId
					});

					continue;
				}

				AddEntry(entries, new ShiftDayRosterEntry
				{
					UserId = signup.UserId,
					DepartmentGroupId = signup.DepartmentGroupId,
					Source = GetSignupSource(signup, standingRoster),
					ShiftSignupId = signup.ShiftSignupId,
					ApprovalPending = signup.ApprovalPending
				});
			}

			if (shift.Personnel != null)
			{
				foreach (var person in shift.Personnel.Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId)))
				{
					if (standingExclusions.Contains(person.UserId) || onDutySignupSlots.Contains(SlotKey(person.UserId, person.GroupId)))
						continue;

					AddEntry(entries, new ShiftDayRosterEntry
					{
						UserId = person.UserId,
						DepartmentGroupId = person.GroupId,
						Source = ShiftRosterSources.Assigned
					});
				}
			}

			return entries;
		}

		/// <summary>
		/// Remaining needs per department group and personnel role: group id to (role id to people still needed, never
		/// below zero). Every shift group gets an entry, including groups with no role requirements (an empty map).
		/// Only on-duty (not pending) roster entries for a group count, and each person fills at most one role
		/// requirement. People who qualify for fewer of the group's roles are placed first so a multi-role person is
		/// not spent on a role someone else could have filled.
		/// </summary>
		public static Dictionary<int, Dictionary<int, int>> CalculateNeeds(Shift shift, IEnumerable<ShiftDayRosterEntry> roster,
			IDictionary<string, List<PersonnelRole>> rolesByUser)
		{
			var needs = new Dictionary<int, Dictionary<int, int>>();

			if (shift?.Groups == null)
				return needs;

			var roles = new Dictionary<string, List<PersonnelRole>>(StringComparer.OrdinalIgnoreCase);
			if (rolesByUser != null)
			{
				foreach (var pair in rolesByUser)
					if (pair.Key != null && !roles.ContainsKey(pair.Key))
						roles.Add(pair.Key, pair.Value ?? new List<PersonnelRole>());
			}

			var onDuty = (roster ?? Enumerable.Empty<ShiftDayRosterEntry>()).Where(x => x != null && x.IsOnDuty()).ToList();

			foreach (var group in shift.Groups.Where(x => x != null))
			{
				if (!needs.TryGetValue(group.DepartmentGroupId, out var requirements))
				{
					requirements = new Dictionary<int, int>();
					needs.Add(group.DepartmentGroupId, requirements);
				}

				if (group.Roles != null)
				{
					foreach (var role in group.Roles.Where(x => x != null))
					{
						requirements.TryGetValue(role.PersonnelRoleId, out var existing);
						requirements[role.PersonnelRoleId] = existing + Math.Max(role.Required, 0);
					}
				}
			}

			foreach (var groupNeeds in needs)
			{
				var requirements = groupNeeds.Value;

				if (requirements.Count == 0)
					continue;

				var candidates = onDuty
					.Where(x => x.DepartmentGroupId == groupNeeds.Key)
					.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
					.Select(x => roles.TryGetValue(x.Key, out var userRoles)
						? userRoles.Select(r => r.PersonnelRoleId).Where(requirements.ContainsKey).Distinct().ToList()
						: new List<int>())
					.Where(x => x.Count > 0)
					.OrderBy(x => x.Count)
					.ToList();

				foreach (var qualifyingRoles in candidates)
				{
					var open = qualifyingRoles.Where(r => requirements[r] > 0).ToList();

					if (open.Count == 0)
						continue;

					var roleToFill = open.OrderByDescending(r => requirements[r]).First();
					requirements[roleToFill]--;
				}
			}

			return needs;
		}

		private static (string UserId, int? GroupId) SlotKey(string userId, int? groupId)
		{
			return (userId?.ToUpperInvariant(), groupId);
		}

		private static ShiftRosterSources GetSignupSource(ShiftSignup signup, HashSet<string> standingRoster)
		{
			if (!String.IsNullOrWhiteSpace(signup.AssignedByUserId))
				return ShiftRosterSources.SupervisorAssigned;

			// A standing-roster person's own signup for a day is the per-day slot created when they asked for a trade;
			// they are still there because they are scheduled, not because they signed up.
			if (standingRoster.Contains(signup.UserId))
				return ShiftRosterSources.Assigned;

			return ShiftRosterSources.Signup;
		}

		private static Dictionary<int, (string UserId, int ShiftSignupTradeId)> GetTradeReplacements(IEnumerable<ShiftSignupTrade> trades)
		{
			var replacements = new Dictionary<int, (string UserId, int ShiftSignupTradeId)>();

			if (trades == null)
				return replacements;

			foreach (var trade in trades.Where(x => x != null && x.IsTradeComplete() && x.SourceShiftSignup != null))
			{
				var source = trade.SourceShiftSignup;
				var target = trade.TargetShiftSignup;

				var sourceTaker = !String.IsNullOrWhiteSpace(trade.UserId) ? trade.UserId : target?.UserId;

				if (!String.IsNullOrWhiteSpace(sourceTaker))
					replacements[source.ShiftSignupId] = (sourceTaker, trade.ShiftSignupTradeId);

				// Swap-back: the requester works the day they took in exchange.
				if (target != null && !String.IsNullOrWhiteSpace(source.UserId))
					replacements[target.ShiftSignupId] = (source.UserId, trade.ShiftSignupTradeId);
			}

			return replacements;
		}

		private static void AddEntry(List<ShiftDayRosterEntry> entries, ShiftDayRosterEntry entry)
		{
			var existing = entries.FirstOrDefault(x => String.Equals(x.UserId, entry.UserId, StringComparison.OrdinalIgnoreCase) &&
			                                           x.DepartmentGroupId == entry.DepartmentGroupId);

			if (existing == null)
			{
				entries.Add(entry);
				return;
			}

			// The same person twice in the same group (say, traded into a slot on a day they already had): keep one
			// entry, and prefer the one that actually puts them on duty.
			if (existing.ApprovalPending && !entry.ApprovalPending)
				entries[entries.IndexOf(existing)] = entry;
		}
	}
}
