using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Recurring calls (M0319, M0320): schedules that create an ordinary scheduled call for each occurrence ahead of time (or, in
	/// dispatcher mode, alert a dispatcher to send it), the alerts dispatchers act on, and the calendar of scheduled calls. The
	/// scheduled-calls worker (12) calls <see cref="GenerateDueCallsAsync"/> every pass and sends recurring calls through
	/// <see cref="DispatchCallAsync"/>, which checks the chosen resources are available and holds the call when nobody is.
	///
	/// Every change is audited (<see cref="AuditLogTypes"/> RecurringCall*) and raises the recurring call workflow triggers
	/// (<see cref="WorkflowTriggerEventType"/> 208-217).
	/// </summary>
	public interface IRecurringCallsService
	{
		/// <summary>Every schedule of the department, whoever asks (the worker, ownership checks). Lists shown to a member use <see cref="GetAllVisibleToUserAsync"/>.</summary>
		Task<List<RecurringCall>> GetAllForDepartmentAsync(int departmentId);

		/// <summary>The department's schedules in the member's dispatch scope (the calls they create would be), as the calendar and alerts show them.</summary>
		Task<List<RecurringCall>> GetAllVisibleToUserAsync(int departmentId, string userId);

		/// <summary>Whether a schedule is in the member's dispatch scope (see <see cref="GetAllVisibleToUserAsync"/>).</summary>
		Task<bool> IsVisibleToUserAsync(RecurringCall series, string userId);

		/// <summary>The department's schedule, or null (not found, another department's, or deleted).</summary>
		Task<RecurringCall> GetByIdAsync(int departmentId, int recurringCallId);

		/// <summary>
		/// A chosen pickup facility point of interest fills the facility name and the address (with its pin) when they are blank, and
		/// is cleared when it is not one of the department's. <see cref="SaveAsync"/> does it too; callers validating the schedule's
		/// call fields first apply it so a facility's address counts.
		/// </summary>
		Task ApplyFacilityDefaultsAsync(RecurringCall series);

		/// <summary>
		/// Creates or updates a schedule (already validated with <see cref="RecurringCallRecurrence.Validate"/>), locating its
		/// address when it has no pin, then creates the calls now due within its lead time. With
		/// <paramref name="replaceUpcomingCalls"/>, calls it already created that are scheduled and have not gone out are deleted and
		/// created again from the saved schedule (dropping any edits made to them); without it they are left as they are. A held
		/// (pending) call is never replaced: it has an open alert and is the dispatcher's to send or close.
		/// </summary>
		Task<RecurringCallSaveResult> SaveAsync(RecurringCall series, string userId, bool replaceUpcomingCalls, AuditOrigin origin = null,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Pauses a schedule without deleting it: nothing is created or alerted while paused. <paramref name="resumeOn"/> (a date in
		/// the department's time zone) resumes it automatically at the start of that day. With <paramref name="deleteUpcomingCalls"/>
		/// the scheduled calls it already created that have not gone out are deleted too (held calls stay). Null when not found.
		/// </summary>
		Task<RecurringCall> PauseAsync(int departmentId, int recurringCallId, string userId, string reason, DateTime? resumeOn, bool deleteUpcomingCalls,
			AuditOrigin origin = null, CancellationToken cancellationToken = default);

		/// <summary>Resumes a paused schedule. Resuming never creates an occurrence that was due before it. Null when not found.</summary>
		Task<RecurringCall> ResumeAsync(int departmentId, int recurringCallId, string userId, AuditOrigin origin = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a schedule; with <paramref name="deleteUpcomingCalls"/> also the scheduled calls it created that have not gone
		/// out (held calls stay for the dispatcher).
		/// </summary>
		Task<bool> DeleteAsync(int departmentId, int recurringCallId, string userId, bool deleteUpcomingCalls, AuditOrigin origin = null,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// The calls a schedule created that have not gone out yet: still waiting for their dispatch time (the ones a change can
		/// replace or delete), or held as pending (left for the dispatcher). With <paramref name="userId"/>, only those in that
		/// member's dispatch scope.
		/// </summary>
		Task<List<Call>> GetUpcomingCallsAsync(int departmentId, int recurringCallId, string userId = null);

		/// <summary>
		/// Skips an occurrence that has no call yet (still to come, awaiting a dispatcher, or missed). False when it is not an
		/// occurrence of the schedule or already has a call.
		/// </summary>
		Task<bool> SkipOccurrenceAsync(int departmentId, int recurringCallId, DateTime occurrenceUtc, string userId, AuditOrigin origin = null,
			CancellationToken cancellationToken = default);

		/// <summary>Undoes <see cref="SkipOccurrenceAsync"/>. A restored occurrence due within the lead time is created straight away.</summary>
		Task<bool> RestoreOccurrenceAsync(int departmentId, int recurringCallId, DateTime occurrenceUtc, string userId, AuditOrigin origin = null,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Creates an occurrence's call now without sending it, so it can be changed on its own (another patient, another crew). A
		/// future occurrence becomes a scheduled call for its time; one that is due, awaiting a dispatcher or missed becomes a pending
		/// call for a dispatcher to send. Returns the call, the one already created for it, or null when it cannot be created.
		/// </summary>
		Task<Call> CreateOccurrenceCallNowAsync(int departmentId, int recurringCallId, DateTime occurrenceUtc, string userId, AuditOrigin origin = null,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// A dispatcher sends an occurrence now: creates its call if it has none (still to come, awaiting a dispatcher, missed, or
		/// skipped) and dispatches it at once, with the same run card and availability rules as the worker. A call created for it that
		/// has not gone out is sent instead.
		/// </summary>
		Task<RecurringCallDispatchResult> DispatchOccurrenceNowAsync(int departmentId, int recurringCallId, DateTime occurrenceUtc, string userId,
			AuditOrigin origin = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Sends a call a recurring schedule created whose time has come (the worker), or that a dispatcher asked for. Takes the
		/// call's dispatch claim, adds the matching run card's resources (always when the schedule asks for it, otherwise only when the
		/// run card auto-dispatches), leaves off chosen units and members that are not available, and broadcasts it. When nobody is
		/// left the call is held as pending and the dispatcher alerted; when some were left off it goes out and the dispatcher is told.
		/// </summary>
		Task<RecurringCallDispatchResult> DispatchCallAsync(Call call, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// A call a schedule created went out some other way than <see cref="DispatchCallAsync"/> (a dispatcher's Dispatch Now on the
		/// held or pending call): closes the alerts about its occurrence and raises the occurrence's Dispatched trigger, once. Does
		/// nothing for a call no schedule created; never throws.
		/// </summary>
		Task OnCallDispatchedAsync(Call call, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The worker pass: resumes schedules whose resume date has come, creates the calls due within each running schedule's lead
		/// time (dispatcher mode: alerts the dispatcher instead), raises missed-call alerts, closes alerts that are settled and
		/// resolves abandoned claims. Returns how many calls were created.
		/// </summary>
		Task<int> GenerateDueCallsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// The calendar of scheduled calls between two UTC times as a member sees it: scheduled calls waiting to go out, calls the
		/// recurring schedules created, and the occurrences still to come (projected), skipped, awaiting a dispatcher or missed.
		/// Limited to the member's dispatch scope like the Scheduled Calls list.
		/// </summary>
		Task<List<ScheduledCallCalendarEntry>> GetCalendarAsync(int departmentId, string userId, DateTime fromUtc, DateTime toUtc);

		/// <summary>
		/// The recurring call alerts a member can see (schedules in their dispatch scope): every open one, plus, with
		/// <paramref name="includeClosedSinceUtc"/>, those raised since then. Alerts that are settled are closed first.
		/// </summary>
		Task<List<RecurringCallAlertInfo>> GetAlertsAsync(int departmentId, string userId, DateTime? includeClosedSinceUtc = null,
			CancellationToken cancellationToken = default);

		/// <summary>Marks an open alert seen. False when not found, another department's, or already closed.</summary>
		Task<bool> AcknowledgeAlertAsync(int departmentId, string recurringCallAlertId, string userId, AuditOrigin origin = null,
			CancellationToken cancellationToken = default);

		/// <summary>Closes an open alert by hand. False when not found, another department's, or already closed.</summary>
		Task<bool> DismissAlertAsync(int departmentId, string recurringCallAlertId, string userId, string note, AuditOrigin origin = null,
			CancellationToken cancellationToken = default);
	}

	public class RecurringCallSaveResult
	{
		public RecurringCall Series { get; set; }

		/// <summary>How many calls already created were deleted to be created again from the saved schedule.</summary>
		public int ReplacedCalls { get; set; }

		/// <summary>How many calls were created because they are due within the lead time.</summary>
		public int CreatedCalls { get; set; }
	}

	/// <summary>How sending a recurring call went.</summary>
	public enum RecurringCallDispatchOutcomes
	{
		/// <summary>It went out.</summary>
		Dispatched = 1,

		/// <summary>Nobody could be sent: held as a pending call and the dispatcher alerted.</summary>
		Held = 2,

		/// <summary>Not waiting to go out (sent already, closed, or someone else is sending it).</summary>
		NotWaiting = 3,

		/// <summary>
		/// The broadcast could not be queued and nothing went out. The call is waiting again as it was: a scheduled call is retried
		/// by the worker's next passes (a dispatcher's send-now call is scheduled for now), a held call stays pending.
		/// </summary>
		QueueFailed = 4,

		/// <summary>The occurrence is not one of the schedule's, or its call could not be created.</summary>
		NotCreated = 5
	}

	public class RecurringCallDispatchResult
	{
		public RecurringCallDispatchOutcomes Outcome { get; set; }

		public Call Call { get; set; }

		/// <summary>Chosen units left off because they were not available.</summary>
		public List<int> UnavailableUnitIds { get; set; } = new List<int>();

		/// <summary>Chosen members left off because they were not available.</summary>
		public List<string> UnavailableUserIds { get; set; } = new List<string>();

		/// <summary>Whether a run card added resources.</summary>
		public bool RunCardApplied { get; set; }

		public static RecurringCallDispatchResult Of(RecurringCallDispatchOutcomes outcome, Call call = null)
		{
			return new RecurringCallDispatchResult { Outcome = outcome, Call = call };
		}
	}

	/// <summary>An alert with the names a dispatcher needs to act on it.</summary>
	public class RecurringCallAlertInfo
	{
		public RecurringCallAlert Alert { get; set; }

		public string ScheduleName { get; set; }

		/// <summary>Whether the schedule is in dispatcher mode.</summary>
		public bool DispatcherMode { get; set; }

		/// <summary>The schedule was deleted since.</summary>
		public bool ScheduleDeleted { get; set; }

		/// <summary>
		/// The alert's time is still an occurrence of the schedule as it is now (false once the schedule's times or days changed):
		/// only then can the occurrence be sent, created or skipped from the alert.
		/// </summary>
		public bool OccurrenceScheduled { get; set; }

		/// <summary>
		/// The occurrence can be sent, created or skipped from the alert: it is open, has no call yet, is about an occurrence with
		/// nothing done (due for a dispatcher, missed, or its call not created) and the schedule still has that occurrence.
		/// </summary>
		public bool CanSend => Alert != null && Alert.IsOpen && !Alert.CallId.HasValue && !ScheduleDeleted && OccurrenceScheduled &&
							   (Alert.Kind == (int)RecurringCallAlertKinds.DispatchDue || Alert.Kind == (int)RecurringCallAlertKinds.Missed ||
								Alert.Kind == (int)RecurringCallAlertKinds.CreateFailed);

		public string CallNumber { get; set; }

		/// <summary>The call's <see cref="CallStates"/> value, when there is a call.</summary>
		public int? CallState { get; set; }

		/// <summary>Whether the call concerned has gone out.</summary>
		public bool CallDispatched { get; set; }

		public string Reason { get; set; }

		/// <summary>Unit id and name of each unavailable unit.</summary>
		public List<KeyValuePair<int, string>> UnavailableUnits { get; set; } = new List<KeyValuePair<int, string>>();

		/// <summary>User id and name of each unavailable member.</summary>
		public List<KeyValuePair<string, string>> UnavailablePeople { get; set; } = new List<KeyValuePair<string, string>>();

		public string AcknowledgedByName { get; set; }

		public string ResolvedByName { get; set; }
	}

	public enum ScheduledCallCalendarEntryKinds
	{
		/// <summary>A scheduled call waiting for its dispatch time.</summary>
		Scheduled = 1,

		/// <summary>A call a recurring schedule created that has gone out.</summary>
		Dispatched = 2,

		/// <summary>A call a recurring schedule created that was closed before it went out.</summary>
		Cancelled = 3,

		/// <summary>An occurrence of a recurring schedule whose call is not created yet.</summary>
		Projected = 4,

		/// <summary>An occurrence a dispatcher skipped.</summary>
		Skipped = 5,

		/// <summary>Dispatcher mode: an occurrence that is due and waiting for a dispatcher to send it.</summary>
		Due = 6,

		/// <summary>An occurrence whose time passed with no call created.</summary>
		Missed = 7,

		/// <summary>A call a recurring schedule created that is held as pending (nobody could be sent).</summary>
		Held = 8
	}

	/// <summary>One entry on the scheduled calls calendar.</summary>
	public class ScheduledCallCalendarEntry
	{
		public ScheduledCallCalendarEntryKinds Kind { get; set; }

		/// <summary>When it goes (or went) out, UTC.</summary>
		public DateTime DispatchOnUtc { get; set; }

		public string Name { get; set; }

		/// <summary>As <see cref="Call.Priority"/>.</summary>
		public int Priority { get; set; }

		public string Address { get; set; }

		/// <summary>The call, for every kind except <see cref="ScheduledCallCalendarEntryKinds.Projected"/>, Skipped, Due and Missed.</summary>
		public int? CallId { get; set; }

		public string CallNumber { get; set; }

		/// <summary>The recurring schedule it belongs to, if any.</summary>
		public int? RecurringCallId { get; set; }

		/// <summary>The occurrence it stands for (UTC), for entries of a recurring schedule; differs from DispatchOnUtc when a dispatcher sent it early or late.</summary>
		public DateTime? OccurrenceOnUtc { get; set; }
	}
}
