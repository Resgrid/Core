using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>Desk activity numbers for a period from items, their history and breaks (pure).</summary>
	public static class DispatcherDeskStatsCalculator
	{
		public static DispatcherQueueStats Build(IReadOnlyCollection<DispatcherQueueItem> items, IReadOnlyCollection<DispatcherQueueEvent> events,
			IReadOnlyCollection<DispatcherBreak> breaks, Func<string, string> nameOf)
		{
			items ??= Array.Empty<DispatcherQueueItem>();
			events ??= Array.Empty<DispatcherQueueEvent>();
			breaks ??= Array.Empty<DispatcherBreak>();
			nameOf ??= id => id;

			var stats = new DispatcherQueueStats
			{
				Created = items.Count,
				Completed = items.Count(x => x.State == (int)DispatcherQueueItemStates.Completed),
				Overflowed = items.Count(x => x.OverflowedOn.HasValue),
				Cancelled = items.Count(x => x.State == (int)DispatcherQueueItemStates.Cancelled),
				Escalated = items.Count(x => x.EscalatedOn.HasValue),
				AverageSecondsToAnswer = Average(items.Where(x => x.AcceptedOn.HasValue).Select(x => (x.AcceptedOn.Value - x.CreatedOn).TotalSeconds)),
				CreatedByKind = items.GroupBy(x => x.Kind).ToDictionary(g => g.Key, g => g.Count())
			};

			var byItem = events.GroupBy(x => x.DispatcherQueueItemId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.OccurredOn).ToList());
			var users = events.Where(x => !string.IsNullOrWhiteSpace(x.UserId)).Select(x => x.UserId)
				.Concat(breaks.Select(x => x.UserId))
				.Concat(items.Where(x => !string.IsNullOrWhiteSpace(x.CompletedByUserId)).Select(x => x.CompletedByUserId))
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			foreach (var userId in users)
			{
				var mine = events.Where(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
				var accepted = mine.Where(x => x.EventType == (int)DispatcherQueueEventTypes.Accepted).ToList();

				var toAccept = new List<double>();
				var toComplete = new List<double>();
				foreach (var accept in accepted)
				{
					if (!byItem.TryGetValue(accept.DispatcherQueueItemId, out var history))
						continue;

					var offer = history.LastOrDefault(x => x.EventType == (int)DispatcherQueueEventTypes.Offered && x.OccurredOn <= accept.OccurredOn &&
														  string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
					var created = history.FirstOrDefault(x => x.EventType == (int)DispatcherQueueEventTypes.Created);
					var from = offer?.OccurredOn ?? created?.OccurredOn;
					if (from.HasValue)
						toAccept.Add((accept.OccurredOn - from.Value).TotalSeconds);

					var completed = history.FirstOrDefault(x => x.EventType == (int)DispatcherQueueEventTypes.Completed && x.OccurredOn >= accept.OccurredOn);
					if (completed != null)
						toComplete.Add((completed.OccurredOn - accept.OccurredOn).TotalSeconds);
				}

				var myBreaks = breaks.Where(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
				var taken = myBreaks.Where(x => x.StartedOn.HasValue).ToList();
				var overdue = taken.Where(x => x.ExpectedReturnOn.HasValue && x.EndedOn.HasValue && x.EndedOn.Value > x.ExpectedReturnOn.Value.AddMinutes(1)).ToList();
				var completedItems = items.Where(x => x.State == (int)DispatcherQueueItemStates.Completed &&
													  string.Equals(x.CompletedByUserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();

				stats.Dispatchers.Add(new DispatcherDeskStats
				{
					UserId = userId,
					Name = nameOf(userId),
					Offered = mine.Count(x => x.EventType == (int)DispatcherQueueEventTypes.Offered),
					Accepted = accepted.Count,
					Declined = mine.Count(x => x.EventType == (int)DispatcherQueueEventTypes.Declined),
					OffersMissed = mine.Count(x => x.EventType == (int)DispatcherQueueEventTypes.OfferExpired),
					Completed = completedItems.Count,
					Dispatched = completedItems.Count(x => x.Outcome == (int)DispatcherQueueItemOutcomes.Dispatched || x.Outcome == (int)DispatcherQueueItemOutcomes.CallCreated),
					AverageSecondsToAccept = Average(toAccept),
					AverageSecondsToComplete = Average(toComplete),
					Breaks = taken.Count,
					BreakMinutes = (int)Math.Round(taken.Sum(x => ((x.EndedOn ?? x.ExpectedReturnOn ?? x.StartedOn.Value) - x.StartedOn.Value).TotalMinutes)),
					BreaksOverdue = overdue.Count,
					OverdueMinutes = (int)Math.Round(overdue.Sum(x => (x.EndedOn.Value - x.ExpectedReturnOn.Value).TotalMinutes)),
					BreaksMissed = myBreaks.Count(x => x.State == (int)DispatcherBreakStates.Missed)
				});
			}

			stats.Dispatchers = stats.Dispatchers.OrderBy(x => x.Name ?? x.UserId, StringComparer.OrdinalIgnoreCase).ToList();
			return stats;
		}

		private static double? Average(IEnumerable<double> values)
		{
			var list = values.Where(x => x >= 0).ToList();
			return list.Count == 0 ? (double?)null : Math.Round(list.Average(), 1);
		}
	}
}
