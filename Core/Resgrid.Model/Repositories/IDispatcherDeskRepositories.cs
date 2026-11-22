using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	// Dispatcher desk repositories (M0324). Inserts and plain updates go through IRepository<T>.SaveOrUpdateAsync / InsertAsync;
	// the methods here are the reads and the conditional writes the desk needs to stay correct under concurrency.

	public interface IShiftDispatchSettingsRepository : IRepository<ShiftDispatchSetting>
	{
		Task<ShiftDispatchSetting> GetByShiftIdAsync(int shiftId);

		Task<IEnumerable<ShiftDispatchSetting>> GetByDepartmentIdAsync(int departmentId);

		Task<int> DeleteByShiftIdAsync(int shiftId, CancellationToken cancellationToken = default);
	}

	/// <summary>A shift day with its shift's times, read cheaply for duty windows (no signups, no groups).</summary>
	public class ShiftDayTimesRow
	{
		public int ShiftId { get; set; }
		public int ShiftDayId { get; set; }
		public DateTime Day { get; set; }
		public string ShiftName { get; set; }
		public string Color { get; set; }
		public string StartTime { get; set; }
		public string EndTime { get; set; }
		public int? Hours { get; set; }
		public int AssignmentType { get; set; }
		public bool? RequireApproval { get; set; }
	}

	public interface IShiftDispatchersRepository : IRepository<ShiftDispatcher>
	{
		Task<IEnumerable<ShiftDispatcher>> GetByShiftIdAsync(int shiftId);

		Task<IEnumerable<ShiftDispatcher>> GetByDepartmentIdAsync(int departmentId);

		Task<IEnumerable<ShiftDispatcher>> GetByUserIdAsync(int departmentId, string userId);

		/// <summary>Days (with their shift's times) of the given shifts whose local date is in [fromLocalDate, toLocalDate].</summary>
		Task<IEnumerable<ShiftDayTimesRow>> GetShiftDayTimesAsync(int departmentId, IEnumerable<int> shiftIds, DateTime fromLocalDate, DateTime toLocalDate);

		Task<ShiftDayTimesRow> GetShiftDayTimesByIdAsync(int shiftDayId);

		Task<int> DeleteByShiftIdAsync(int shiftId, CancellationToken cancellationToken = default);
	}

	public interface IDispatcherBreaksRepository : IRepository<DispatcherBreak>
	{
		/// <summary>Breaks planned to start, or actually started, in [fromUtc, toUtc), plus every Active break.</summary>
		Task<IEnumerable<DispatcherBreak>> GetForDepartmentInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc);

		Task<IEnumerable<DispatcherBreak>> GetForShiftDayAsync(int shiftDayId);

		/// <summary>The user's Scheduled and Active breaks planned or started in [fromUtc, toUtc).</summary>
		Task<IEnumerable<DispatcherBreak>> GetOpenForUserAsync(int departmentId, string userId, DateTime fromUtc, DateTime toUtc);

		Task<DispatcherBreak> GetActiveForUserAsync(int departmentId, string userId);

		/// <summary>Every department's Scheduled breaks planned to start before <paramref name="beforeUtc"/> and Active breaks (worker).</summary>
		Task<IEnumerable<DispatcherBreak>> GetForTimersAsync(DateTime scheduledBeforeUtc, DateTime scheduledAfterUtc);

		/// <summary>Moves the break from one state to another only if it is still in <paramref name="fromState"/>.</summary>
		Task<bool> TryTransitionAsync(DispatcherBreak updated, int fromState, CancellationToken cancellationToken = default);

		/// <summary>Stamps one alert column (ReminderSentOn, DueAlertSentOn, ReturnAlertSentOn, OverdueAlertSentOn) if still null.</summary>
		Task<bool> TryMarkAlertAsync(string dispatcherBreakId, string column, DateTime sentOnUtc, CancellationToken cancellationToken = default);

		Task<int> PurgeBeforeAsync(DateTime beforeUtc, CancellationToken cancellationToken = default);
	}

	public interface IDispatcherPresencesRepository : IRepository<DispatcherPresence>
	{
		Task<DispatcherPresence> GetForUserAsync(int departmentId, string userId);

		Task<IEnumerable<DispatcherPresence>> GetByDepartmentIdAsync(int departmentId);

		/// <summary>Every signed-in presence in every department (worker).</summary>
		Task<IEnumerable<DispatcherPresence>> GetSignedInAsync();

		/// <summary>Inserts the row unless the department + user already has one; false when it exists (read it instead).</summary>
		Task<bool> TryInsertAsync(DispatcherPresence presence, CancellationToken cancellationToken = default);
	}

	public interface IDispatcherQueueItemsRepository : IRepository<DispatcherQueueItem>
	{
		Task<DispatcherQueueItem> GetItemAsync(string dispatcherQueueItemId);

		/// <summary>Queued, Offered and Accepted items, oldest first.</summary>
		Task<IEnumerable<DispatcherQueueItem>> GetOpenForDepartmentAsync(int departmentId);

		Task<IEnumerable<DispatcherQueueItem>> GetOpenForCallAsync(int departmentId, int callId);

		/// <summary>Whether the call has ever been on the desk (any state), so the sweep never re-adds a call a dispatcher already handled.</summary>
		Task<bool> AnyForCallAsync(int departmentId, int callId);

		/// <summary>The newest open item with this conversation key whose last activity is at or after <paramref name="activeSinceUtc"/>.</summary>
		Task<DispatcherQueueItem> GetOpenByConversationKeyAsync(int departmentId, string conversationKey, DateTime activeSinceUtc);

		/// <summary>The newest open item with this conversation key in any department (a reply to the master number).</summary>
		Task<DispatcherQueueItem> GetOpenByConversationKeyAnyDepartmentAsync(string conversationKey, DateTime activeSinceUtc);

		Task<DispatcherQueueItem> GetByExternalReferenceAsync(string externalReference);

		/// <summary>Writes every column of <paramref name="item"/> only if its stored Version still equals <paramref name="expectedVersion"/>; bumps Version.</summary>
		Task<bool> TryUpdateAsync(DispatcherQueueItem item, int expectedVersion, CancellationToken cancellationToken = default);

		/// <summary>Departments with any open item (worker).</summary>
		Task<IEnumerable<int>> GetDepartmentsWithOpenItemsAsync();

		/// <summary>Items created in [fromUtc, toUtc) (reports).</summary>
		Task<IEnumerable<DispatcherQueueItem>> GetCreatedInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc);

		/// <summary>Removes closed items (completed, overflowed, cancelled) last touched before the cutoff, with their events and messages.</summary>
		Task<int> PurgeClosedBeforeAsync(DateTime beforeUtc, CancellationToken cancellationToken = default);
	}

	public interface IDispatcherQueueEventsRepository : IRepository<DispatcherQueueEvent>
	{
		Task<IEnumerable<DispatcherQueueEvent>> GetForItemAsync(string dispatcherQueueItemId);

		Task<IEnumerable<DispatcherQueueEvent>> GetForDepartmentInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc);
	}

	public interface IDispatcherConversationMessagesRepository : IRepository<DispatcherConversationMessage>
	{
		Task<IEnumerable<DispatcherConversationMessage>> GetForItemAsync(string dispatcherQueueItemId);

		Task<DispatcherConversationMessage> GetByProviderMessageIdAsync(string providerMessageId);
	}
}
