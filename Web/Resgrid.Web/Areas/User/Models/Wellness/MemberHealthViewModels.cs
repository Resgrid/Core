using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Wellness
{
	/// <summary>Health records home (WL1): the member's own cases, injuries and clearance; the officers' open work.</summary>
	public class MemberHealthIndexView
	{
		public MemberHealthAccess Access { get; set; } = new MemberHealthAccess();
		public Department Department { get; set; }
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<RmsExposureIncident> MyCases { get; set; } = new List<RmsExposureIncident>();
		public List<RmsMemberInjury> MyInjuries { get; set; } = new List<RmsMemberInjury>();
		public MemberDutyStatus MyStatus { get; set; }
		public List<RmsExposureIncident> OpenCases { get; set; } = new List<RmsExposureIncident>();
		public List<RmsMemberInjury> OpenInjuries { get; set; } = new List<RmsMemberInjury>();
		public List<MemberDutyStatus> DutyBoard { get; set; } = new List<MemberDutyStatus>();
		public string Message { get; set; }
		public string Error { get; set; }

		public string NameOf(string userId) => MemberHealthPresentation.NameOf(Names, userId);
	}

	/// <summary>One BBP / infectious-disease exposure case.</summary>
	public class ExposureCaseView
	{
		public RmsExposureIncident Case { get; set; }
		public bool IsDesignatedOfficer { get; set; }
		public bool IsSelf { get; set; }
		public string ViewerUserId { get; set; }
		public string MemberName { get; set; }
		public string CallName { get; set; }
		public BbpTrainingStatus BbpTraining { get; set; } = new BbpTrainingStatus();
		public List<RmsRecordWorkAssignment> FollowUps { get; set; } = new List<RmsRecordWorkAssignment>();
		public List<PersonName> FollowUpAssignees { get; set; } = new List<PersonName>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Department Department { get; set; }
		public bool ProtectedWithheld { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }

		public IReadOnlyList<ExposureIncidentStages> NextStages => Case == null ? Array.Empty<ExposureIncidentStages>() : ExposureIncidentWorkflow.NextStages(Case.Stage);
		public string NameOf(string userId) => MemberHealthPresentation.NameOf(Names, userId);
	}

	/// <summary>Reporting a BBP / infectious-disease exposure.</summary>
	public class ReportExposureView
	{
		public string MemberUserId { get; set; }
		public string ResponderExposureId { get; set; }
		public int Route { get; set; } = (int)ExposureIncidentRoutes.Percutaneous;
		/// <summary>Department-local "yyyy-MM-ddTHH:mm".</summary>
		public string OccurredOnLocal { get; set; }
		public int? CallId { get; set; }
		public string Narrative { get; set; }
		public string PpeWorn { get; set; }
		public bool IsDesignatedOfficer { get; set; }
		public List<PersonName> Members { get; set; } = new List<PersonName>();
		public List<WellnessCallOption> Calls { get; set; } = new List<WellnessCallOption>();
		/// <summary>The member's confirmed bloodborne (or Other) WL0 entries without an open case.</summary>
		public List<ExposureEntryOption> Entries { get; set; } = new List<ExposureEntryOption>();
		public string Error { get; set; }
	}

	public class ExposureEntryOption
	{
		public string Id { get; set; }
		public string Label { get; set; }
	}

	/// <summary>One injury, or the report form (<see cref="IsNew"/>).</summary>
	public class InjuryView
	{
		public RmsMemberInjury Injury { get; set; }
		public List<RmsInjuryDutyPeriod> Periods { get; set; } = new List<RmsInjuryDutyPeriod>();
		public bool IsHealthOfficer { get; set; }
		public bool IsSelf { get; set; }
		public string MemberName { get; set; }
		public string CallName { get; set; }
		public Department Department { get; set; }
		public bool ProtectedWithheld { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
		public bool IsNew => Injury == null || string.IsNullOrWhiteSpace(Injury.RmsMemberInjuryId);

		// Form fields (report and edit)
		public string InjuryId { get; set; }
		public string MemberUserId { get; set; }
		public string ExposureIncidentId { get; set; }
		public string OccurredOnLocal { get; set; }
		public int? CallId { get; set; }
		public string Location { get; set; }
		public string JobTitle { get; set; }
		public string Description { get; set; }
		public string InjuryDetails { get; set; }
		public string TreatmentDetails { get; set; }
		public bool IsRecordable { get; set; }
		public int OshaInjuryType { get; set; } = (int)OshaInjuryTypes.Injury;
		public bool IsPrivacyCase { get; set; }
		public bool TreatedInEmergencyRoom { get; set; }
		public bool HospitalizedOvernight { get; set; }
		public string DiedOn { get; set; }
		public long RowVersion { get; set; }
		public List<PersonName> Members { get; set; } = new List<PersonName>();
		public List<WellnessCallOption> Calls { get; set; } = new List<WellnessCallOption>();
	}

	/// <summary>Medical clearances: every member's current status (officers) or one member's history.</summary>
	public class ClearancesView
	{
		public List<RmsMedicalClearance> Current { get; set; } = new List<RmsMedicalClearance>();
		public string HistoryUserId { get; set; }
		public List<RmsMedicalClearance> History { get; set; } = new List<RmsMedicalClearance>();
		public List<PersonName> Members { get; set; } = new List<PersonName>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public bool ClearanceTypeConfigured { get; set; }
		public Department Department { get; set; }
		public bool ProtectedWithheld { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }

		public string NameOf(string userId) => MemberHealthPresentation.NameOf(Names, userId);
	}

	/// <summary>The OSHA 300/301 page: a year's log with its exports.</summary>
	public class OshaLogView
	{
		public int Year { get; set; }
		public List<int> Years { get; set; } = new List<int>();
		public OshaLog Log { get; set; } = new OshaLog();
		public string Error { get; set; }
	}

	/// <summary>The duty board on the personnel page (supervisors and health officers): operational status only.</summary>
	public class DutyBoardView
	{
		public List<MemberDutyStatus> Rows { get; set; } = new List<MemberDutyStatus>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Department Department { get; set; }

		public string NameOf(string userId) => MemberHealthPresentation.NameOf(Names, userId);
	}

	/// <summary>Localization keys and display helpers of the WL1 views (MemberHealth resx).</summary>
	public static class MemberHealthPresentation
	{
		public static string NameOf(IReadOnlyDictionary<string, string> names, string userId) =>
			!string.IsNullOrWhiteSpace(userId) && names != null && names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;

		public static string StageKey(int stage) => "Stage." + (ExposureIncidentWorkflow.IsKnownStage(stage) ? ((ExposureIncidentStages)stage).ToString() : "Reported");

		public static string RouteKey(int route) => "Route." + (MemberHealthRules.IsKnownRoute(route) ? ((ExposureIncidentRoutes)route).ToString() : "Other");

		public static string DutyKey(int status) => "Duty." + (MemberHealthRules.IsKnownDutyStatus(status) ? ((MemberDutyStatuses)status).ToString() : "FullDuty");

		public static string InjuryStateKey(int state) => "InjuryState." + (Enum.IsDefined(typeof(MemberInjuryStates), state) ? ((MemberInjuryStates)state).ToString() : "Open");

		public static string ClearanceKey(int status) => "Clearance." + (MemberHealthRules.IsKnownClearanceStatus(status) ? ((MedicalClearanceStatuses)status).ToString() : "Cleared");

		public static string OshaTypeKey(int type) => "Osha.Type." + (OshaRules.IsKnownInjuryType(type) ? ((OshaInjuryTypes)type).ToString() : "Injury");

		public static string BbpKey(BbpTrainingStates state) => "Bbp." + state;

		public static string FollowUpKindKey(string kind) => "FollowUpKind." + (ExposureCaseFollowUpKinds.IsKnown(kind) ? kind.Trim().ToLowerInvariant() : ExposureCaseFollowUpKinds.Other);

		public static string FollowUpStateKey(int state) => "FollowUpState." + (Enum.IsDefined(typeof(RmsWorkAssignmentState), state) ? ((RmsWorkAssignmentState)state).ToString() : "Open");

		/// <summary>A label class for a duty status badge.</summary>
		public static string DutyBadge(int status) => status switch
		{
			(int)MemberDutyStatuses.OffDuty => "label label-danger",
			(int)MemberDutyStatuses.RestrictedDuty => "label label-warning",
			_ => "label label-primary"
		};

		public static string ClearanceBadge(int status) => status switch
		{
			(int)MedicalClearanceStatuses.NotCleared => "label label-danger",
			(int)MedicalClearanceStatuses.ClearedWithRestrictions => "label label-warning",
			_ => "label label-primary"
		};

		/// <summary>A calendar date stored at midnight (duty periods, return dates, clearances) as a short date; no time zone shift.</summary>
		public static string CalendarDate(DateTime? value) => value.HasValue ? value.Value.ToString("d", CultureInfo.CurrentCulture) : string.Empty;

		public static string InputDate(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

		public static int JourneyIndex(int stage)
		{
			var journey = ExposureIncidentWorkflow.Journey.Select(s => (int)s).ToList();
			return journey.IndexOf(stage);
		}
	}
}
