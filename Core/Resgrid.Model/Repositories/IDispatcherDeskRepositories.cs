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

		/// <summary>
		/// Writes the row's review columns (Status, ReviewedByUserId, ReviewedOn, Note) only while its stored Source is still
		/// <paramref name="expectedSource"/>: a withdrawal checked against a signup row never denies a row a manager has since turned
		/// into an assignment. False when the row changed (or is gone).
		/// </summary>
		Task<bool> TryUpdateReviewWithSourceAsync(ShiftDispatcher row, int expectedSource, CancellationToken cancellationToken = default);
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

		/// <summary>
		/// The oldest Scheduled breaks planned to start before <paramref name="scheduledBeforeUtc"/>, at most <paramref name="limit"/>
		/// (worker catch-up: a break the timer window has already passed by, after the worker was down, is still marked missed).
		/// </summary>
		Task<IEnumerable<DispatcherBreak>> GetOverdueScheduledAsync(DateTime scheduledBeforeUtc, int limit);

		/// <summary>Moves the break from one state to another only if it is still in <paramref name="fromState"/>.</summary>
		Task<bool> TryTransitionAsync(DispatcherBreak updated, int fromState, CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes <paramref name="updated"/> only if the stored break still has the <paramref name="observed"/> state and plan (planned
		/// start and length), so a timer or editor that read the break cannot act on a plan someone has since changed. With
		/// <paramref name="resetPlanAlerts"/> the "starts soon" / "due now" stamps are cleared in the same statement (a re-planned break
		/// reminds again).
		/// </summary>
		Task<bool> TryUpdateObservedAsync(DispatcherBreak updated, DispatcherBreak observed, bool resetPlanAlerts, CancellationToken cancellationToken = default);

		/// <summary>
		/// Starts a planned break: writes the start columns alone (State, DurationMinutes, StartedOn, ExpectedReturnOn, UpdatedByUserId,
		/// UpdatedOn), and only while the stored break still has the <paramref name="observed"/> state and plan. The rest of the plan
		/// (type, note, shift and shift day) stays as stored, so an edit saved after the start read the break is not written back.
		/// </summary>
		Task<bool> TryStartObservedAsync(DispatcherBreak started, DispatcherBreak observed, CancellationToken cancellationToken = default);

		/// <summary>
		/// Stamps one alert column (ReminderSentOn, DueAlertSentOn, ReturnAlertSentOn, OverdueAlertSentOn) if still null and the break
		/// still has the <paramref name="observed"/> state and plan (planned start and length): a break started, ended or re-planned
		/// since the timer read it is not alerted on.
		/// </summary>
		Task<bool> TryMarkAlertAsync(DispatcherBreak observed, string column, DateTime sentOnUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gives back an alert claim (<see cref="TryMarkAlertAsync"/>) whose push delivery did not accept the alert: the column is cleared
		/// only while it still holds this claim's <paramref name="sentOnUtc"/>, so a later pass can send it again and a claim taken since
		/// (a re-plan reset the stamp and another pass claimed it) is left alone.
		/// </summary>
		Task<bool> TryClearAlertAsync(string dispatcherBreakId, string column, DateTime sentOnUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Serializes break starts and day plans in one department (the coverage check and the start commit; the read of a shift
		/// day's breaks and the new plan's inserts) for the rest of the unit of work's transaction. Needs the transaction; throws
		/// <see cref="TimeoutException"/> when another start or plan holds it too long.
		/// </summary>
		Task LockDepartmentForBreakStartAsync(int departmentId, CancellationToken cancellationToken = default);

		Task<int> PurgeBeforeAsync(DateTime beforeUtc, CancellationToken cancellationToken = default);
	}

	public interface IDispatcherPresencesRepository : IRepository<DispatcherPresence>
	{
		Task<DispatcherPresence> GetForUserAsync(int departmentId, string userId);

		Task<IEnumerable<DispatcherPresence>> GetByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Stamps only LastOfferedOn (who has waited longest for work). A routing pass holds the member's presence from before its
		/// offer, so writing the whole row back would undo a state change (a break started) made since.
		/// </summary>
		Task<bool> SetLastOfferedOnAsync(string dispatcherPresenceId, DateTime offeredOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Every signed-in presence in every department (worker).</summary>
		Task<IEnumerable<DispatcherPresence>> GetSignedInAsync(CancellationToken cancellationToken = default);

		/// <summary>Inserts the row unless the department + user already has one; false when it exists (read it instead).</summary>
		Task<bool> TryInsertAsync(DispatcherPresence presence, CancellationToken cancellationToken = default);

		/// <summary>
		/// The heartbeat: writes LastSeenOn alone, and only while the row is still signed in, so a heartbeat read before a
		/// sign-off can never write the signed-in state back. False when the row is signed off (or gone).
		/// </summary>
		Task<bool> TouchAsync(string dispatcherPresenceId, DateTime lastSeenOnUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Signs the row off (Offline, no break, no away reason, no missed offers) only while it is still the signed-in session
		/// that began at <paramref name="expectedSignedInOn"/>. False when that session is already signed off or a new one began.
		/// </summary>
		Task<bool> TrySignOffAsync(string dispatcherPresenceId, DateTime? expectedSignedInOn, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Moves the session's duty on (the next shift day, or unscheduled with no end), writing the duty columns alone and only
		/// while the row is still the signed-in session <paramref name="observed"/> read (same SignedInOn) with the duty the caller
		/// evaluated (DutyMode, DutyShiftId, DutyShiftDayId, DutyEndsOn): another process that moved the duty on since wins.
		/// </summary>
		Task<bool> TrySetDutyAsync(DispatcherPresence observed, int dutyMode, int? dutyShiftId, int? dutyShiftDayId, DateTime? dutyEndsOn,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Back from a break: Available, no active break, no missed offers (and LastSeenOn when given), written alone and only while the
		/// row still has the state, session (SignedInOn) and active break <paramref name="observed"/> read. A sign-off or another
		/// break committed since keeps its own state. False when the row changed.
		/// </summary>
		Task<bool> TryReturnFromBreakAsync(DispatcherPresence observed, DateTime nowUtc, DateTime? lastSeenOnUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// The sign-in write: the session columns of <paramref name="updated"/> (State, ActiveBreakId, StateChangedOn, SignedInOn,
		/// LastSeenOn, the duty columns, MissedOffers, AwayReason, DeviceKind, and CallbackNumber only when it is not null: a sign-in
		/// that supplies no number keeps the stored one), written only while the row still has the state, session (SignedInOn) and
		/// active break <paramref name="observed"/> read. A sign-in, break or sign-off committed since keeps its own state; the caller
		/// reads the row again. False when the row changed.
		/// </summary>
		Task<bool> TrySignInAsync(DispatcherPresence updated, DispatcherPresence observed, CancellationToken cancellationToken = default);
	}

	public interface IDispatcherQueueItemsRepository : IRepository<DispatcherQueueItem>
	{
		Task<DispatcherQueueItem> GetItemAsync(string dispatcherQueueItemId);

		/// <summary>Queued, Offered and Accepted items, oldest first.</summary>
		Task<IEnumerable<DispatcherQueueItem>> GetOpenForDepartmentAsync(int departmentId);

		Task<IEnumerable<DispatcherQueueItem>> GetOpenForCallAsync(int departmentId, int callId);

		/// <summary>Whether the call has ever been on the desk (any state), so the sweep never re-adds a call a dispatcher already handled.</summary>
		Task<bool> AnyForCallAsync(int departmentId, int callId);

		/// <summary>The calls among <paramref name="callIds"/> that have ever been on the desk (any state): the sweep's one read per batch.</summary>
		Task<IEnumerable<int>> GetCallIdsOnDeskAsync(int departmentId, IEnumerable<int> callIds);

		/// <summary>
		/// Inserts a new item unless another open item of the department already has its call or external reference (the unique open-item
		/// indexes of M0324); false when it lost to that item (read the winner instead).
		/// </summary>
		Task<bool> TryInsertAsync(DispatcherQueueItem item, CancellationToken cancellationToken = default);

		/// <summary>The newest open item with this conversation key whose last activity is at or after <paramref name="activeSinceUtc"/>.</summary>
		Task<DispatcherQueueItem> GetOpenByConversationKeyAsync(int departmentId, string conversationKey, DateTime activeSinceUtc);

		/// <summary>The newest open item with this conversation key in any department (a reply to the master number).</summary>
		Task<DispatcherQueueItem> GetOpenByConversationKeyAnyDepartmentAsync(string conversationKey, DateTime activeSinceUtc);

		Task<DispatcherQueueItem> GetByExternalReferenceAsync(string externalReference);

		/// <summary>
		/// The open item in <paramref name="departmentId"/> with this external reference: the row the unique open-reference index
		/// (department, reference, open states) lets win an insert race, whatever other departments or closed items share the reference.
		/// </summary>
		Task<DispatcherQueueItem> GetOpenByExternalReferenceAsync(int departmentId, string externalReference);

		/// <summary>
		/// Writes every column of <paramref name="item"/> only if its stored Version still equals <paramref name="expectedVersion"/>; bumps Version.
		/// False as well when the write would give a second open item the department's call or external reference (the unique open-item indexes).
		/// </summary>
		Task<bool> TryUpdateAsync(DispatcherQueueItem item, int expectedVersion, CancellationToken cancellationToken = default);

		/// <summary>Departments with any open item (worker).</summary>
		Task<IEnumerable<int>> GetDepartmentsWithOpenItemsAsync();

		/// <summary>Items created in [fromUtc, toUtc) (reports).</summary>
		Task<IEnumerable<DispatcherQueueItem>> GetCreatedInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc);

		/// <summary>
		/// Overflowed voice items, in every department, created since <paramref name="createdSinceUtc"/> that carry a voicemail claim
		/// (an overflow note starting "Voicemail recording ") and no call: voicemails whose request stopped or could not confirm its
		/// save (worker recovery). Oldest first.
		/// </summary>
		Task<IEnumerable<DispatcherQueueItem>> GetUnlinkedVoicemailClaimsAsync(DateTime createdSinceUtc);

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

		/// <summary>
		/// Inserts the message unless one with the same provider message id is stored (the id is unique in the table, so of two
		/// overlapping deliveries one wins). False when it was a duplicate.
		/// </summary>
		Task<bool> TryInsertAsync(DispatcherConversationMessage message, CancellationToken cancellationToken = default);
	}
}
