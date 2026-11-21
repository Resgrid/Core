using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.RecurringCalls
{
	// Recurring calls (v4, M0319/M0320). Dates are "yyyy-MM-dd" and times "HH:mm" in the department's time zone; instants
	// (dispatch times, occurrences, alerts) are ISO-8601 UTC ("2026-11-18T16:00:00Z"). An occurrence is identified by its
	// schedule id and its OccurrenceOn instant.

	/// <summary>A recurring call schedule.</summary>
	public class RecurringCallResultData
	{
		/// <summary>The route plan's name (GetRecurringCall only; null in lists and when the plan can no longer be followed).</summary>
		public string RoutePlanName { get; set; }

		public int RecurringCallId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public bool IsActive { get; set; }
		/// <summary>Past its end date: it will never run again.</summary>
		public bool HasEnded { get; set; }

		/// <summary>1 Daily, 2 Weekly, 3 Monthly.</summary>
		public int Frequency { get; set; }
		public int RepeatInterval { get; set; }
		/// <summary>Weekly: the <c>System.DayOfWeek</c> values it runs on (0 = Sunday).</summary>
		public List<int> Days { get; set; } = new List<int>();
		/// <summary>Monthly by date (1-31); 0 when monthly by weekday.</summary>
		public int MonthlyDay { get; set; }
		/// <summary>Monthly by weekday: 1-4, or 5 for the last; 0 when monthly by date.</summary>
		public int MonthlyWeek { get; set; }
		public int MonthlyWeekday { get; set; }
		/// <summary>"HH:mm" local times.</summary>
		public List<string> Times { get; set; } = new List<string>();
		public string StartsOn { get; set; }
		public string EndsOn { get; set; }
		public int LeadTimeMinutes { get; set; }

		/// <summary>0 Automatic (the system creates and dispatches each call), 1 Dispatcher (a dispatcher is alerted and sends it).</summary>
		public int CreationMode { get; set; }
		public int ManualAlertMinutes { get; set; }
		public bool UseRunCard { get; set; }

		// The call each occurrence creates
		public string NatureOfCall { get; set; }
		public string Notes { get; set; }
		public string Address { get; set; }
		/// <summary>"lat,lon".</summary>
		public string GeoLocationData { get; set; }
		public int Priority { get; set; }
		public string Type { get; set; }
		public string ContactName { get; set; }
		public string ContactNumber { get; set; }
		public string ExternalIdentifier { get; set; }
		public string ReferenceNumber { get; set; }
		public string IncidentNumber { get; set; }
		public string MapPage { get; set; }
		public int? DestinationPoiId { get; set; }
		public bool CheckInTimersEnabled { get; set; }

		// About the schedule
		public string ScheduleContactName { get; set; }
		public string ScheduleContactNumber { get; set; }
		public string ScheduleContactEmail { get; set; }
		public string FacilityName { get; set; }
		public string FacilityDetails { get; set; }
		public int? FacilityPoiId { get; set; }
		public string DestinationName { get; set; }
		public string DestinationDetails { get; set; }

		/// <summary>Free destination address when the destination is not a POI (M0322).</summary>
		public string DestinationAddress { get; set; }

		/// <summary>Free destination point "lat,lon" (M0322).</summary>
		public string DestinationGeoLocationData { get; set; }

		/// <summary>The route plan the created calls follow (M0322, Routes/GetCallRoutePlans).</summary>
		public string RoutePlanId { get; set; }

		// Who
		public List<string> DispatchUserIds { get; set; } = new List<string>();
		public List<int> DispatchGroupIds { get; set; } = new List<int>();
		public List<int> DispatchUnitIds { get; set; } = new List<int>();
		public List<int> DispatchRoleIds { get; set; } = new List<int>();
		public List<string> AlertUserIds { get; set; } = new List<string>();
		public List<int> AlertGroupIds { get; set; } = new List<int>();
		public List<int> AlertRoleIds { get; set; } = new List<int>();

		// Pause
		/// <summary>ISO UTC; null while running.</summary>
		public string PausedOn { get; set; }
		public string PausedByUserId { get; set; }
		public string PausedByName { get; set; }
		public string PauseReason { get; set; }
		/// <summary>"yyyy-MM-dd" (department time) it resumes by itself; null to stay paused.</summary>
		public string ResumeOn { get; set; }

		public string CreatedByUserId { get; set; }
		public string CreatedByName { get; set; }
		/// <summary>ISO UTC.</summary>
		public string CreatedOn { get; set; }
		public string UpdatedOn { get; set; }

		/// <summary>The next time a call goes out (ISO UTC): a waiting call or an occurrence still to come; null when none.</summary>
		public string NextDispatchOn { get; set; }
		public int OpenAlertCount { get; set; }
		/// <summary>The viewer may edit, pause, resume or delete it (its creator or a department admin who can create calls).</summary>
		public bool CanManage { get; set; }
	}

	public class RecurringCallsResult : StandardApiResponseV4Base
	{
		public List<RecurringCallResultData> Data { get; set; } = new List<RecurringCallResultData>();
	}

	/// <summary>One schedule with what is coming up.</summary>
	public class RecurringCallDetailResultData
	{
		public RecurringCallResultData RecurringCall { get; set; }
		/// <summary>The next dispatch times (ISO UTC), at most six.</summary>
		public List<string> NextDispatches { get; set; } = new List<string>();
		/// <summary>Calls it created that have not gone out (waiting or held).</summary>
		public List<RecurringCallUpcomingCallData> UpcomingCalls { get; set; } = new List<RecurringCallUpcomingCallData>();
	}

	public class RecurringCallUpcomingCallData
	{
		public int CallId { get; set; }
		public string Number { get; set; }
		public string Name { get; set; }
		/// <summary>ISO UTC.</summary>
		public string DispatchOn { get; set; }
		/// <summary>Pending, waiting for a dispatcher to send it (nobody could be sent, or it was created by hand once due).</summary>
		public bool IsHeld { get; set; }
	}

	public class RecurringCallResult : StandardApiResponseV4Base
	{
		public RecurringCallDetailResultData Data { get; set; }
	}

	/// <summary>What a recurring call form needs to know.</summary>
	public class RecurringCallOptionsResultData
	{
		public bool CanCreate { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		/// <summary>Run cards are turned on (UseRunCard does something).</summary>
		public bool RunCardsEnabled { get; set; }
		/// <summary>The department's time zone (the times and dates are in it).</summary>
		public string TimeZone { get; set; }
		public string Today { get; set; }
		public int DefaultLeadTimeMinutes { get; set; }
		public int MinLeadTimeMinutes { get; set; }
		public int MaxLeadTimeMinutes { get; set; }
		public List<int> LeadTimeChoices { get; set; } = new List<int>();
		public int DefaultManualAlertMinutes { get; set; }
		public int MaxManualAlertMinutes { get; set; }
		public List<int> ManualAlertChoices { get; set; } = new List<int>();
		public int MaxTimesPerDay { get; set; }
		public int MaxInterval { get; set; }
	}

	public class RecurringCallOptionsResult : StandardApiResponseV4Base
	{
		public RecurringCallOptionsResultData Data { get; set; }
	}

	/// <summary>Creates (RecurringCallId 0) or updates a schedule. Lists left null are empty.</summary>
	public class RecurringCallInput
	{
		public int RecurringCallId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		/// <summary>New schedules only: false to save it paused. Use Pause/Resume for an existing one.</summary>
		public bool? IsActive { get; set; }

		public int Frequency { get; set; } = 2;
		public int RepeatInterval { get; set; } = 1;
		public List<int> Days { get; set; }
		public int MonthlyDay { get; set; }
		public int MonthlyWeek { get; set; }
		public int MonthlyWeekday { get; set; }
		public List<string> Times { get; set; }
		public string StartsOn { get; set; }
		public string EndsOn { get; set; }
		public int? LeadTimeMinutes { get; set; }
		public int CreationMode { get; set; }
		public int? ManualAlertMinutes { get; set; }
		public bool UseRunCard { get; set; }

		public string NatureOfCall { get; set; }
		public string Notes { get; set; }
		public string Address { get; set; }
		public string GeoLocationData { get; set; }
		public int Priority { get; set; }
		public string Type { get; set; }
		public string ContactName { get; set; }
		public string ContactNumber { get; set; }
		public string ExternalIdentifier { get; set; }
		public string ReferenceNumber { get; set; }
		public string IncidentNumber { get; set; }
		public string MapPage { get; set; }
		public int? DestinationPoiId { get; set; }
		public bool CheckInTimersEnabled { get; set; }

		public string ScheduleContactName { get; set; }
		public string ScheduleContactNumber { get; set; }
		public string ScheduleContactEmail { get; set; }
		public string FacilityName { get; set; }
		public string FacilityDetails { get; set; }
		public int? FacilityPoiId { get; set; }
		public string DestinationName { get; set; }
		public string DestinationDetails { get; set; }

		/// <summary>Free destination address when the destination is not a POI (M0322).</summary>
		public string DestinationAddress { get; set; }

		/// <summary>Free destination point "lat,lon" (M0322).</summary>
		public string DestinationGeoLocationData { get; set; }

		/// <summary>The route plan the created calls follow (M0322, Routes/GetCallRoutePlans).</summary>
		public string RoutePlanId { get; set; }

		public List<string> DispatchUserIds { get; set; }
		public List<int> DispatchGroupIds { get; set; }
		public List<int> DispatchUnitIds { get; set; }
		public List<int> DispatchRoleIds { get; set; }
		public List<string> AlertUserIds { get; set; }
		public List<int> AlertGroupIds { get; set; }
		public List<int> AlertRoleIds { get; set; }

		/// <summary>
		/// Updating: delete and re-create the scheduled calls already created that have not gone out (default true). A held
		/// (pending) call is not replaced: it has an open alert and stays the dispatcher's to send or close.
		/// </summary>
		public bool ReplaceUpcomingCalls { get; set; } = true;
	}

	public class SaveRecurringCallResultData
	{
		public int RecurringCallId { get; set; }
		public int CreatedCalls { get; set; }
		public int ReplacedCalls { get; set; }
	}

	public class SaveRecurringCallResult : StandardApiResponseV4Base
	{
		public SaveRecurringCallResultData Data { get; set; }

		/// <summary>
		/// Why it was not saved: stable keys ("ErrorDaysRequired", "ErrorStartsOnRequired", "NewCallField_address", ...) the client
		/// words itself; with an English message for each.
		/// </summary>
		public List<RecurringCallErrorData> Errors { get; set; } = new List<RecurringCallErrorData>();
	}

	public class RecurringCallErrorData
	{
		public string Key { get; set; }
		public string Message { get; set; }
	}

	public class PauseRecurringCallInput
	{
		public int RecurringCallId { get; set; }
		public string Reason { get; set; }
		/// <summary>"yyyy-MM-dd" in the department's time zone; null to stay paused.</summary>
		public string ResumeOn { get; set; }
		/// <summary>Also delete the scheduled calls already created that have not gone out (held calls stay).</summary>
		public bool DeleteUpcomingCalls { get; set; }
	}

	public class RecurringCallIdInput
	{
		public int RecurringCallId { get; set; }
	}

	public class DeleteRecurringCallInput
	{
		public int RecurringCallId { get; set; }
		/// <summary>Also delete the scheduled calls already created that have not gone out (default true; held calls stay).</summary>
		public bool DeleteUpcomingCalls { get; set; } = true;
	}

	/// <summary>Acts on one occurrence of a schedule.</summary>
	public class RecurringCallOccurrenceInput
	{
		public int RecurringCallId { get; set; }
		/// <summary>The occurrence, ISO-8601 UTC, as the calendar and alerts give it.</summary>
		public string OccurrenceOn { get; set; }
	}

	public class RecurringCallActionResult : StandardApiResponseV4Base
	{
		public bool Success { get; set; }
		public string Message { get; set; }
		public RecurringCallResultData Data { get; set; }
	}

	public class RecurringCallOccurrenceResultData
	{
		/// <summary>Dispatched, Held (nobody could be sent; pending), NotWaiting, QueueFailed, NotCreated, or Created (not sent).</summary>
		public string Outcome { get; set; }
		public int? CallId { get; set; }
		public string CallNumber { get; set; }
		/// <summary>The call's state: 0 active (scheduled or sent), 8 pending.</summary>
		public int? CallState { get; set; }
		public List<int> UnavailableUnitIds { get; set; } = new List<int>();
		public List<string> UnavailableUserIds { get; set; } = new List<string>();
		public bool RunCardApplied { get; set; }
	}

	public class RecurringCallOccurrenceResult : StandardApiResponseV4Base
	{
		public bool Success { get; set; }
		public RecurringCallOccurrenceResultData Data { get; set; }
	}

	/// <summary>One entry of the scheduled calls calendar.</summary>
	public class ScheduledCallCalendarEntryData
	{
		/// <summary>Scheduled, Dispatched, Cancelled, Projected, Skipped, Due, Missed or Held.</summary>
		public string Kind { get; set; }
		public int KindId { get; set; }
		/// <summary>When it goes (went) out, ISO UTC.</summary>
		public string DispatchOn { get; set; }
		/// <summary>The same in the department's time zone, "yyyy-MM-ddTHH:mm:ss" without an offset.</summary>
		public string DispatchOnLocal { get; set; }
		/// <summary>The occurrence it stands for (ISO UTC), for entries of a recurring schedule; the key of the occurrence actions.</summary>
		public string OccurrenceOn { get; set; }
		public string Name { get; set; }
		public int Priority { get; set; }
		public string Address { get; set; }
		public int? CallId { get; set; }
		public string CallNumber { get; set; }
		public int? RecurringCallId { get; set; }
		public string ScheduleName { get; set; }
	}

	public class ScheduledCallsCalendarResult : StandardApiResponseV4Base
	{
		public List<ScheduledCallCalendarEntryData> Data { get; set; } = new List<ScheduledCallCalendarEntryData>();
	}

	/// <summary>Something about one occurrence a dispatcher has to act on.</summary>
	public class RecurringCallAlertResultData
	{
		public string AlertId { get; set; }
		public int RecurringCallId { get; set; }
		public string ScheduleName { get; set; }
		public bool DispatcherMode { get; set; }
		public bool ScheduleDeleted { get; set; }
		/// <summary>CreateFailed, Missed, NoResources, ResourcesUnavailable or DispatchDue.</summary>
		public string Kind { get; set; }
		public int KindId { get; set; }
		/// <summary>NoneChosen, NoneAvailable, SaveFailed or DepartmentUnavailable, when there is one.</summary>
		public string Reason { get; set; }
		/// <summary>ISO UTC: the occurrence (the key of the occurrence actions).</summary>
		public string OccurrenceOn { get; set; }
		/// <summary>The occurrence in the department's time zone, "yyyy-MM-ddTHH:mm:ss".</summary>
		public string OccurrenceOnLocal { get; set; }
		public int? CallId { get; set; }
		public string CallNumber { get; set; }
		public int? CallState { get; set; }
		public bool CallDispatched { get; set; }
		public List<RecurringCallAlertResourceData> UnavailableUnits { get; set; } = new List<RecurringCallAlertResourceData>();
		public List<RecurringCallAlertResourceData> UnavailablePeople { get; set; } = new List<RecurringCallAlertResourceData>();
		/// <summary>ISO UTC.</summary>
		public string CreatedOn { get; set; }
		public string AcknowledgedOn { get; set; }
		public string AcknowledgedByUserId { get; set; }
		public string AcknowledgedByName { get; set; }
		public bool IsOpen { get; set; }
		public string ResolvedOn { get; set; }
		public string Resolution { get; set; }
		public string ResolvedByName { get; set; }
		public string ResolutionNote { get; set; }
		/// <summary>The occurrence has no call: it can be sent (DispatchOccurrenceNow), created or skipped.</summary>
		public bool CanSend { get; set; }
		/// <summary>The viewer may act on it (send, skip, acknowledge, dismiss): anyone who can create calls.</summary>
		public bool CanAct { get; set; }
	}

	public class RecurringCallAlertResourceData
	{
		public string Id { get; set; }
		public string Name { get; set; }
	}

	public class RecurringCallAlertsResult : StandardApiResponseV4Base
	{
		public List<RecurringCallAlertResultData> Data { get; set; } = new List<RecurringCallAlertResultData>();
	}

	public class RecurringCallAlertInput
	{
		public string AlertId { get; set; }
		/// <summary>Dismiss: why (optional).</summary>
		public string Note { get; set; }
	}
}
