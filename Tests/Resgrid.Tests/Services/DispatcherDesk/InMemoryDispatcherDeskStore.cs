using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>
	/// In-memory desk repositories for service tests: the conditional writes (Version, state transitions, alert stamps) behave
	/// like the SQL ones, so races and replays are testable without a database.
	/// </summary>
	public class InMemoryDispatcherDeskStore
	{
		public List<DispatcherQueueItem> Items { get; } = new List<DispatcherQueueItem>();
		public List<DispatcherQueueEvent> Events { get; } = new List<DispatcherQueueEvent>();
		public List<DispatcherConversationMessage> Messages { get; } = new List<DispatcherConversationMessage>();
		public List<DispatcherPresence> Presences { get; } = new List<DispatcherPresence>();
		public List<DispatcherBreak> Breaks { get; } = new List<DispatcherBreak>();

		/// <summary>Runs before each conditional break write or alert stamp: a concurrent writer acting between a read and the write.</summary>
		public Action<DispatcherBreak> BeforeBreakWrite { get; set; }

		/// <summary>Departments whose break start took the department lock, in order.</summary>
		public List<int> BreakStartLocks { get; } = new List<int>();

		/// <summary>
		/// Runs on the stored row just before each conditional item update is checked, so a test can play a concurrent writer (bump the
		/// stored Version, change its state) between a service's read and its write.
		/// </summary>
		public Action<DispatcherQueueItem> BeforeStoredItemUpdate { get; set; }

		public IDispatcherQueueItemsRepository ItemsRepository => new ItemsRepo(this);
		public IDispatcherQueueEventsRepository EventsRepository => new EventsRepo(this);
		public IDispatcherConversationMessagesRepository MessagesRepository => new MessagesRepo(this);
		public IDispatcherPresencesRepository PresencesRepository => new PresencesRepo(this);
		public IDispatcherBreaksRepository BreaksRepository => new BreaksRepo(this);

		/// <summary>Runs just before a queue item insert checks the unique open-item indexes (a racing writer's insert lands in between).</summary>
		public Action<DispatcherQueueItem> BeforeItemInsert { get; set; }

		/// <summary>Runs just before a queue item's conditional update is applied (a racing writer lands in between).</summary>
		public Action<DispatcherQueueItem> BeforeItemUpdate { get; set; }

		public DispatcherPresence AddPresence(int departmentId, string userId, DispatcherPresenceStates state, DateTime now, DateTime? lastOffered = null)
		{
			var presence = new DispatcherPresence
			{
				DispatcherPresenceId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				UserId = userId,
				State = (int)state,
				StateChangedOn = now,
				SignedInOn = state == DispatcherPresenceStates.Offline ? (DateTime?)null : now,
				LastSeenOn = now,
				LastOfferedOn = lastOffered
			};
			Presences.Add(presence);
			return presence;
		}

		private static T Clone<T>(T value) where T : class => value == null ? null : (T)typeof(T).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(value, null);

		private abstract class RepoBase<T> : IRepository<T> where T : class, IEntity
		{
			protected readonly InMemoryDispatcherDeskStore Store;
			protected RepoBase(InMemoryDispatcherDeskStore store) { Store = store; }
			protected abstract List<T> List { get; }

			public Task<IEnumerable<T>> GetAllAsync() => Task.FromResult<IEnumerable<T>>(List.Select(Clone).ToList());

			public Task<T> GetByIdAsync(object id) =>
				Task.FromResult(Clone(List.FirstOrDefault(x => string.Equals(x.IdValue?.ToString(), id?.ToString(), StringComparison.OrdinalIgnoreCase))));

			public Task<IEnumerable<T>> GetAllByDepartmentIdAsync(int departmentId) => throw new NotSupportedException();

			public Task<T> InsertAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
			{
				if (entity.IdType == 1 && string.IsNullOrWhiteSpace(entity.IdValue?.ToString()))
					entity.IdValue = Guid.NewGuid().ToString();
				List.Add(Clone(entity));
				return Task.FromResult(entity);
			}

			public Task<T> UpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
			{
				var index = List.FindIndex(x => string.Equals(x.IdValue?.ToString(), entity.IdValue?.ToString(), StringComparison.OrdinalIgnoreCase));
				if (index >= 0)
					List[index] = Clone(entity);
				return Task.FromResult(entity);
			}

			public Task<bool> DeleteAsync(T entity, CancellationToken cancellationToken) =>
				Task.FromResult(List.RemoveAll(x => string.Equals(x.IdValue?.ToString(), entity.IdValue?.ToString(), StringComparison.OrdinalIgnoreCase)) > 0);

			public Task<T> SaveOrUpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
			{
				if (string.IsNullOrWhiteSpace(entity.IdValue?.ToString()) ||
					!List.Any(x => string.Equals(x.IdValue?.ToString(), entity.IdValue?.ToString(), StringComparison.OrdinalIgnoreCase)))
					return InsertAsync(entity, cancellationToken, firstLevelOnly);
				return UpdateAsync(entity, cancellationToken, firstLevelOnly);
			}

			public Task<IEnumerable<T>> GetAllByUserIdAsync(string userId) => throw new NotSupportedException();

			public Task<bool> DeleteMultipleAsync(T entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) =>
				throw new NotSupportedException();
		}

		private sealed class ItemsRepo : RepoBase<DispatcherQueueItem>, IDispatcherQueueItemsRepository
		{
			public ItemsRepo(InMemoryDispatcherDeskStore store) : base(store) { }
			protected override List<DispatcherQueueItem> List => Store.Items;

			public Task<DispatcherQueueItem> GetItemAsync(string id) => GetByIdAsync(id);

			public Task<IEnumerable<DispatcherQueueItem>> GetOpenForDepartmentAsync(int departmentId) =>
				Task.FromResult<IEnumerable<DispatcherQueueItem>>(List.Where(x => x.DepartmentId == departmentId && x.State < (int)DispatcherQueueItemStates.Completed)
					.OrderBy(x => x.CreatedOn).Select(Clone).ToList());

			public Task<IEnumerable<DispatcherQueueItem>> GetOpenForCallAsync(int departmentId, int callId) =>
				Task.FromResult<IEnumerable<DispatcherQueueItem>>(List.Where(x => x.DepartmentId == departmentId && x.CallId == callId &&
					x.State < (int)DispatcherQueueItemStates.Completed).Select(Clone).ToList());

			public Task<bool> AnyForCallAsync(int departmentId, int callId) => Task.FromResult(List.Any(x => x.DepartmentId == departmentId && x.CallId == callId));

			public Task<IEnumerable<int>> GetCallIdsOnDeskAsync(int departmentId, IEnumerable<int> callIds)
			{
				var wanted = new HashSet<int>(callIds ?? Enumerable.Empty<int>());
				return Task.FromResult<IEnumerable<int>>(List.Where(x => x.DepartmentId == departmentId && x.CallId.HasValue && wanted.Contains(x.CallId.Value))
					.Select(x => x.CallId.Value).Distinct().ToList());
			}

			/// <summary>The unique open-item indexes (M0324): one open item per department call and per external reference.</summary>
			private bool ClashesWithOpenItem(DispatcherQueueItem item) =>
				item.State < (int)DispatcherQueueItemStates.Completed && List.Any(x => x.DispatcherQueueItemId != item.DispatcherQueueItemId &&
					x.DepartmentId == item.DepartmentId && x.State < (int)DispatcherQueueItemStates.Completed &&
					(item.CallId.HasValue && x.CallId == item.CallId ||
					 !string.IsNullOrWhiteSpace(item.ExternalReference) && string.Equals(x.ExternalReference, item.ExternalReference, StringComparison.OrdinalIgnoreCase)));

			public async Task<bool> TryInsertAsync(DispatcherQueueItem item, CancellationToken cancellationToken = default)
			{
				Store.BeforeItemInsert?.Invoke(item);
				if (ClashesWithOpenItem(item))
					return false;
				await InsertAsync(item, cancellationToken, true);
				return true;
			}

			public Task<DispatcherQueueItem> GetOpenByConversationKeyAsync(int departmentId, string key, DateTime since) =>
				Task.FromResult(Clone(List.Where(x => x.DepartmentId == departmentId && string.Equals(x.ConversationKey, key, StringComparison.OrdinalIgnoreCase) &&
					x.State < (int)DispatcherQueueItemStates.Completed && x.LastActivityOn >= since).OrderByDescending(x => x.LastActivityOn).FirstOrDefault()));

			public Task<DispatcherQueueItem> GetOpenByConversationKeyAnyDepartmentAsync(string key, DateTime since) =>
				Task.FromResult(Clone(List.Where(x => string.Equals(x.ConversationKey, key, StringComparison.OrdinalIgnoreCase) &&
					x.State < (int)DispatcherQueueItemStates.Completed && x.LastActivityOn >= since).OrderByDescending(x => x.LastActivityOn).FirstOrDefault()));

			public Task<DispatcherQueueItem> GetByExternalReferenceAsync(string reference) =>
				Task.FromResult(Clone(List.Where(x => string.Equals(x.ExternalReference, reference, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.CreatedOn).FirstOrDefault()));

			public Task<bool> TryUpdateAsync(DispatcherQueueItem item, int expectedVersion, CancellationToken cancellationToken = default)
			{
				Store.BeforeItemUpdate?.Invoke(item);
				var index = List.FindIndex(x => x.DispatcherQueueItemId == item.DispatcherQueueItemId);
				if (index >= 0)
					Store.BeforeStoredItemUpdate?.Invoke(List[index]);
				if (index < 0 || List[index].Version != expectedVersion || ClashesWithOpenItem(item))
					return Task.FromResult(false);

				item.Version = expectedVersion + 1;
				List[index] = Clone(item);
				return Task.FromResult(true);
			}

			public Task<IEnumerable<int>> GetDepartmentsWithOpenItemsAsync() =>
				Task.FromResult<IEnumerable<int>>(List.Where(x => x.State < (int)DispatcherQueueItemStates.Completed).Select(x => x.DepartmentId).Distinct().ToList());

			public Task<IEnumerable<DispatcherQueueItem>> GetCreatedInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc) =>
				Task.FromResult<IEnumerable<DispatcherQueueItem>>(List.Where(x => x.DepartmentId == departmentId && x.CreatedOn >= fromUtc && x.CreatedOn < toUtc).Select(Clone).ToList());

			public Task<int> PurgeClosedBeforeAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) =>
				Task.FromResult(List.RemoveAll(x => x.State >= (int)DispatcherQueueItemStates.Completed && x.LastActivityOn < beforeUtc));
		}

		private sealed class EventsRepo : RepoBase<DispatcherQueueEvent>, IDispatcherQueueEventsRepository
		{
			public EventsRepo(InMemoryDispatcherDeskStore store) : base(store) { }
			protected override List<DispatcherQueueEvent> List => Store.Events;

			public Task<IEnumerable<DispatcherQueueEvent>> GetForItemAsync(string id) =>
				Task.FromResult<IEnumerable<DispatcherQueueEvent>>(List.Where(x => x.DispatcherQueueItemId == id).OrderBy(x => x.OccurredOn).Select(Clone).ToList());

			public Task<IEnumerable<DispatcherQueueEvent>> GetForDepartmentInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc) =>
				Task.FromResult<IEnumerable<DispatcherQueueEvent>>(List.Where(x => x.DepartmentId == departmentId && x.OccurredOn >= fromUtc && x.OccurredOn < toUtc).Select(Clone).ToList());
		}

		private sealed class MessagesRepo : RepoBase<DispatcherConversationMessage>, IDispatcherConversationMessagesRepository
		{
			public MessagesRepo(InMemoryDispatcherDeskStore store) : base(store) { }
			protected override List<DispatcherConversationMessage> List => Store.Messages;

			public Task<IEnumerable<DispatcherConversationMessage>> GetForItemAsync(string id) =>
				Task.FromResult<IEnumerable<DispatcherConversationMessage>>(List.Where(x => x.DispatcherQueueItemId == id).OrderBy(x => x.CreatedOn).Select(Clone).ToList());

			public Task<DispatcherConversationMessage> GetByProviderMessageIdAsync(string id) =>
				Task.FromResult(Clone(List.FirstOrDefault(x => string.Equals(x.ProviderMessageId, id, StringComparison.OrdinalIgnoreCase))));

			/// <summary>As the unique filtered index on ProviderMessageId: a second row with the same provider id is refused.</summary>
			public async Task<bool> TryInsertAsync(DispatcherConversationMessage message, CancellationToken cancellationToken = default)
			{
				if (!string.IsNullOrWhiteSpace(message.ProviderMessageId) &&
					List.Any(x => string.Equals(x.ProviderMessageId, message.ProviderMessageId, StringComparison.OrdinalIgnoreCase)))
					return false;

				await InsertAsync(message, cancellationToken, true);
				return true;
			}
		}

		private sealed class PresencesRepo : RepoBase<DispatcherPresence>, IDispatcherPresencesRepository
		{
			public PresencesRepo(InMemoryDispatcherDeskStore store) : base(store) { }
			protected override List<DispatcherPresence> List => Store.Presences;

			public Task<DispatcherPresence> GetForUserAsync(int departmentId, string userId) =>
				Task.FromResult(Clone(List.FirstOrDefault(x => x.DepartmentId == departmentId && string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase))));

			public Task<IEnumerable<DispatcherPresence>> GetByDepartmentIdAsync(int departmentId) =>
				Task.FromResult<IEnumerable<DispatcherPresence>>(List.Where(x => x.DepartmentId == departmentId).Select(Clone).ToList());

			public Task<bool> SetLastOfferedOnAsync(string id, DateTime offeredOnUtc, CancellationToken cancellationToken = default)
			{
				var row = List.FirstOrDefault(x => string.Equals(x.DispatcherPresenceId, id, StringComparison.OrdinalIgnoreCase));
				if (row == null)
					return Task.FromResult(false);
				row.LastOfferedOn = offeredOnUtc;
				return Task.FromResult(true);
			}

			public Task<IEnumerable<DispatcherPresence>> GetSignedInAsync(CancellationToken cancellationToken = default) =>
				Task.FromResult<IEnumerable<DispatcherPresence>>(List.Where(x => x.State > 0).Select(Clone).ToList());

			public async Task<bool> TryInsertAsync(DispatcherPresence presence, CancellationToken cancellationToken = default)
			{
				if (List.Any(x => x.DepartmentId == presence.DepartmentId && string.Equals(x.UserId, presence.UserId, StringComparison.OrdinalIgnoreCase)))
					return false;
				await InsertAsync(presence, cancellationToken);
				return true;
			}

			public Task<bool> TouchAsync(string id, DateTime lastSeenOnUtc, CancellationToken cancellationToken = default)
			{
				var row = List.FirstOrDefault(x => string.Equals(x.DispatcherPresenceId, id, StringComparison.OrdinalIgnoreCase));
				if (row == null || row.State == (int)DispatcherPresenceStates.Offline)
					return Task.FromResult(false);

				row.LastSeenOn = lastSeenOnUtc;
				return Task.FromResult(true);
			}

			public Task<bool> TrySignOffAsync(string id, DateTime? expectedSignedInOn, DateTime nowUtc, CancellationToken cancellationToken = default)
			{
				var row = List.FirstOrDefault(x => string.Equals(x.DispatcherPresenceId, id, StringComparison.OrdinalIgnoreCase));
				if (row == null || row.State == (int)DispatcherPresenceStates.Offline || row.SignedInOn != expectedSignedInOn)
					return Task.FromResult(false);

				row.State = (int)DispatcherPresenceStates.Offline;
				row.StateChangedOn = nowUtc;
				row.ActiveBreakId = null;
				row.AwayReason = null;
				row.MissedOffers = 0;
				return Task.FromResult(true);
			}

			public Task<bool> TrySetDutyAsync(string id, DateTime? expectedSignedInOn, int dutyMode, int? dutyShiftId, int? dutyShiftDayId, DateTime? dutyEndsOn,
				CancellationToken cancellationToken = default)
			{
				var row = List.FirstOrDefault(x => string.Equals(x.DispatcherPresenceId, id, StringComparison.OrdinalIgnoreCase));
				if (row == null || row.State == (int)DispatcherPresenceStates.Offline || row.SignedInOn != expectedSignedInOn)
					return Task.FromResult(false);

				row.DutyMode = dutyMode;
				row.DutyShiftId = dutyShiftId;
				row.DutyShiftDayId = dutyShiftDayId;
				row.DutyEndsOn = dutyEndsOn;
				return Task.FromResult(true);
			}
		}

		private sealed class BreaksRepo : RepoBase<DispatcherBreak>, IDispatcherBreaksRepository
		{
			public BreaksRepo(InMemoryDispatcherDeskStore store) : base(store) { }
			protected override List<DispatcherBreak> List => Store.Breaks;

			public Task<IEnumerable<DispatcherBreak>> GetForDepartmentInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc) =>
				Task.FromResult<IEnumerable<DispatcherBreak>>(List.Where(x => x.DepartmentId == departmentId &&
					(x.ScheduledStartOn >= fromUtc && x.ScheduledStartOn < toUtc || x.StartedOn >= fromUtc && x.StartedOn < toUtc || x.State == (int)DispatcherBreakStates.Active))
					.Select(Clone).ToList());

			public Task<IEnumerable<DispatcherBreak>> GetForShiftDayAsync(int shiftDayId) =>
				Task.FromResult<IEnumerable<DispatcherBreak>>(List.Where(x => x.ShiftDayId == shiftDayId).Select(Clone).ToList());

			public Task<IEnumerable<DispatcherBreak>> GetOpenForUserAsync(int departmentId, string userId, DateTime fromUtc, DateTime toUtc) =>
				Task.FromResult<IEnumerable<DispatcherBreak>>(List.Where(x => x.DepartmentId == departmentId && string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
					(x.State == (int)DispatcherBreakStates.Active || x.State == (int)DispatcherBreakStates.Scheduled && x.ScheduledStartOn >= fromUtc && x.ScheduledStartOn < toUtc))
					.Select(Clone).ToList());

			public Task<DispatcherBreak> GetActiveForUserAsync(int departmentId, string userId) =>
				Task.FromResult(Clone(List.FirstOrDefault(x => x.DepartmentId == departmentId && string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
					x.State == (int)DispatcherBreakStates.Active)));

			public Task<IEnumerable<DispatcherBreak>> GetForTimersAsync(DateTime before, DateTime after) =>
				Task.FromResult<IEnumerable<DispatcherBreak>>(List.Where(x => x.State == (int)DispatcherBreakStates.Active ||
					x.State == (int)DispatcherBreakStates.Scheduled && x.ScheduledStartOn < before && x.ScheduledStartOn >= after).Select(Clone).ToList());

			public Task<IEnumerable<DispatcherBreak>> GetOverdueScheduledAsync(DateTime before, int limit) =>
				Task.FromResult<IEnumerable<DispatcherBreak>>(List.Where(x => x.State == (int)DispatcherBreakStates.Scheduled && x.ScheduledStartOn < before)
					.OrderBy(x => x.ScheduledStartOn).Take(Math.Max(1, limit)).Select(Clone).ToList());

			public Task<bool> TryTransitionAsync(DispatcherBreak updated, int fromState, CancellationToken cancellationToken = default) =>
				Task.FromResult(Write(updated, stored => stored.State == fromState, false));

			public Task<bool> TryUpdateObservedAsync(DispatcherBreak updated, DispatcherBreak observed, bool resetPlanAlerts, CancellationToken cancellationToken = default) =>
				Task.FromResult(observed != null && Write(updated, stored => Matches(stored, observed), resetPlanAlerts));

			private bool Write(DispatcherBreak updated, Func<DispatcherBreak, bool> condition, bool resetPlanAlerts)
			{
				Store.BeforeBreakWrite?.Invoke(updated);
				var index = List.FindIndex(x => x.DispatcherBreakId == updated.DispatcherBreakId);
				if (index < 0 || !condition(List[index]))
					return false;

				// Like the SQL: the plan and state columns are written, the alert stamps are left alone (or the plan ones cleared).
				var stored = List[index];
				var copy = Clone(updated);
				copy.ReminderSentOn = resetPlanAlerts ? null : stored.ReminderSentOn;
				copy.DueAlertSentOn = resetPlanAlerts ? null : stored.DueAlertSentOn;
				copy.ReturnAlertSentOn = stored.ReturnAlertSentOn;
				copy.OverdueAlertSentOn = stored.OverdueAlertSentOn;
				copy.CreatedOn = stored.CreatedOn;
				copy.CreatedByUserId = stored.CreatedByUserId;
				List[index] = copy;
				return true;
			}

			private static bool Matches(DispatcherBreak stored, DispatcherBreak observed) =>
				stored.State == observed.State && stored.DurationMinutes == observed.DurationMinutes && stored.ScheduledStartOn == observed.ScheduledStartOn;

			private static readonly HashSet<string> AlertColumns = new HashSet<string>(StringComparer.Ordinal)
			{
				nameof(DispatcherBreak.ReminderSentOn), nameof(DispatcherBreak.DueAlertSentOn), nameof(DispatcherBreak.ReturnAlertSentOn), nameof(DispatcherBreak.OverdueAlertSentOn)
			};

			public Task<bool> TryMarkAlertAsync(DispatcherBreak observed, string column, DateTime sentOnUtc, CancellationToken cancellationToken = default)
			{
				if (!AlertColumns.Contains(column))
					throw new ArgumentOutOfRangeException(nameof(column));

				Store.BeforeBreakWrite?.Invoke(observed);
				var row = List.FirstOrDefault(x => x.DispatcherBreakId == observed?.DispatcherBreakId);
				if (row == null || !Matches(row, observed))
					return Task.FromResult(false);

				var property = typeof(DispatcherBreak).GetProperty(column);
				if (property.GetValue(row) != null)
					return Task.FromResult(false);

				property.SetValue(row, sentOnUtc);
				return Task.FromResult(true);
			}

			public Task LockDepartmentForBreakStartAsync(int departmentId, CancellationToken cancellationToken = default)
			{
				Store.BreakStartLocks.Add(departmentId);
				return Task.CompletedTask;
			}

			public Task<int> PurgeBeforeAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
		}
	}
}
