using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>The outcome of a desk action: the value on success, otherwise why it was refused. Warnings never block.</summary>
	public class DispatcherDeskResult<T>
	{
		public bool Success { get; set; }

		public DispatcherDeskErrors Error { get; set; }

		/// <summary>Optional detail for the refusal (English, for logs; clients localize <see cref="Error"/>).</summary>
		public string Message { get; set; }

		public T Value { get; set; }

		/// <summary>Things the caller should show but that did not stop the action (coverage below minimum, overlapping breaks).</summary>
		public List<DispatcherDeskWarning> Warnings { get; set; } = new List<DispatcherDeskWarning>();

		public static DispatcherDeskResult<T> Ok(T value, List<DispatcherDeskWarning> warnings = null) =>
			new DispatcherDeskResult<T> { Success = true, Value = value, Warnings = warnings ?? new List<DispatcherDeskWarning>() };

		public static DispatcherDeskResult<T> Fail(DispatcherDeskErrors error, string message = null, List<DispatcherDeskWarning> warnings = null) =>
			new DispatcherDeskResult<T> { Success = false, Error = error, Message = message, Warnings = warnings ?? new List<DispatcherDeskWarning>() };
	}

	/// <summary>Kinds of <see cref="DispatcherDeskWarning"/>. Sent by name; append only.</summary>
	public enum DispatcherDeskWarningKinds
	{
		/// <summary>Fewer dispatchers than the minimum would be available.</summary>
		CoverageBelowMinimum = 1,

		/// <summary>Two or more dispatchers' breaks overlap.</summary>
		BreaksOverlap = 2,

		/// <summary>The break falls (partly) outside the dispatcher's shift day.</summary>
		OutsideDuty = 3,

		/// <summary>Nobody at all would be available.</summary>
		NoCoverage = 4,

		/// <summary>The dispatcher held work that went back to the queue.</summary>
		WorkReturnedToQueue = 5,

		/// <summary>The break started later than planned.</summary>
		StartedLate = 6,

		/// <summary>The break started earlier than planned.</summary>
		StartedEarly = 7
	}

	public class DispatcherDeskWarning
	{
		public DispatcherDeskWarningKinds Kind { get; set; }

		public DateTime? StartUtc { get; set; }

		public DateTime? EndUtc { get; set; }

		/// <summary>The dispatchers involved.</summary>
		public List<string> UserIds { get; set; } = new List<string>();

		/// <summary>How many would be available (coverage warnings).</summary>
		public int? Available { get; set; }

		/// <summary>The minimum that applied.</summary>
		public int? Minimum { get; set; }

		/// <summary>Minutes early or late (StartedEarly / StartedLate).</summary>
		public int? Minutes { get; set; }
	}

	/// <summary>A dispatcher's time on a shift day's dispatch roster.</summary>
	public class DispatcherDutyWindow
	{
		public string UserId { get; set; }

		public int ShiftId { get; set; }

		public int ShiftDayId { get; set; }

		public string ShiftName { get; set; }

		public string ShiftColor { get; set; }

		/// <summary>The shift day's local date.</summary>
		public DateTime Day { get; set; }

		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		public bool IsSupervisor { get; set; }

		/// <summary><see cref="ShiftDispatcherSources"/>.</summary>
		public int Source { get; set; }

		/// <summary>Not yet approved (signup on a shift requiring approval); listed but not on duty.</summary>
		public bool IsPending { get; set; }

		public string ShiftDispatcherId { get; set; }

		public bool Covers(DateTime utc) => !IsPending && utc >= StartUtc && utc < EndUtc;
	}

	/// <summary>One dispatcher as the desk board shows them.</summary>
	public class DispatcherDeskMember
	{
		public string UserId { get; set; }

		public string Name { get; set; }

		public DispatcherDisplayStates DisplayState { get; set; }

		/// <summary>Null when the dispatcher has never signed in to the desk.</summary>
		public DispatcherPresence Presence { get; set; }

		/// <summary>The shift day they are on duty for now; null when off duty or on unscheduled duty.</summary>
		public DispatcherDutyWindow Duty { get; set; }

		public bool IsOnDuty { get; set; }

		public bool IsSupervisor { get; set; }

		public DispatcherBreak ActiveBreak { get; set; }

		/// <summary>The next planned break not yet taken (today).</summary>
		public DispatcherBreak NextBreak { get; set; }

		public int ActiveItemCount { get; set; }

		public int OfferedItemCount { get; set; }

		public int Capacity { get; set; }

		/// <summary>Minutes past the expected return (on break only); 0 when not overdue.</summary>
		public int OverdueMinutes { get; set; }
	}

	/// <summary>Everything the desk board and the Dispatch app's desk screen show.</summary>
	public class DispatcherDeskSnapshot
	{
		public bool Enabled { get; set; }

		public DateTime GeneratedOn { get; set; }

		public List<DispatcherDeskMember> Members { get; set; } = new List<DispatcherDeskMember>();

		/// <summary>Open items: queued, offered and held.</summary>
		public List<DispatcherQueueItem> Queue { get; set; } = new List<DispatcherQueueItem>();

		public int MinimumAvailable { get; set; }

		public int AvailableCount { get; set; }

		public int OnBreakCount { get; set; }

		public int OnDutyCount { get; set; }

		public bool BelowMinimum => MinimumAvailable > 0 && AvailableCount < MinimumAvailable;
	}

	/// <summary>One slice of a coverage timeline.</summary>
	public class DispatcherCoverageSlot
	{
		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		public int OnDuty { get; set; }

		public int OnBreak { get; set; }

		public int Available => Math.Max(0, OnDuty - OnBreak);

		public int Minimum { get; set; }

		public bool BelowMinimum => OnDuty > 0 && Minimum > 0 && Available < Minimum;

		/// <summary>Two or more breaks overlap in this slice.</summary>
		public bool BreaksOverlap => OnBreak > 1;
	}

	/// <summary>Who is on the desk and on break across a period, sliced for the coverage timeline.</summary>
	public class DispatcherCoverage
	{
		public DateTime FromUtc { get; set; }

		public DateTime ToUtc { get; set; }

		public int SlotMinutes { get; set; }

		public List<DispatcherDutyWindow> Duty { get; set; } = new List<DispatcherDutyWindow>();

		public List<DispatcherBreak> Breaks { get; set; } = new List<DispatcherBreak>();

		public List<DispatcherCoverageSlot> Slots { get; set; } = new List<DispatcherCoverageSlot>();

		public List<DispatcherDeskWarning> Warnings { get; set; } = new List<DispatcherDeskWarning>();
	}

	/// <summary>Signing in to the desk from the Dispatch app (or the web desk board).</summary>
	public class DispatcherSignInRequest
	{
		/// <summary>phone, tablet or desktop.</summary>
		public string DeviceKind { get; set; }

		/// <summary>The phone to ring for voice calls (E.164); null keeps the stored one / the profile mobile.</summary>
		public string CallbackNumber { get; set; }

		/// <summary>Sign on without a shift (needs <see cref="DispatcherDeskConfig.AllowUnscheduledDuty"/> or RequireShift off).</summary>
		public bool Unscheduled { get; set; }

		/// <summary>The planned end of unscheduled duty, UTC; null = until signed off.</summary>
		public DateTime? DutyEndsOn { get; set; }

		/// <summary>Start Away rather than Available.</summary>
		public bool StartAway { get; set; }
	}

	/// <summary>A piece of work to put on the desk.</summary>
	public class DispatcherEnqueueRequest
	{
		public int DepartmentId { get; set; }

		public DispatcherQueueItemKinds Kind { get; set; }

		public int? CallId { get; set; }

		public string SourceAddress { get; set; }

		public string SourceName { get; set; }

		public string TargetAddress { get; set; }

		public string Subject { get; set; }

		/// <summary>The message text (first conversation message for live channels) or the call's nature.</summary>
		public string Body { get; set; }

		public string ExternalReference { get; set; }

		public string ProviderMessageId { get; set; }

		/// <summary>Null = from the kind and the call's priority.</summary>
		public int? Priority { get; set; }

		public DateTime? DueOn { get; set; }

		public string CreatedByUserId { get; set; }

		/// <summary>Offer it to this dispatcher first (a transfer target, or the dispatcher the sender last spoke to).</summary>
		public string PreferredUserId { get; set; }

		/// <summary>
		/// An intake text thread (<see cref="IntakeConversation"/>, dispatchers-on-duty-plan.md F2): the thread and its messages
		/// live in intake, which already created the pending call (<see cref="CallId"/>). The item links the thread
		/// (<see cref="DispatcherRoutingPolicy.IntakeReference"/>) and keeps no conversation key, messages or acknowledgement of its
		/// own; it never overflows (the call exists).
		/// </summary>
		public string IntakeConversationId { get; set; }
	}

	/// <summary>What routing did with an item.</summary>
	public enum DispatcherRouteOutcomes
	{
		/// <summary>The desk is off, or does not take this kind; the caller handles it as before.</summary>
		NotRouted = 0,

		/// <summary>Offered to a dispatcher.</summary>
		Offered = 1,

		/// <summary>Waiting in the shared queue (no dispatcher free; calls only).</summary>
		Queued = 2,

		/// <summary>No dispatcher free for a live channel: the caller runs the department's overflow handling.</summary>
		Overflow = 3,

		/// <summary>Joined the sender's open conversation (follow-up message).</summary>
		Joined = 4,

		/// <summary>Already on the desk (the same call or provider message).</summary>
		Duplicate = 5
	}

	public class DispatcherRouteResult
	{
		public DispatcherRouteOutcomes Outcome { get; set; }

		public DispatcherQueueItem Item { get; set; }

		/// <summary>Who it was offered to (Offered) or who holds the conversation (Joined).</summary>
		public string UserId { get; set; }

		public static DispatcherRouteResult NotRouted() => new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.NotRouted };
	}

	/// <summary>A routed item with its history and conversation.</summary>
	public class DispatcherQueueItemDetail
	{
		public DispatcherQueueItem Item { get; set; }

		public List<DispatcherQueueEvent> Events { get; set; } = new List<DispatcherQueueEvent>();

		public List<DispatcherConversationMessage> Messages { get; set; } = new List<DispatcherConversationMessage>();

		/// <summary>The call it is about, when there is one (loaded without dispatch lists).</summary>
		public Call Call { get; set; }

		/// <summary>The intake text thread linked to the item, with its messages, when there is one (read for the viewer).</summary>
		public IntakeConversationView IntakeConversation { get; set; }
	}

	/// <summary>Per-dispatcher desk numbers for a period (desk activity report).</summary>
	public class DispatcherDeskStats
	{
		public string UserId { get; set; }

		public string Name { get; set; }

		public int Offered { get; set; }

		public int Accepted { get; set; }

		public int Declined { get; set; }

		public int OffersMissed { get; set; }

		public int Completed { get; set; }

		public int Dispatched { get; set; }

		/// <summary>Mean seconds from offer (or creation, when taken from the queue) to accept.</summary>
		public double? AverageSecondsToAccept { get; set; }

		/// <summary>Mean seconds from accept to completion.</summary>
		public double? AverageSecondsToComplete { get; set; }

		public int Breaks { get; set; }

		public int BreakMinutes { get; set; }

		public int BreaksOverdue { get; set; }

		public int OverdueMinutes { get; set; }

		public int BreaksMissed { get; set; }
	}

	/// <summary>Department-wide queue numbers for a period.</summary>
	public class DispatcherQueueStats
	{
		public int Created { get; set; }

		public int Completed { get; set; }

		public int Overflowed { get; set; }

		public int Cancelled { get; set; }

		public int Escalated { get; set; }

		/// <summary>Mean seconds from creation to accept.</summary>
		public double? AverageSecondsToAnswer { get; set; }

		public Dictionary<int, int> CreatedByKind { get; set; } = new Dictionary<int, int>();

		public List<DispatcherDeskStats> Dispatchers { get; set; } = new List<DispatcherDeskStats>();
	}
}
