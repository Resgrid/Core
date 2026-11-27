using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Services.Wellness
{
	/// <summary>
	/// Who the dispatch data places on a call, and for how long (§13b WL0 "post-call suggested entries"). Pure: the service loads the
	/// call's personnel statuses, unit states with their crews (the unit response timeline) and check-ins, and this decides. A member
	/// is placed on the call by any of them; their window runs from their first to their last fact, and a unit's crew shares the
	/// unit's window. A "Not Responding" status places nobody. The result is only ever a suggestion the member or a wellness officer
	/// confirms.
	/// </summary>
	public static class ResponderExposureSuggestionBuilder
	{
		public static List<ExposureSuggestionCandidate> Build(IEnumerable<ActionLog> statuses, IEnumerable<UnitState> unitStates,
			IEnumerable<CheckInRecord> checkIns, IEnumerable<string> eligibleUserIds)
		{
			var eligible = new HashSet<string>(eligibleUserIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			var byUser = new Dictionary<string, ExposureSuggestionCandidate>(StringComparer.OrdinalIgnoreCase);

			void Place(string userId, DateTime start, DateTime end, string basis)
			{
				if (string.IsNullOrWhiteSpace(userId) || !eligible.Contains(userId))
					return;
				if (end < start)
					(start, end) = (end, start);

				if (!byUser.TryGetValue(userId, out var candidate))
				{
					// The stored id keeps the casing the member list holds, so a later lookup by the profile id matches.
					var stored = eligible.TryGetValue(userId, out var actual) ? actual : userId;
					candidate = new ExposureSuggestionCandidate { UserId = stored, StartedOn = start, EndedOn = end };
					byUser[userId] = candidate;
				}
				else
				{
					if (start < candidate.StartedOn) candidate.StartedOn = start;
					if (end > candidate.EndedOn) candidate.EndedOn = end;
				}
				candidate.Bases.Add(basis);
			}

			foreach (var status in statuses ?? Enumerable.Empty<ActionLog>())
			{
				if (status == null || status.ActionTypeId == (int)ActionTypes.NotResponding)
					continue;
				Place(status.UserId, status.Timestamp, status.Timestamp, ResponderExposureSuggestionBases.Status);
			}

			// A unit's response window on the call: its first to its last state there. Everyone it carried in a role shares it.
			foreach (var unit in (unitStates ?? Enumerable.Empty<UnitState>()).Where(s => s != null).GroupBy(s => s.UnitId))
			{
				var first = unit.Min(s => s.Timestamp);
				var last = unit.Max(s => s.Timestamp);
				foreach (var role in unit.Where(s => s.Roles != null).SelectMany(s => s.Roles).Where(r => r != null))
				{
					Place(role.UserId, first, last, ResponderExposureSuggestionBases.UnitCrew);
					byUser.TryGetValue(role.UserId ?? string.Empty, out var placed);
					placed?.Bases.Add(ResponderExposureSuggestionBases.UnitResponse);
				}
			}

			foreach (var checkIn in checkIns ?? Enumerable.Empty<CheckInRecord>())
			{
				if (checkIn == null)
					continue;
				Place(checkIn.UserId, checkIn.Timestamp, checkIn.Timestamp, ResponderExposureSuggestionBases.CheckIn);
			}

			return byUser.Values.OrderBy(c => c.StartedOn).ThenBy(c => c.UserId, StringComparer.OrdinalIgnoreCase).ToList();
		}
	}
}
