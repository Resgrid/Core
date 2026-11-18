using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Recurring call schedules (RecurringCalls, M0319), and the reads of the calls they and Scheduled Calls show.</summary>
	public interface IRecurringCallsRepository : IRepository<RecurringCall>
	{
		/// <summary>Every department's running schedules (active, not deleted), for the worker.</summary>
		Task<IEnumerable<RecurringCall>> GetActiveAsync();

		/// <summary>Paused schedules (not deleted) that have a date to resume on, for the worker.</summary>
		Task<IEnumerable<RecurringCall>> GetPausedWithResumeDateAsync();

		/// <summary>A department's schedules, paused ones included, deleted ones not.</summary>
		Task<IEnumerable<RecurringCall>> GetForDepartmentAsync(int departmentId);

		/// <summary>
		/// The calls a calendar of scheduled calls shows between two UTC times (by <see cref="Call.DispatchOn"/>): every scheduled call
		/// still waiting to go out, and every call a recurring schedule created whether sent or not. Deleted calls are left out.
		/// </summary>
		Task<IEnumerable<Call>> GetScheduledCallsInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc);

		/// <summary>The not-deleted calls a schedule created that are due at or after <paramref name="fromUtc"/>.</summary>
		Task<IEnumerable<Call>> GetCallsForScheduleAsync(int departmentId, int recurringCallId, DateTime fromUtc);

		/// <summary>The not-deleted call a schedule created for an occurrence, logged at or after <paramref name="loggedSinceUtc"/> (claim recovery).</summary>
		Task<Call> GetCallForOccurrenceAsync(int recurringCallId, DateTime occurrenceUtc, DateTime loggedSinceUtc);
	}

	/// <summary>Occurrences of recurring schedules that were created, are being created, or were skipped (RecurringCallOccurrences, M0319).</summary>
	public interface IRecurringCallOccurrencesRepository : IRepository<RecurringCallOccurrence>
	{
		/// <summary>Inserts the row unless the schedule already has one for that time; false when it did (someone else has it).</summary>
		Task<bool> TryClaimAsync(RecurringCallOccurrence occurrence, CancellationToken cancellationToken = default);

		/// <summary>
		/// <see cref="TryClaimAsync"/> for the worker: also only while the stored schedule is active, not deleted and still the revision
		/// the worker read (<see cref="RecurringCall.EffectiveFrom"/> = <paramref name="scheduleRevisionUtc"/>, to the second). The schedule
		/// row is locked for the check, so a pause or change either lands first (nothing is claimed) or waits for the claim.
		/// </summary>
		Task<bool> TryClaimWhileRunningAsync(RecurringCallOccurrence occurrence, DateTime scheduleRevisionUtc, CancellationToken cancellationToken = default);

		Task<IEnumerable<RecurringCallOccurrence>> GetForScheduleInRangeAsync(int recurringCallId, DateTime fromUtc, DateTime toUtc);

		Task<IEnumerable<RecurringCallOccurrence>> GetForDepartmentInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc);

		Task<RecurringCallOccurrence> GetByOccurrenceAsync(int recurringCallId, DateTime occurrenceUtc);

		/// <summary>The occurrence a call was created for (its dispatch time may have been moved since).</summary>
		Task<RecurringCallOccurrence> GetByCallAsync(int recurringCallId, int callId);

		/// <summary>Claims (<see cref="RecurringCallOccurrenceStates.Creating"/>) taken before <paramref name="claimedBeforeUtc"/>: their process died.</summary>
		Task<IEnumerable<RecurringCallOccurrence>> GetStaleClaimsAsync(DateTime claimedBeforeUtc);

		/// <summary>Deletes one row, only while it is still in <paramref name="expectedState"/>; true when it was deleted.</summary>
		Task<bool> DeleteIfStateAsync(string recurringCallOccurrenceId, int expectedState, CancellationToken cancellationToken = default);

		/// <summary>
		/// Moves one row from <paramref name="fromState"/> to <paramref name="toState"/> (and stamps who and when), only while it is
		/// still in <paramref name="fromState"/>; true when this caller moved it. How a dispatcher takes over an occurrence that is
		/// awaiting them or was missed.
		/// </summary>
		Task<bool> TryTransitionStateAsync(string recurringCallOccurrenceId, int fromState, int toState, string userId, DateTime updatedOnUtc,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks a claim created with its call, only while it is still that claim (<see cref="RecurringCallOccurrenceStates.Creating"/>
		/// with <paramref name="claimedOnUtc"/> as its <see cref="RecurringCallOccurrence.UpdatedOn"/>); false when it is not (the
		/// stale-claim recovery dropped it or recorded the call, or the row was given back and claimed again by someone else).
		/// </summary>
		Task<bool> TryCompleteClaimAsync(string recurringCallOccurrenceId, int callId, DateTime claimedOnUtc, DateTime updatedOnUtc,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Renews a claim this caller still holds (same row, still creating, same <paramref name="claimedOnUtc"/>) to
		/// <paramref name="renewedOnUtc"/>, right before its call is saved, so the stale-claim recovery cannot drop it while the save
		/// runs. With <paramref name="scheduleRevisionUtc"/>, also only while the schedule runs as that revision (as
		/// <see cref="TryClaimWhileRunningAsync"/>). False: the claim is not this caller's any more, or the schedule stopped or changed.
		/// </summary>
		Task<bool> TryRenewClaimAsync(string recurringCallOccurrenceId, DateTime claimedOnUtc, DateTime renewedOnUtc, DateTime? scheduleRevisionUtc,
			CancellationToken cancellationToken = default);

		/// <summary>Drops a claim, only while it is still this caller's (creating, with <paramref name="claimedOnUtc"/>).</summary>
		Task<bool> DeleteClaimAsync(string recurringCallOccurrenceId, DateTime claimedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Puts a claim a dispatcher took over back to <paramref name="toState"/>, only while it is still theirs (creating, with
		/// <paramref name="claimedOnUtc"/>).
		/// </summary>
		Task<bool> TryReturnClaimAsync(string recurringCallOccurrenceId, DateTime claimedOnUtc, int toState, string userId, DateTime updatedOnUtc,
			CancellationToken cancellationToken = default);
	}

	/// <summary>What dispatchers have to act on for recurring calls (RecurringCallAlerts, M0320).</summary>
	public interface IRecurringCallAlertsRepository : IRepository<RecurringCallAlert>
	{
		/// <summary>
		/// Inserts the alert unless the occurrence already has an open one of that kind; false when it did. A closed one does not
		/// count, so an occurrence restored after its alert was closed can be alerted again.
		/// </summary>
		Task<bool> TryRaiseAsync(RecurringCallAlert alert, CancellationToken cancellationToken = default);

		/// <summary>Whether the occurrence has ever had an alert of that kind, open or closed.</summary>
		Task<bool> AnyForOccurrenceAsync(int recurringCallId, DateTime occurrenceUtc, int kind);

		/// <summary>A department's open alerts.</summary>
		Task<IEnumerable<RecurringCallAlert>> GetOpenForDepartmentAsync(int departmentId);

		/// <summary>A department's open alerts and those raised since a time (the recent history).</summary>
		Task<IEnumerable<RecurringCallAlert>> GetForDepartmentSinceAsync(int departmentId, DateTime sinceUtc);

		/// <summary>Every department's open alerts, for the worker's pass (they are few).</summary>
		Task<IEnumerable<RecurringCallAlert>> GetAllOpenAsync();

		/// <summary>The open alerts about one occurrence.</summary>
		Task<IEnumerable<RecurringCallAlert>> GetOpenForOccurrenceAsync(int recurringCallId, DateTime occurrenceUtc);

		/// <summary>The open alerts of one schedule.</summary>
		Task<IEnumerable<RecurringCallAlert>> GetOpenForScheduleAsync(int recurringCallId);

		/// <summary>Closes an alert that is still open; false when it was already closed (or does not exist).</summary>
		Task<bool> TryResolveAsync(string recurringCallAlertId, int resolution, string userId, string note, DateTime resolvedOnUtc,
			CancellationToken cancellationToken = default);

		/// <summary>Marks an open, unacknowledged alert seen; false when it was closed or already acknowledged.</summary>
		Task<bool> TryAcknowledgeAsync(string recurringCallAlertId, string userId, DateTime acknowledgedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Claims sending an alert to its recipients: only while it is still open and nobody has sent it or is sending it
		/// (<see cref="RecurringCallAlert.NotifiedOn"/> is null; the claim sets it to <paramref name="claimedOnUtc"/>). False: closed
		/// meanwhile, or someone else has it.
		/// </summary>
		Task<bool> TryClaimNotificationAsync(string recurringCallAlertId, DateTime claimedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Gives back a notification claim nobody was reached under (only while it is still that claim), so a later pass retries it.</summary>
		Task<bool> ReleaseNotificationClaimAsync(string recurringCallAlertId, DateTime claimedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Records that the alert recipients were sent it.</summary>
		Task MarkNotifiedAsync(string recurringCallAlertId, DateTime notifiedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Links the call an alert is about once it exists.</summary>
		Task SetCallAsync(string recurringCallAlertId, int callId, CancellationToken cancellationToken = default);

		/// <summary>Deletes alerts closed before a time; returns how many.</summary>
		Task<int> PurgeResolvedBeforeAsync(DateTime resolvedBeforeUtc, CancellationToken cancellationToken = default);
	}
}
