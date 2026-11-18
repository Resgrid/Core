using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models.Dispatch
{
	/// <summary>Calls → Recurring Calls: the department's schedules.</summary>
	public class RecurringCallsIndexView
	{
		public List<RecurringCallRow> Rows { get; set; } = new List<RecurringCallRow>();

		/// <summary>The open alerts the member can see ("Needs attention").</summary>
		public List<RecurringCallAlertRow> Alerts { get; set; } = new List<RecurringCallAlertRow>();

		public bool CanCreate { get; set; }

		public string Message { get; set; }

		public string ErrorMessage { get; set; }
	}

	public class RecurringCallRow
	{
		public int RecurringCallId { get; set; }

		public string Name { get; set; }

		/// <summary>"Every week on Tuesday, Thursday at 08:00, 13:00, 19:00".</summary>
		public string Summary { get; set; }

		/// <summary>"From 18/11/2026 · Until 31/12/2026".</summary>
		public string Dates { get; set; }

		public string NextDispatch { get; set; }

		public string PriorityName { get; set; }

		public string PriorityColor { get; set; }

		public bool IsActive { get; set; }

		/// <summary>Past its end date: it will never run again.</summary>
		public bool HasEnded { get; set; }

		public bool CanManage { get; set; }

		/// <summary>A dispatcher sends each occurrence.</summary>
		public bool DispatcherMode { get; set; }

		public string Facility { get; set; }

		public string PauseReason { get; set; }

		/// <summary>The date it resumes by itself, formatted.</summary>
		public string ResumeOn { get; set; }

		public int OpenAlerts { get; set; }
	}

	/// <summary>One open recurring call alert on the Recurring Calls page.</summary>
	public class RecurringCallAlertRow
	{
		public string AlertId { get; set; }

		public int RecurringCallId { get; set; }

		public string ScheduleName { get; set; }

		public RecurringCallAlertKinds Kind { get; set; }

		/// <summary>The occurrence, Unix seconds (what the occurrence actions take).</summary>
		public long Occurrence { get; set; }

		/// <summary>The occurrence in the department's time zone, formatted.</summary>
		public string When { get; set; }

		public int? CallId { get; set; }

		public string CallNumber { get; set; }

		public string Reason { get; set; }

		/// <summary>Names of the chosen units and members that were not available.</summary>
		public string Unavailable { get; set; }

		public string AcknowledgedBy { get; set; }

		/// <summary>The member may act on the alert (acknowledge, dismiss); see <see cref="CanSend"/> for the occurrence actions.</summary>
		public bool CanAct { get; set; }

		/// <summary>
		/// The occurrence has no call yet and the schedule (not deleted) still has it: it can be sent, created or skipped from here
		/// (<see cref="Resgrid.Model.Services.RecurringCallAlertInfo.CanSend"/>, the same rule the API reports).
		/// </summary>
		public bool CanSend { get; set; }
	}

	/// <summary>The New / Edit Recurring Call form. Dates are "yyyy-MM-dd" and times "HH:mm", in the department's time zone.</summary>
	public class RecurringCallEditView
	{
		public int RecurringCallId { get; set; }

		// Schedule

		public int Frequency { get; set; } = (int)RecurringCallFrequencies.Weekly;

		public int RepeatInterval { get; set; } = 1;

		/// <summary>Weekly: the <see cref="System.DayOfWeek"/> values ticked.</summary>
		public List<int> Days { get; set; } = new List<int>();

		/// <summary>Monthly: 0 = on a date (<see cref="MonthlyDay"/>), 1 = on an nth weekday.</summary>
		public int MonthlyMode { get; set; }

		public int MonthlyDay { get; set; } = 1;

		public int MonthlyWeek { get; set; } = 1;

		public int MonthlyWeekday { get; set; } = 1;

		public List<string> Times { get; set; } = new List<string>();

		public string StartsOn { get; set; }

		public string EndsOn { get; set; }

		public int LeadTimeMinutes { get; set; } = RecurringCallRecurrence.DefaultLeadTimeMinutes;

		public bool IsActive { get; set; } = true;

		/// <summary><see cref="RecurringCallCreationModes"/>.</summary>
		public int CreationMode { get; set; } = (int)RecurringCallCreationModes.Automatic;

		public int ManualAlertMinutes { get; set; } = RecurringCallRecurrence.DefaultManualAlertMinutes;

		public bool UseRunCard { get; set; }

		// About the schedule

		public string Description { get; set; }

		public string ScheduleContactName { get; set; }

		public string ScheduleContactNumber { get; set; }

		public string ScheduleContactEmail { get; set; }

		public string FacilityName { get; set; }

		public string FacilityDetails { get; set; }

		public int? FacilityPoiId { get; set; }

		public string DestinationName { get; set; }

		public string DestinationDetails { get; set; }

		public List<string> AlertUserIds { get; set; } = new List<string>();

		public List<int> AlertGroupIds { get; set; } = new List<int>();

		public List<int> AlertRoleIds { get; set; } = new List<int>();

		// The call

		public string Name { get; set; }

		public int Priority { get; set; }

		public string Type { get; set; }

		public string NatureOfCall { get; set; }

		public string Notes { get; set; }

		public string Address { get; set; }

		/// <summary>"lat,lon"; blank to locate <see cref="Address"/>.</summary>
		public string Coordinates { get; set; }

		public int? DestinationPoiId { get; set; }

		public string ContactName { get; set; }

		public string ContactNumber { get; set; }

		public string ExternalIdentifier { get; set; }

		public string ReferenceNumber { get; set; }

		public string IncidentNumber { get; set; }

		public string MapPage { get; set; }

		public bool CheckInTimersEnabled { get; set; }

		public List<string> DispatchUserIds { get; set; } = new List<string>();

		public List<int> DispatchGroupIds { get; set; } = new List<int>();

		public List<int> DispatchUnitIds { get; set; } = new List<int>();

		public List<int> DispatchRoleIds { get; set; } = new List<int>();

		/// <summary>Editing: delete and re-create the calls already created that have not gone out.</summary>
		public bool ReplaceUpcomingCalls { get; set; } = true;

		// Display only

		public int UpcomingCallCount { get; set; }

		public string FromCallNumber { get; set; }

		public string TimeZoneName { get; set; }

		public List<string> NextDispatches { get; set; } = new List<string>();

		public bool CanManage { get; set; } = true;

		public SelectList Priorities { get; set; }

		public List<SelectListItem> CallTypes { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> DestinationPois { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> Personnel { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> Groups { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> Units { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> Roles { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> LeadTimes { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> ManualAlertChoices { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> FacilityPois { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> AlertPersonnel { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> AlertGroups { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> AlertRoles { get; set; } = new List<SelectListItem>();

		/// <summary>Run cards are turned on for the department (the run card option does something).</summary>
		public bool RunCardsEnabled { get; set; }

		/// <summary>Paused: why, and until when (formatted), shown above the form.</summary>
		public string PauseReason { get; set; }

		public string ResumeOn { get; set; }

		public List<SelectListItem> WeekDays { get; set; } = new List<SelectListItem>();

		public NewCallFieldPolicy FieldPolicy { get; set; }

		public bool IsNew => RecurringCallId == 0;

		public bool IsVisible(string key) => FieldPolicy == null || FieldPolicy.IsVisible(key);

		public bool IsRequired(string key) => FieldPolicy != null && FieldPolicy.IsRequired(key);
	}
}
