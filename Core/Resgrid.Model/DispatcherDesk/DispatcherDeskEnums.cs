namespace Resgrid.Model
{
	// Dispatcher desk (M0324, dispatchers-on-duty-plan.md). Every enum here is persisted or sent over the wire: append only,
	// never renumber.

	/// <summary>How a dispatcher came to be on a shift's dispatch roster (<see cref="ShiftDispatcher.Source"/>).</summary>
	public enum ShiftDispatcherSources
	{
		/// <summary>Put on the roster by a shift manager. On an Assigned shift with no day it covers every day of the shift.</summary>
		Assigned = 1,

		/// <summary>Signed up themselves for one day of a Signup shift.</summary>
		Signup = 2
	}

	/// <summary>Whether a dispatch roster row counts (<see cref="ShiftDispatcher.Status"/>).</summary>
	public enum ShiftDispatcherStatuses
	{
		Active = 0,

		/// <summary>A signup on a shift that requires approval, not yet reviewed. Not on duty.</summary>
		PendingApproval = 1,

		/// <summary>Refused by a reviewer, or withdrawn. Not on duty; kept for the record.</summary>
		Denied = 2
	}

	/// <summary>The kind of break (<see cref="DispatcherBreak.BreakType"/>).</summary>
	public enum DispatcherBreakTypes
	{
		Break = 1,
		Meal = 2,
		Personal = 3,
		Other = 4
	}

	/// <summary>Where a break is in its life (<see cref="DispatcherBreak.State"/>).</summary>
	public enum DispatcherBreakStates
	{
		/// <summary>Planned, not started.</summary>
		Scheduled = 0,

		/// <summary>Started: the dispatcher is on break and gets no routed work and no desk alerts.</summary>
		Active = 1,

		/// <summary>Ended (by the dispatcher, a supervisor, auto return or signing off).</summary>
		Completed = 2,

		/// <summary>Never started before the dispatcher's duty ended.</summary>
		Missed = 3,

		/// <summary>Removed from the plan before it started.</summary>
		Cancelled = 4
	}

	/// <summary>Why a break ended (<see cref="DispatcherBreak.EndReason"/>).</summary>
	public enum DispatcherBreakEndReasons
	{
		None = 0,

		/// <summary>The dispatcher came back in the app.</summary>
		Returned = 1,

		/// <summary>The department's auto return: not back the configured minutes after the break's length ran out.</summary>
		AutoReturn = 2,

		/// <summary>A desk supervisor or shift manager ended it.</summary>
		Supervisor = 3,

		/// <summary>The dispatcher signed off the desk (or their duty ended) while on break.</summary>
		SignedOff = 4
	}

	/// <summary>The dispatcher's chosen desk state (<see cref="DispatcherPresence.State"/>). Busy is derived, never stored.</summary>
	public enum DispatcherPresenceStates
	{
		/// <summary>Not signed in to the desk; gets no routed work.</summary>
		Offline = 0,

		/// <summary>Signed in and taking work.</summary>
		Available = 1,

		/// <summary>Signed in but not taking routed work for now (manual, or after missing offers).</summary>
		Away = 2,

		/// <summary>On a break (<see cref="DispatcherPresence.ActiveBreakId"/>); no routed work and no desk alerts.</summary>
		OnBreak = 3
	}

	/// <summary>What the desk shows for a dispatcher: the stored state with Busy worked out from the work they hold.</summary>
	public enum DispatcherDisplayStates
	{
		Offline = 0,
		Available = 1,
		Busy = 2,
		Away = 3,
		OnBreak = 4
	}

	/// <summary>How the dispatcher is on duty (<see cref="DispatcherPresence.DutyMode"/>).</summary>
	public enum DispatcherDutyModes
	{
		/// <summary>On a shift's dispatch roster for the shift day running now.</summary>
		Scheduled = 0,

		/// <summary>Signed on without a shift (an event, a facility, cover), where the department allows it.</summary>
		Unscheduled = 1
	}

	/// <summary>What a routed piece of work is (<see cref="DispatcherQueueItem.Kind"/>).</summary>
	public enum DispatcherQueueItemKinds
	{
		/// <summary>A pending call (CallStates.Pending) waiting for a dispatcher.</summary>
		PendingCall = 1,

		/// <summary>A scheduled call coming up for review before it goes out by itself.</summary>
		ScheduledCall = 2,

		/// <summary>An inbound text conversation (Twilio / SignalWire SMS).</summary>
		Text = 3,

		/// <summary>An inbound phone call held for a dispatcher (Twilio voice).</summary>
		Voice = 4,

		/// <summary>An inbound email.</summary>
		Email = 5,

		/// <summary>An inbound chat platform conversation.</summary>
		Chat = 6,

		/// <summary>Anything else an integration routes to the desk.</summary>
		Other = 7
	}

	/// <summary>Where a routed piece of work is (<see cref="DispatcherQueueItem.State"/>).</summary>
	public enum DispatcherQueueItemStates
	{
		/// <summary>Waiting in the shared queue for a dispatcher.</summary>
		Queued = 0,

		/// <summary>Offered to one dispatcher (<see cref="DispatcherQueueItem.AssignedUserId"/>) until the offer expires.</summary>
		Offered = 1,

		/// <summary>Taken: the dispatcher is working it and counts as busy with it.</summary>
		Accepted = 2,

		/// <summary>Finished (<see cref="DispatcherQueueItem.Outcome"/>).</summary>
		Completed = 3,

		/// <summary>No dispatcher could take it, so it went to the department's overflow handling.</summary>
		Overflowed = 4,

		/// <summary>No longer needed (the call was dispatched or closed elsewhere, the caller hung up first).</summary>
		Cancelled = 5
	}

	/// <summary>How a dispatcher finished a piece of work (<see cref="DispatcherQueueItem.Outcome"/>).</summary>
	public enum DispatcherQueueItemOutcomes
	{
		None = 0,

		/// <summary>The call went out.</summary>
		Dispatched = 1,

		/// <summary>A call was created from the conversation.</summary>
		CallCreated = 2,

		/// <summary>Left as a pending call for later.</summary>
		LeftPending = 3,

		/// <summary>Already covered by another call.</summary>
		Duplicate = 4,

		/// <summary>Nothing to dispatch (information only, wrong number, test).</summary>
		NoActionNeeded = 5,

		/// <summary>The call was closed.</summary>
		Closed = 6,

		/// <summary>Handed to another agency or service.</summary>
		Referred = 7
	}

	/// <summary>One step in a routed item's history (<see cref="DispatcherQueueEvent.EventType"/>).</summary>
	public enum DispatcherQueueEventTypes
	{
		Created = 1,
		Offered = 2,
		Declined = 3,
		OfferExpired = 4,
		Accepted = 5,
		Released = 6,
		Transferred = 7,
		Completed = 8,
		Overflowed = 9,
		Cancelled = 10,
		MessageReceived = 11,
		MessageSent = 12,
		VoiceConnecting = 13,
		VoiceConnected = 14,
		VoiceEnded = 15,
		Escalated = 16,
		Reopened = 17,

		/// <summary>The intake text thread linked to the item closed (idle, STOP, the call closed, someone ended it).</summary>
		ConversationClosed = 18,

		/// <summary>
		/// The item's intake call was screened (Enhanced AI "likely not an incident", a repeat sender; M0326): it waits in the queue for a
		/// dispatcher to take and is no longer offered, and an offer out for it was withdrawn.
		/// </summary>
		HeldForReview = 19
	}

	/// <summary>Which way a conversation message went (<see cref="DispatcherConversationMessage.Direction"/>).</summary>
	public enum DispatcherMessageDirections
	{
		Inbound = 1,
		Outbound = 2,

		/// <summary>A desk note, never sent anywhere.</summary>
		Note = 3
	}

	/// <summary>Delivery of an outbound conversation message (<see cref="DispatcherConversationMessage.DeliveryState"/>).</summary>
	public enum DispatcherMessageDeliveryStates
	{
		Received = 0,
		Queued = 1,
		Sent = 2,
		Failed = 3
	}

	/// <summary>What happens to an inbound text or chat when no dispatcher can take it (<see cref="DispatcherDeskConfig.TextOverflowMode"/>).</summary>
	public enum DispatcherTextOverflowModes
	{
		/// <summary>It stays a pending call in the shared queue until a dispatcher is free.</summary>
		PendingQueue = 0,

		/// <summary>Enhanced AI fills in the pending call (type, priority, address) while it waits; needs the add-on.</summary>
		EnhancedAi = 1
	}

	/// <summary>What happens to an inbound phone call when no dispatcher can take it (<see cref="DispatcherDeskConfig.VoiceOverflowMode"/>).</summary>
	public enum DispatcherVoiceOverflowModes
	{
		/// <summary>Enhanced AI voice intake asks the caller the questions and creates a pending call; falls back to a message when unavailable.</summary>
		EnhancedAiIntake = 0,

		/// <summary>The caller leaves a message, which becomes a pending call with the recording.</summary>
		Voicemail = 1
	}

	/// <summary>
	/// What an emailed dispatch (call email templates) becomes (<see cref="DispatcherDeskConfig.EmailCallMode"/>). Applies to the
	/// department's dispatch email address whether or not the desk routes emails.
	/// </summary>
	public enum DispatcherEmailCallModes
	{
		/// <summary>The template's call is dispatched at once, as before this setting existed.</summary>
		AutoDispatch = 0,

		/// <summary>The template's call is saved as a pending call for a dispatcher to review and send.</summary>
		Pending = 1,

		/// <summary>Enhanced AI reads the email into a pending call (type, priority, address) for a dispatcher to send; needs the add-on.</summary>
		EnhancedAi = 2
	}

	/// <summary>Why the desk refused an action. Sent to clients by name; append only.</summary>
	public enum DispatcherDeskErrors
	{
		None = 0,
		DeskDisabled = 1,
		NotADispatcher = 2,
		NotOnDuty = 3,
		NotSignedIn = 4,
		ItemNotFound = 5,
		ItemNotAvailable = 6,
		NotAssignedToYou = 7,
		AtCapacity = 8,
		HoldingWork = 9,
		BreakNotFound = 10,
		BreakNotStartable = 11,
		AlreadyOnBreak = 12,
		NotOnBreak = 13,
		CoverageTooLow = 14,
		NotPermitted = 15,
		InvalidInput = 16,
		ChannelUnavailable = 17,
		NoCallbackNumber = 18,
		ShiftNotFound = 19,
		DispatchersNotEnabled = 20,
		AlreadyOnRoster = 21,
		BreakOverlaps = 22
	}
}
