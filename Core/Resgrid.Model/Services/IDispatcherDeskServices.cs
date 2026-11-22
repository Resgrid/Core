using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	// Dispatcher desk (M0324, dispatchers-on-duty-plan.md): who dispatches on which shift, their breaks, their desk session in
	// the Dispatch app, and the queue of work routed to them.

	/// <summary>The department's <see cref="DispatcherDeskConfig"/> (setting 128).</summary>
	public interface IDispatcherDeskConfigService
	{
		/// <summary>The stored config, or the defaults (desk off) when there is none or it cannot be read.</summary>
		Task<DispatcherDeskConfig> GetConfigAsync(int departmentId, bool bypassCache = false);

		/// <summary>Normalizes and saves the config.</summary>
		Task<DispatcherDeskConfig> SaveConfigAsync(int departmentId, DispatcherDeskConfig config, string userId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The "Dispatchers on duty" part of shifts: which shifts take dispatchers, who dispatches on them, and when each
	/// dispatcher is on duty. Eligibility is the Dispatch app login permission (<see cref="IDispatchAccessService"/>).
	/// </summary>
	public interface IShiftDispatchersService
	{
		Task<ShiftDispatchSetting> GetSettingsForShiftAsync(int shiftId);

		Task<List<ShiftDispatchSetting>> GetSettingsForDepartmentAsync(int departmentId);

		/// <summary>Creates or updates the shift's dispatcher settings (the shift must belong to the department).</summary>
		Task<DispatcherDeskResult<ShiftDispatchSetting>> SaveSettingsAsync(int departmentId, ShiftDispatchSetting settings, string userId,
			CancellationToken cancellationToken = default);

		/// <summary>Every roster row of the shift (standing and per day, any status).</summary>
		Task<List<ShiftDispatcher>> GetRosterForShiftAsync(int shiftId);

		/// <summary>
		/// Replaces the shift's standing dispatch roster (rows with no day) with these people. Per-day rows (signups, one-day
		/// assignments, day exclusions) are kept. People not allowed to dispatch are refused.
		/// </summary>
		Task<DispatcherDeskResult<List<ShiftDispatcher>>> SetStandingRosterAsync(int departmentId, int shiftId,
			IEnumerable<(string UserId, bool IsSupervisor)> dispatchers, string byUserId, CancellationToken cancellationToken = default);

		/// <summary>A shift manager puts a dispatcher on one day (either shift type).</summary>
		Task<DispatcherDeskResult<ShiftDispatcher>> AssignToDayAsync(int departmentId, int shiftDayId, string userId, bool isSupervisor,
			string byUserId, CancellationToken cancellationToken = default);

		/// <summary>A dispatcher signs up for a day of a Signup shift (pending when the shift requires approval).</summary>
		Task<DispatcherDeskResult<ShiftDispatcher>> SignupForDayAsync(int departmentId, int shiftDayId, string userId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Takes a dispatcher off one day: their day row is denied, or, for someone on the standing roster, a denied row for that
		/// day keeps them off it. Their future breaks that day are cancelled.
		/// </summary>
		Task<DispatcherDeskResult<bool>> RemoveFromDayAsync(int departmentId, int shiftDayId, string userId, string byUserId, string note,
			CancellationToken cancellationToken = default);

		Task<DispatcherDeskResult<ShiftDispatcher>> ReviewSignupAsync(int departmentId, string shiftDispatcherId, bool approve, string byUserId,
			string note, CancellationToken cancellationToken = default);

		/// <summary>Dispatcher duty windows (approved and pending) on enabled shifts overlapping [fromUtc, toUtc).</summary>
		Task<List<DispatcherDutyWindow>> GetDutyWindowsAsync(int departmentId, DateTime fromUtc, DateTime toUtc);

		/// <summary>The dispatch roster of one shift day (approved and pending), with each dispatcher's window.</summary>
		Task<List<DispatcherDutyWindow>> GetDayRosterAsync(int departmentId, int shiftDayId);

		/// <summary>Dispatchers on duty at <paramref name="utcNow"/> (approved only), one window per dispatcher (the one ending last).</summary>
		Task<List<DispatcherDutyWindow>> GetOnDutyAsync(int departmentId, DateTime utcNow);

		/// <summary>The dispatcher's window covering <paramref name="utcNow"/>, else null.</summary>
		Task<DispatcherDutyWindow> GetDutyForUserAsync(int departmentId, string userId, DateTime utcNow);

		/// <summary>Whether the person may work the dispatch desk (Dispatch app login permission).</summary>
		Task<bool> IsEligibleDispatcherAsync(int departmentId, string userId);

		/// <summary>Removes every dispatcher row and setting of a deleted shift.</summary>
		Task DeleteForShiftAsync(int shiftId, CancellationToken cancellationToken = default);
	}

	/// <summary>Planned and taken dispatcher breaks, and desk coverage.</summary>
	public interface IDispatcherBreaksService
	{
		Task<List<DispatcherBreak>> GetBreaksAsync(int departmentId, DateTime fromUtc, DateTime toUtc);

		Task<List<DispatcherBreak>> GetBreaksForShiftDayAsync(int departmentId, int shiftDayId);

		/// <summary>The user's open (Scheduled or Active) breaks today and the active one first.</summary>
		Task<List<DispatcherBreak>> GetOpenBreaksForUserAsync(int departmentId, string userId, DateTime utcNow);

		Task<DispatcherBreak> GetBreakAsync(int departmentId, string dispatcherBreakId);

		/// <summary>
		/// Plans (or, with an id, re-plans) a break. Refuses overlapping the same dispatcher's own breaks; warns (never refuses)
		/// when other dispatchers' breaks overlap or coverage drops below the minimum.
		/// </summary>
		Task<DispatcherDeskResult<DispatcherBreak>> SaveBreakAsync(int departmentId, DispatcherBreak dispatcherBreak, string byUserId,
			CancellationToken cancellationToken = default);

		Task<DispatcherDeskResult<bool>> CancelBreakAsync(int departmentId, string dispatcherBreakId, string byUserId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Places the shift's (or department's) break plan for every dispatcher on the day, staggered so breaks do not overlap
		/// while keeping the minimum available. <paramref name="replace"/> cancels the day's Scheduled breaks first.
		/// </summary>
		Task<DispatcherDeskResult<List<DispatcherBreak>>> PlanBreaksAsync(int departmentId, int shiftDayId, bool replace, string byUserId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Starts a break: a planned one (<paramref name="dispatcherBreakId"/>) or one taken on the spot (type + minutes). The
		/// dispatcher is due back <c>DurationMinutes</c> after now, whatever the plan said. Work they hold goes back to the queue
		/// only when <paramref name="returnWork"/> is set (otherwise HoldingWork). A coverage drop below the minimum is a warning,
		/// or a refusal when the department prevents it and <paramref name="byUserId"/> is not a supervisor.
		/// </summary>
		Task<DispatcherDeskResult<DispatcherBreak>> StartBreakAsync(int departmentId, string userId, string dispatcherBreakId, int? breakType,
			int? durationMinutes, bool returnWork, string byUserId, CancellationToken cancellationToken = default);

		/// <summary>Ends the user's active break and makes them Available.</summary>
		Task<DispatcherDeskResult<DispatcherBreak>> EndBreakAsync(int departmentId, string userId, string byUserId, DispatcherBreakEndReasons reason,
			CancellationToken cancellationToken = default);

		/// <summary>Who is on duty and on break across the period, in slots, with coverage and overlap warnings.</summary>
		Task<DispatcherCoverage> GetCoverageAsync(int departmentId, DateTime fromUtc, DateTime toUtc, int slotMinutes = 5);

		/// <summary>Worker: reminders, due alerts, return alerts, auto return, overdue alerts, missed breaks.</summary>
		Task<int> ProcessBreakTimersAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>The dispatcher's desk session in the Dispatch app and the desk board.</summary>
	public interface IDispatcherPresenceService
	{
		/// <summary>Signs on to the desk (scheduled duty from the shift, or unscheduled where allowed) and makes the dispatcher Available.</summary>
		Task<DispatcherDeskResult<DispatcherDeskMember>> SignInAsync(int departmentId, string userId, DispatcherSignInRequest request,
			CancellationToken cancellationToken = default);

		/// <summary>Signs off: held and offered work goes back to the queue, an active break ends.</summary>
		Task<DispatcherDeskResult<bool>> SignOffAsync(int departmentId, string userId, string byUserId, CancellationToken cancellationToken = default);

		Task HeartbeatAsync(int departmentId, string userId, CancellationToken cancellationToken = default);

		Task<DispatcherDeskResult<DispatcherDeskMember>> SetAvailableAsync(int departmentId, string userId, string byUserId,
			CancellationToken cancellationToken = default);

		/// <summary>Stops routed work for now (offers in flight go back to the queue).</summary>
		Task<DispatcherDeskResult<DispatcherDeskMember>> SetAwayAsync(int departmentId, string userId, string reason, string byUserId,
			CancellationToken cancellationToken = default);

		Task<DispatcherDeskResult<DispatcherDeskMember>> SetCallbackNumberAsync(int departmentId, string userId, string callbackNumber,
			CancellationToken cancellationToken = default);

		/// <summary>The desk board: every on-duty or signed-in dispatcher with their state, break and work, and the open queue.</summary>
		Task<DispatcherDeskSnapshot> GetDeskAsync(int departmentId, DateTime utcNow);

		Task<DispatcherDeskMember> GetMemberAsync(int departmentId, string userId, DateTime utcNow);

		/// <summary>Dispatchers who can be offered work now, best first (least loaded, then longest since their last offer).</summary>
		Task<List<DispatcherDeskMember>> GetRoutableAsync(int departmentId, DateTime utcNow, IEnumerable<string> excludeUserIds = null);

		/// <summary>Desk supervisors on duty now, else the department admins (who to tell about overdue breaks and waiting work).</summary>
		Task<List<string>> GetSupervisorUserIdsAsync(int departmentId, DateTime utcNow);

		/// <summary>True while the user is on break and the department suppresses desk alerts on break.</summary>
		Task<bool> AreAlertsSuppressedAsync(int departmentId, string userId);

		/// <summary>Whether <paramref name="userId"/> may manage other dispatchers (desk supervisor on duty, shift manager or department admin).</summary>
		Task<bool> CanSuperviseAsync(int departmentId, string userId);

		/// <summary>Worker: signs dispatchers off at the end of their duty and marks stale sessions.</summary>
		Task<int> ProcessPresenceAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>The queue of work routed to dispatchers.</summary>
	public interface IDispatcherRoutingService
	{
		/// <summary>
		/// Puts work on the desk and offers it to the best available dispatcher. Returns NotRouted when the desk is off or does
		/// not take the kind (the caller then does what it did before), Duplicate when the call or provider message is already on
		/// the desk, Overflow for a live channel with nobody free (the caller runs the overflow handling).
		/// </summary>
		Task<DispatcherRouteResult> EnqueueAsync(DispatcherEnqueueRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// An inbound text or chat message: joins the sender's open conversation (within the join window) or starts a new one
		/// through <see cref="EnqueueAsync"/>.
		/// </summary>
		Task<DispatcherRouteResult> RouteInboundMessageAsync(DispatcherEnqueueRequest request, CancellationToken cancellationToken = default);

		/// <summary>Offers queued work to free dispatchers; returns the number of offers made.</summary>
		Task<int> RouteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Takes an item: one offered to the user, or one waiting in the shared queue.</summary>
		Task<DispatcherDeskResult<DispatcherQueueItem>> AcceptAsync(int departmentId, string dispatcherQueueItemId, string userId,
			CancellationToken cancellationToken = default);

		Task<DispatcherDeskResult<DispatcherQueueItem>> DeclineAsync(int departmentId, string dispatcherQueueItemId, string userId, string reason,
			CancellationToken cancellationToken = default);

		/// <summary>Puts a held item back in the queue (it is offered to someone else).</summary>
		Task<DispatcherDeskResult<DispatcherQueueItem>> ReleaseAsync(int departmentId, string dispatcherQueueItemId, string userId,
			CancellationToken cancellationToken = default);

		/// <summary>Hands an item to another on-duty dispatcher (as an offer they accept), by its holder or a supervisor.</summary>
		Task<DispatcherDeskResult<DispatcherQueueItem>> TransferAsync(int departmentId, string dispatcherQueueItemId, string userId, string toUserId,
			string note, CancellationToken cancellationToken = default);

		/// <summary>
		/// Finishes an item with how it ended. An intake text thread linked to it is closed too; <paramref name="notifyParticipant"/>
		/// tells the texter it ended (from the master number).
		/// </summary>
		Task<DispatcherDeskResult<DispatcherQueueItem>> CompleteAsync(int departmentId, string dispatcherQueueItemId, string userId,
			DispatcherQueueItemOutcomes outcome, int? callId, string note, bool notifyParticipant = true, CancellationToken cancellationToken = default);

		/// <summary>Links the call created from a conversation (the item stays held until completed).</summary>
		Task<DispatcherDeskResult<DispatcherQueueItem>> LinkCallAsync(int departmentId, string dispatcherQueueItemId, string userId, int callId,
			CancellationToken cancellationToken = default);

		Task<List<DispatcherQueueItem>> GetOpenItemsAsync(int departmentId);

		/// <summary>The item with its history, messages, call and (read for <paramref name="viewerUserId"/>) its intake text thread.</summary>
		Task<DispatcherQueueItemDetail> GetItemDetailAsync(int departmentId, string dispatcherQueueItemId, string viewerUserId = null);

		Task<DispatcherQueueItem> GetItemByExternalReferenceAsync(string externalReference);

		/// <summary>
		/// A call's pending or scheduled state changed elsewhere: items for a call that no longer waits are completed or cancelled. For a
		/// call that still waits, an offer out for it goes back to the queue when the call was screened meanwhile (M0326).
		/// </summary>
		Task OnCallChangedAsync(int departmentId, int callId, CancellationToken cancellationToken = default);

		// ---- intake text threads (IIntakeConversationRouter, dispatchers-on-duty-plan.md F2)

		/// <summary>
		/// A new intake text thread whose pending call intake already saved: queued and offered on the desk as a text item linked
		/// to the thread (or linked to the call's open item when the call reached the desk first). NotRouted when the desk is
		/// off or does not take texts.
		/// </summary>
		Task<DispatcherRouteResult> RouteIntakeConversationAsync(IntakeConversation conversation, Call call, CancellationToken cancellationToken = default);

		/// <summary>The texter wrote again: the item shows activity and its dispatcher hears about it.</summary>
		Task OnIntakeParticipantMessageAsync(IntakeConversation conversation, CancellationToken cancellationToken = default);

		/// <summary>A dispatcher connected to the thread (the desk's accept, or "Take conversation" on the call): the item is theirs.</summary>
		Task OnIntakeDispatcherConnectedAsync(IntakeConversation conversation, string userId, CancellationToken cancellationToken = default);

		/// <summary>The thread's dispatcher stepped away outside the desk: the item goes back in the queue.</summary>
		Task OnIntakeDispatcherReleasedAsync(IntakeConversation conversation, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The thread closed: an item whose call no longer waits is completed; one whose call is still pending stays as call work.
		/// </summary>
		Task OnIntakeConversationClosedAsync(IntakeConversation conversation, CancellationToken cancellationToken = default);

		/// <summary>Worker: pending calls not on the desk are added; scheduled calls within the lead time are added for review.</summary>
		Task<int> SyncCallsAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Worker: expired offers move on, live items with nobody free overflow, long waits are escalated.</summary>
		Task<int> ProcessTimeoutsAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// Records that a live item went to overflow handling, with a note for the history. An item that has a call (the overflow
		/// made a pending call, e.g. from a voicemail) keeps waiting in the queue as that call; one without a call is closed as
		/// Overflowed. Text and chat overflow is done by the routing service itself (it creates the pending call).
		/// </summary>
		Task MarkOverflowedAsync(DispatcherQueueItem item, string note, CancellationToken cancellationToken = default);

		/// <summary>
		/// Voice webhooks (DispatcherVoiceController): records VoiceConnecting / VoiceConnected / VoiceEnded on the item, touches
		/// its LastActivityOn and publishes the queue change. The item's state is not changed (an abandoned caller stays open for
		/// Call back). Convention: events about the held caller's own leg carry <paramref name="userId"/> null; events about a
		/// dispatcher's leg carry that dispatcher's id. "Caller waiting" = open voice item with no caller-leg VoiceEnded.
		/// </summary>
		Task RecordVoiceEventAsync(DispatcherQueueItem item, DispatcherQueueEventTypes eventType, string userId, string detail,
			CancellationToken cancellationToken = default);

		Task<DispatcherQueueStats> GetStatsAsync(int departmentId, DateTime fromUtc, DateTime toUtc);

		/// <summary>Worker: removes closed items older than the retention.</summary>
		Task<int> PurgeAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>Messages in routed text, email and chat conversations.</summary>
	public interface IDispatcherConversationService
	{
		Task<List<DispatcherConversationMessage>> GetMessagesAsync(int departmentId, string dispatcherQueueItemId);

		/// <summary>Records an inbound message on the item (idempotent on the provider message id).</summary>
		Task<DispatcherConversationMessage> RecordInboundAsync(DispatcherQueueItem item, string from, string to, string body, string providerMessageId,
			CancellationToken cancellationToken = default);

		/// <summary>Sends the dispatcher's reply on the item's channel (SMS from the department number; email; chat where supported).</summary>
		Task<DispatcherDeskResult<DispatcherConversationMessage>> SendReplyAsync(int departmentId, string dispatcherQueueItemId, string userId, string body,
			CancellationToken cancellationToken = default);

		/// <summary>A desk note on the item, never sent.</summary>
		Task<DispatcherDeskResult<DispatcherConversationMessage>> AddNoteAsync(int departmentId, string dispatcherQueueItemId, string userId, string body,
			CancellationToken cancellationToken = default);

		/// <summary>An automatic message to the sender (acknowledgement), recorded on the item.</summary>
		Task<DispatcherConversationMessage> SendSystemReplyAsync(DispatcherQueueItem item, string body, CancellationToken cancellationToken = default);
	}

	/// <summary>Connecting dispatchers to callers held on the desk (Twilio voice).</summary>
	public interface IDispatcherVoiceService
	{
		/// <summary>The desk queue name a held caller waits in.</summary>
		string QueueNameFor(DispatcherQueueItem item);

		/// <summary>Rings the dispatcher's phone and, when they confirm, bridges them to the caller held for the item.</summary>
		Task<DispatcherDeskResult<DispatcherQueueItem>> ConnectAsync(int departmentId, string dispatcherQueueItemId, string userId, string callbackNumber,
			CancellationToken cancellationToken = default);

		/// <summary>Rings the dispatcher's phone and then calls the item's caller back from the department number.</summary>
		Task<DispatcherDeskResult<DispatcherQueueItem>> CallBackAsync(int departmentId, string dispatcherQueueItemId, string userId, string callbackNumber,
			CancellationToken cancellationToken = default);

		/// <summary>The number to ring for the dispatcher: the one given, their desk callback number, else their profile mobile.</summary>
		Task<string> ResolveCallbackNumberAsync(int departmentId, string userId, string requested);

		/// <summary>Removes a held caller's queue once they have left it (Twilio's Enqueue action, any result). Best effort.</summary>
		Task ReleaseQueueAsync(string queueSid, CancellationToken cancellationToken = default);
	}

	/// <summary>Push notifications (Dispatch app only) and realtime desk events.</summary>
	public interface IDispatcherDeskNotifier
	{
		Task NotifyOfferAsync(DispatcherQueueItem item, string userId, CancellationToken cancellationToken = default);

		Task NotifyQueueEscalationAsync(DispatcherQueueItem item, IEnumerable<string> userIds, CancellationToken cancellationToken = default);

		Task NotifyMessageAsync(DispatcherQueueItem item, string userId, string preview, CancellationToken cancellationToken = default);

		/// <summary>Break coming up (<paramref name="due"/> false) or due now (true).</summary>
		Task NotifyBreakReminderAsync(DispatcherBreak dispatcherBreak, bool due, CancellationToken cancellationToken = default);

		/// <summary>Break over (to the dispatcher) or, with supervisors, the dispatcher is overdue / was auto-returned.</summary>
		Task NotifyBreakOverAsync(DispatcherBreak dispatcherBreak, IEnumerable<string> supervisorUserIds, bool autoReturned, CancellationToken cancellationToken = default);

		/// <summary>Desk board changed (presence, breaks, roster): EventingTypes 19.</summary>
		Task PublishDeskChangedAsync(int departmentId, string userId = null);

		/// <summary>Queue changed (item created, offered, taken, completed): EventingTypes 20.</summary>
		Task PublishQueueChangedAsync(int departmentId, string dispatcherQueueItemId);
	}

	/// <summary>Worker 82: one tick of the desk's timers.</summary>
	public interface IDispatcherDeskWorkerService
	{
		Task<int> TickAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}
}
