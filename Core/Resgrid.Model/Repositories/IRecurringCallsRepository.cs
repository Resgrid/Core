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
	}

	/// <summary>What dispatchers have to act on for recurring calls (RecurringCallAlerts, M0320).</summary>
	public interface IRecurringCallAlertsRepository : IRepository<RecurringCallAlert>
	{
		/// <summary>Inserts the alert unless the occurrence already has one of that kind; false when it did.</summary>
		Task<bool> TryRaiseAsync(RecurringCallAlert alert, CancellationToken cancellationToken = default);

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

		/// <summary>Records that the alert recipients were sent it.</summary>
		Task MarkNotifiedAsync(string recurringCallAlertId, DateTime notifiedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Links the call an alert is about once it exists.</summary>
		Task SetCallAsync(string recurringCallAlertId, int callId, CancellationToken cancellationToken = default);

		/// <summary>Deletes alerts closed before a time; returns how many.</summary>
		Task<int> PurgeResolvedBeforeAsync(DateTime resolvedBeforeUtc, CancellationToken cancellationToken = default);
	}
}
