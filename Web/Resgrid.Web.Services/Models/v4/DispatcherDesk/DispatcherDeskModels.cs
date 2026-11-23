using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.DispatcherDesk
{
	// Dispatcher desk (v4, M0324, dispatchers-on-duty-plan.md). Instants are ISO-8601 UTC ("2026-11-19T16:00:00Z"); local dates
	// are "yyyy-MM-dd". Enums are sent as their integer values (Resgrid.Model DispatcherDesk enums); refusals carry the
	// DispatcherDeskErrors name in Error. Every action result carries the caller's refreshed desk member so the app can redraw
	// its status bar without another round trip.

	/// <summary>A refusal or success with the desk state after the action.</summary>
	public class DispatcherDeskActionResult : StandardApiResponseV4Base
	{
		public bool Success { get; set; }

		/// <summary>DispatcherDeskErrors name when refused ("HoldingWork", "CoverageTooLow", …); empty on success.</summary>
		public string Error { get; set; }

		public List<DispatcherDeskWarningData> Warnings { get; set; } = new List<DispatcherDeskWarningData>();

		/// <summary>The caller's desk member after the action.</summary>
		public DispatcherDeskMemberData Me { get; set; }

		/// <summary>The item acted on (item actions).</summary>
		public DispatcherQueueItemData Item { get; set; }

		/// <summary>The break acted on (break actions).</summary>
		public DispatcherBreakData Break { get; set; }

		/// <summary>The message sent (message actions).</summary>
		public DispatcherConversationMessageData Message { get; set; }
	}

	public class DispatcherDeskWarningData
	{
		/// <summary>DispatcherDeskWarningKinds name.</summary>
		public string Kind { get; set; }
		public string StartUtc { get; set; }
		public string EndUtc { get; set; }
		public List<string> UserIds { get; set; } = new List<string>();
		public int? Available { get; set; }
		public int? Minimum { get; set; }
		public int? Minutes { get; set; }
	}

	/// <summary>The department's desk settings the app needs (a subset of DispatcherDeskConfig).</summary>
	public class DispatcherDeskConfigData
	{
		public bool Enabled { get; set; }
		public bool RequireShift { get; set; }
		public bool AllowUnscheduledDuty { get; set; }
		public int MaxActiveItemsPerDispatcher { get; set; }
		public int OfferTimeoutSeconds { get; set; }
		public int BreakReminderMinutes { get; set; }
		public int AutoReturnAfterMinutes { get; set; }
		public int MinimumAvailableDispatchers { get; set; }
		public bool PreventBreakBelowMinimum { get; set; }
		public bool SuppressAlertsOnBreak { get; set; }
		public bool RouteText { get; set; }
		public bool RouteVoice { get; set; }
		public bool RouteEmail { get; set; }
		public bool RouteChat { get; set; }
		public bool RoutePendingCalls { get; set; }
		public bool RouteScheduledCalls { get; set; }
		/// <summary>DispatcherEmailCallModes: 0 AutoDispatch, 1 Pending, 2 EnhancedAi.</summary>
		public int EmailCallMode { get; set; }
		/// <summary>The default break plan (shown when planning a break): type, minutes.</summary>
		public List<DispatcherBreakPlanEntryData> DefaultBreakPlan { get; set; } = new List<DispatcherBreakPlanEntryData>();
	}

	public class DispatcherBreakPlanEntryData
	{
		/// <summary>DispatcherBreakTypes: 1 Break, 2 Meal, 3 Personal, 4 Other.</summary>
		public int BreakType { get; set; }
		public int DurationMinutes { get; set; }
		/// <summary>Minutes after the shift day starts; null = spread evenly.</summary>
		public int? TargetOffsetMinutes { get; set; }
	}

	public class DispatcherDutyData
	{
		public string UserId { get; set; }
		public int ShiftId { get; set; }
		public int ShiftDayId { get; set; }
		public string ShiftName { get; set; }
		public string ShiftColor { get; set; }
		/// <summary>The shift day's local date, "yyyy-MM-dd".</summary>
		public string Day { get; set; }
		public string StartUtc { get; set; }
		public string EndUtc { get; set; }
		public bool IsSupervisor { get; set; }
		/// <summary>1 Assigned, 2 Signup.</summary>
		public int Source { get; set; }
		public bool IsPending { get; set; }
		public string ShiftDispatcherId { get; set; }
	}

	public class DispatcherBreakData
	{
		public string DispatcherBreakId { get; set; }
		public string UserId { get; set; }
		public string UserName { get; set; }
		public int? ShiftId { get; set; }
		public int? ShiftDayId { get; set; }
		/// <summary>DispatcherBreakTypes.</summary>
		public int BreakType { get; set; }
		/// <summary>DispatcherBreakStates: 0 Scheduled, 1 Active, 2 Completed, 3 Missed, 4 Cancelled.</summary>
		public int State { get; set; }
		public string ScheduledStartOn { get; set; }
		/// <summary>ScheduledStartOn + DurationMinutes.</summary>
		public string ScheduledEndOn { get; set; }
		public int DurationMinutes { get; set; }
		public string StartedOn { get; set; }
		/// <summary>StartedOn + DurationMinutes: when the dispatcher is due back.</summary>
		public string ExpectedReturnOn { get; set; }
		public string EndedOn { get; set; }
		/// <summary>DispatcherBreakEndReasons.</summary>
		public int EndReason { get; set; }
		public string Note { get; set; }
	}

	public class DispatcherDeskMemberData
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		/// <summary>DispatcherDisplayStates: 0 Offline, 1 Available, 2 Busy, 3 Away, 4 OnBreak.</summary>
		public int DisplayState { get; set; }
		public bool IsSignedIn { get; set; }
		public bool IsOnDuty { get; set; }
		public bool IsSupervisor { get; set; }
		/// <summary>0 Scheduled (on a shift), 1 Unscheduled.</summary>
		public int DutyMode { get; set; }
		public DispatcherDutyData Duty { get; set; }
		/// <summary>When the duty ends (shift day end, or the chosen end of unscheduled duty); null = open.</summary>
		public string DutyEndsOn { get; set; }
		public string SignedInOn { get; set; }
		public string StateChangedOn { get; set; }
		public string LastSeenOn { get; set; }
		public string AwayReason { get; set; }
		public string CallbackNumber { get; set; }
		/// <summary>phone, tablet or desktop.</summary>
		public string DeviceKind { get; set; }
		public DispatcherBreakData ActiveBreak { get; set; }
		public DispatcherBreakData NextBreak { get; set; }
		public int ActiveItemCount { get; set; }
		public int OfferedItemCount { get; set; }
		public int Capacity { get; set; }
		public int OverdueMinutes { get; set; }
		/// <summary>The caller may supervise other dispatchers (end their breaks, reassign their work).</summary>
		public bool CanSupervise { get; set; }
	}

	public class DispatcherQueueItemData
	{
		public string DispatcherQueueItemId { get; set; }
		/// <summary>DispatcherQueueItemKinds: 1 PendingCall, 2 ScheduledCall, 3 Text, 4 Voice, 5 Email, 6 Chat, 7 Other.</summary>
		public int Kind { get; set; }
		/// <summary>DispatcherQueueItemStates: 0 Queued, 1 Offered, 2 Accepted, 3 Completed, 4 Overflowed, 5 Cancelled.</summary>
		public int State { get; set; }
		/// <summary>DispatcherQueueItemOutcomes.</summary>
		public int Outcome { get; set; }
		public int? CallId { get; set; }
		public string CallNumber { get; set; }
		public string CallName { get; set; }
		public int? CallPriority { get; set; }
		public string CallPriorityColor { get; set; }
		public int Priority { get; set; }
		public string SourceAddress { get; set; }
		public string SourceName { get; set; }
		public string TargetAddress { get; set; }
		public string Subject { get; set; }
		public string Preview { get; set; }
		public string AssignedUserId { get; set; }
		public string AssignedUserName { get; set; }
		public string OfferedOn { get; set; }
		public string OfferExpiresOn { get; set; }
		public int OfferAttempts { get; set; }
		public string AcceptedOn { get; set; }
		public string CompletedOn { get; set; }
		public string DueOn { get; set; }
		public string CreatedOn { get; set; }
		public string LastActivityOn { get; set; }
		public string OverflowedOn { get; set; }
		public string OverflowNote { get; set; }
		public string EscalatedOn { get; set; }
		public string Note { get; set; }
		/// <summary>Voice: a held caller is waiting now (not hung up).</summary>
		public bool CallerWaiting { get; set; }
		/// <summary>Messages in the conversation (text, email, chat).</summary>
		public int MessageCount { get; set; }
		/// <summary>Messages received since the holder last replied.</summary>
		public int UnreadCount { get; set; }
		/// <summary>
		/// An intake text thread the item is linked to (a text to the department's private inbound dispatch number): its messages are read from
		/// <see cref="DispatcherQueueItemDetailData.IntakeConversation"/>, replies go out from the region's master number.
		/// </summary>
		public string IntakeConversationId { get; set; }
	}

	public class DispatcherQueueEventData
	{
		/// <summary>DispatcherQueueEventTypes.</summary>
		public int EventType { get; set; }
		public string UserId { get; set; }
		public string UserName { get; set; }
		public string OccurredOn { get; set; }
		public string Detail { get; set; }
	}

	public class DispatcherConversationMessageData
	{
		public string DispatcherConversationMessageId { get; set; }
		/// <summary>DispatcherMessageDirections: 1 Inbound, 2 Outbound, 3 Note.</summary>
		public int Direction { get; set; }
		public string FromAddress { get; set; }
		public string ToAddress { get; set; }
		public string Body { get; set; }
		public string UserId { get; set; }
		public string UserName { get; set; }
		public string CreatedOn { get; set; }
		/// <summary>DispatcherMessageDeliveryStates: 0 Received, 1 Queued, 2 Sent, 3 Failed.</summary>
		public int DeliveryState { get; set; }
		public string Error { get; set; }
	}

	public class DispatcherQueueItemDetailData
	{
		public DispatcherQueueItemData Item { get; set; }
		public List<DispatcherQueueEventData> Events { get; set; } = new List<DispatcherQueueEventData>();
		public List<DispatcherConversationMessageData> Messages { get; set; } = new List<DispatcherConversationMessageData>();
		/// <summary>Whether a reply can be sent on the item's channel.</summary>
		public bool CanReply { get; set; }
		/// <summary>Whether the desk can ring the dispatcher's phone for this item (voice configured).</summary>
		public bool CanConnectVoice { get; set; }
		/// <summary>The intake text thread linked to the item, with its messages (null for other items).</summary>
		public DispatcherIntakeThreadData IntakeConversation { get; set; }
	}

	/// <summary>An intake text thread on a desk item (IntakeConversation, M0325).</summary>
	public class DispatcherIntakeThreadData
	{
		public string IntakeConversationId { get; set; }
		/// <summary>IntakeConversationStates: 1 Open, 2 Closed.</summary>
		public int State { get; set; }
		/// <summary>IntakeConversationHandlers: 1 Assistant (Enhanced AI asks), 2 Unassisted, 3 Dispatcher.</summary>
		public int Handler { get; set; }
		public string AssignedUserId { get; set; }
		public string AssignedUserName { get; set; }
		/// <summary>The texter sent STOP: nothing more can be sent.</summary>
		public bool OptedOut { get; set; }
		public string ParticipantName { get; set; }
		/// <summary>The number replies go out from (the region's master number).</summary>
		public string ReplyFromAddress { get; set; }
		public string StartedOn { get; set; }
		public string ClosedOn { get; set; }
		public string CloseReason { get; set; }
		/// <summary>Oldest first.</summary>
		public List<DispatcherIntakeMessageData> Messages { get; set; } = new List<DispatcherIntakeMessageData>();
	}

	public class DispatcherIntakeMessageData
	{
		public string MessageId { get; set; }
		/// <summary>IntakeConversationMessageDirections: 1 Inbound, 2 Outbound.</summary>
		public int Direction { get; set; }
		/// <summary>IntakeConversationAuthors: 1 Participant, 2 Assistant (Enhanced AI), 3 Dispatcher, 4 System (welcome, connected, closed).</summary>
		public int Author { get; set; }
		public string AuthorUserId { get; set; }
		/// <summary>The dispatcher's first name, for Dispatcher messages.</summary>
		public string AuthorName { get; set; }
		public string Text { get; set; }
		/// <summary>IntakeConversationMessageStatuses: 1 Received, 2 Queued, 3 Sent, 4 Failed.</summary>
		public int Status { get; set; }
		public string CreatedOn { get; set; }
	}

	/// <summary>GET DispatcherDesk/GetDesk: the whole board.</summary>
	public class DispatcherDeskResultData
	{
		public bool Enabled { get; set; }
		public string GeneratedOn { get; set; }
		public DispatcherDeskConfigData Config { get; set; }
		public DispatcherDeskMemberData Me { get; set; }
		public List<DispatcherDeskMemberData> Members { get; set; } = new List<DispatcherDeskMemberData>();
		public List<DispatcherQueueItemData> Queue { get; set; } = new List<DispatcherQueueItemData>();
		/// <summary>The caller's open breaks today (active first, then planned).</summary>
		public List<DispatcherBreakData> MyBreaks { get; set; } = new List<DispatcherBreakData>();
		public int MinimumAvailable { get; set; }
		public int AvailableCount { get; set; }
		public int OnBreakCount { get; set; }
		public int OnDutyCount { get; set; }
		public bool BelowMinimum { get; set; }
		/// <summary>The caller is allowed to use the desk (Dispatch app login).</summary>
		public bool IsDispatcher { get; set; }
	}

	public class DispatcherDeskResult : StandardApiResponseV4Base
	{
		public DispatcherDeskResultData Data { get; set; }
	}

	public class DispatcherQueueResult : StandardApiResponseV4Base
	{
		public List<DispatcherQueueItemData> Data { get; set; } = new List<DispatcherQueueItemData>();
	}

	public class DispatcherQueueItemDetailResult : StandardApiResponseV4Base
	{
		public DispatcherQueueItemDetailData Data { get; set; }
	}

	public class DispatcherCoverageSlotData
	{
		public string StartUtc { get; set; }
		public string EndUtc { get; set; }
		public int OnDuty { get; set; }
		public int OnBreak { get; set; }
		public int Available { get; set; }
		public int Minimum { get; set; }
		public bool BelowMinimum { get; set; }
		public bool BreaksOverlap { get; set; }
	}

	/// <summary>GET DispatcherDesk/GetCoverage: duty windows, breaks and coverage slots for the timeline.</summary>
	public class DispatcherCoverageResultData
	{
		public string FromUtc { get; set; }
		public string ToUtc { get; set; }
		public int SlotMinutes { get; set; }
		public List<DispatcherDutyData> Duty { get; set; } = new List<DispatcherDutyData>();
		public List<DispatcherBreakData> Breaks { get; set; } = new List<DispatcherBreakData>();
		public List<DispatcherCoverageSlotData> Slots { get; set; } = new List<DispatcherCoverageSlotData>();
		public List<DispatcherDeskWarningData> Warnings { get; set; } = new List<DispatcherDeskWarningData>();
		/// <summary>Display names of every dispatcher in Duty/Breaks.</summary>
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
	}

	public class DispatcherCoverageResult : StandardApiResponseV4Base
	{
		public DispatcherCoverageResultData Data { get; set; }
	}

	/// <summary>A shift's dispatcher settings and roster (GET DispatcherDesk/GetShiftDispatchers).</summary>
	public class ShiftDispatchersResultData
	{
		public int ShiftId { get; set; }
		public bool IsEnabled { get; set; }
		public bool IsDispatchOnly { get; set; }
		public int RequiredDispatchers { get; set; }
		public int MinimumOnDesk { get; set; }
		public bool AllowSelfScheduledBreaks { get; set; }
		public List<DispatcherBreakPlanEntryData> BreakPlan { get; set; } = new List<DispatcherBreakPlanEntryData>();
		/// <summary>The day's roster when a shift day was asked for, else the standing roster.</summary>
		public List<DispatcherDutyData> Dispatchers { get; set; } = new List<DispatcherDutyData>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
		/// <summary>The caller can sign up for the asked day (Signup shift, eligible, not on it yet).</summary>
		public bool CanSignup { get; set; }
		public bool IsSignedUp { get; set; }
	}

	public class ShiftDispatchersResult : StandardApiResponseV4Base
	{
		public ShiftDispatchersResultData Data { get; set; }
	}

	public class DispatcherDeskStatsData
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public int Offered { get; set; }
		public int Accepted { get; set; }
		public int Declined { get; set; }
		public int OffersMissed { get; set; }
		public int Completed { get; set; }
		public int Dispatched { get; set; }
		public double? AverageSecondsToAccept { get; set; }
		public double? AverageSecondsToComplete { get; set; }
		public int Breaks { get; set; }
		public int BreakMinutes { get; set; }
		public int BreaksOverdue { get; set; }
		public int OverdueMinutes { get; set; }
		public int BreaksMissed { get; set; }
	}

	public class DispatcherQueueStatsData
	{
		public int Created { get; set; }
		public int Completed { get; set; }
		public int Overflowed { get; set; }
		public int Cancelled { get; set; }
		public int Escalated { get; set; }
		public double? AverageSecondsToAnswer { get; set; }
		/// <summary>Kind (DispatcherQueueItemKinds value as string) → count.</summary>
		public Dictionary<string, int> CreatedByKind { get; set; } = new Dictionary<string, int>();
		public List<DispatcherDeskStatsData> Dispatchers { get; set; } = new List<DispatcherDeskStatsData>();
	}

	public class DispatcherStatsResult : StandardApiResponseV4Base
	{
		public DispatcherQueueStatsData Data { get; set; }
	}

	/// <summary>GET DispatcherDesk/GetConfig.</summary>
	public class DispatcherDeskConfigResult : StandardApiResponseV4Base
	{
		public DispatcherDeskConfigData Data { get; set; }
	}

	/// <summary>GET DispatcherDesk/GetBreaks: every dispatcher's breaks planned or taken in the window (the team timeline).</summary>
	public class DispatcherBreaksResult : StandardApiResponseV4Base
	{
		public List<DispatcherBreakData> Data { get; set; } = new List<DispatcherBreakData>();
	}

	// ---- Inputs ----

	public class DispatcherSignInInput
	{
		/// <summary>phone, tablet or desktop.</summary>
		public string DeviceKind { get; set; }
		public string CallbackNumber { get; set; }
		public bool Unscheduled { get; set; }
		/// <summary>ISO UTC end of unscheduled duty; null = until signed off.</summary>
		public string DutyEndsOn { get; set; }
		public bool StartAway { get; set; }
	}

	public class DispatcherAwayInput
	{
		public string Reason { get; set; }
	}

	public class DispatcherCallbackNumberInput
	{
		public string CallbackNumber { get; set; }
	}

	public class DispatcherDeviceInput
	{
		/// <summary>1 iOS, 2 Android, 3 web (as Devices/RegisterDevice).</summary>
		public int Platform { get; set; }
		public string Token { get; set; }
	}

	public class DispatcherItemInput
	{
		public string ItemId { get; set; }
	}

	public class DispatcherDeclineInput
	{
		public string ItemId { get; set; }
		public string Reason { get; set; }
	}

	public class DispatcherTransferInput
	{
		public string ItemId { get; set; }
		public string ToUserId { get; set; }
		public string Note { get; set; }
	}

	public class DispatcherCompleteInput
	{
		public string ItemId { get; set; }
		/// <summary>DispatcherQueueItemOutcomes.</summary>
		public int Outcome { get; set; }
		public int? CallId { get; set; }
		public string Note { get; set; }
		/// <summary>An intake text thread: tell the texter the conversation ended (default true).</summary>
		public bool? NotifyParticipant { get; set; }
	}

	public class DispatcherLinkCallInput
	{
		public string ItemId { get; set; }
		public int CallId { get; set; }
	}

	public class DispatcherMessageInput
	{
		public string ItemId { get; set; }
		public string Body { get; set; }
	}

	public class DispatcherVoiceInput
	{
		public string ItemId { get; set; }
		/// <summary>Ring this number (E.164); empty = the desk callback number, else the profile mobile.</summary>
		public string CallbackNumber { get; set; }
	}

	public class DispatcherSaveBreakInput
	{
		/// <summary>Empty to plan a new break.</summary>
		public string DispatcherBreakId { get; set; }
		/// <summary>Whose break; empty = the caller. Someone else's needs supervisor rights.</summary>
		public string UserId { get; set; }
		public int? ShiftDayId { get; set; }
		public int BreakType { get; set; }
		/// <summary>ISO UTC planned start.</summary>
		public string ScheduledStartOn { get; set; }
		public int DurationMinutes { get; set; }
		public string Note { get; set; }
	}

	public class DispatcherBreakIdInput
	{
		public string DispatcherBreakId { get; set; }
	}

	public class DispatcherStartBreakInput
	{
		/// <summary>The planned break to start; empty to take one now (BreakType + DurationMinutes).</summary>
		public string DispatcherBreakId { get; set; }
		public int? BreakType { get; set; }
		public int? DurationMinutes { get; set; }
		/// <summary>Put held work back in the queue (otherwise a dispatcher holding work is refused with HoldingWork).</summary>
		public bool ReturnWork { get; set; }
		/// <summary>Whose break; empty = the caller (supervisor for someone else).</summary>
		public string UserId { get; set; }
	}

	public class DispatcherEndBreakInput
	{
		/// <summary>Whose break; empty = the caller (supervisor for someone else).</summary>
		public string UserId { get; set; }
	}

	public class DispatcherPlanBreaksInput
	{
		public int ShiftDayId { get; set; }
		public bool Replace { get; set; }
	}

	public class ShiftDayDispatcherInput
	{
		public int ShiftDayId { get; set; }
		/// <summary>Assign/remove: whose; empty = the caller (signup/withdraw).</summary>
		public string UserId { get; set; }
		public bool IsSupervisor { get; set; }
		public string Note { get; set; }
	}

	/// <summary>POST DispatcherDesk/ReviewDaySignup: a shift manager approves or denies a pending dispatcher signup.</summary>
	public class ShiftDispatcherReviewInput
	{
		public int ShiftDayId { get; set; }
		/// <summary>The pending roster row (DispatcherDutyData.ShiftDispatcherId of the day roster).</summary>
		public string ShiftDispatcherId { get; set; }
		public bool Approve { get; set; }
		public string Note { get; set; }
	}
}
