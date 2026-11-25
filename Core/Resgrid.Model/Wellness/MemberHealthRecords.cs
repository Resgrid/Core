using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Resgrid.Model
{
	// Member wellness WL1 (protocols-training-knowledge-plan §13b): the bloodborne-pathogen / infectious-disease exposure case, injury and
	// return to duty with the OSHA 300/301 shapes, and the NFPA 1582 medical clearance. Same restricted tier as WL0: the member always reads
	// their own records, the department's designated officer or wellness officers keep them, department administration alone opens none.
	// Operational columns (member, call, dates, stage, duty status, clearance status, OSHA codes) stay plaintext; narratives and medical
	// details are ADP catalog 38 (MemberHealthProtectedFields). Nothing here names a patient: a BBP case points at the call only.

	/// <summary>Where a BBP / infectious-disease exposure case stands (Ryan White Part G workflow). Persisted: never renumber.</summary>
	public enum ExposureIncidentStages
	{
		/// <summary>Reported by the member or an officer; the designated officer was told.</summary>
		Reported = 0,

		/// <summary>The designated officer asked the receiving facility to follow up on the source patient (keyed to the call only).</summary>
		SourceFollowUpRequested = 1,

		/// <summary>The Ryan White notification request was sent; the facility owes an answer within 48 hours.</summary>
		NotificationRequested = 2,

		/// <summary>The facility's answer arrived.</summary>
		NotificationReceived = 3,

		/// <summary>Post-exposure follow-up (testing, prophylaxis review, counselling) is under way.</summary>
		FollowUp = 4,

		Closed = 5,

		/// <summary>Recorded in error. Kept for the retention period.</summary>
		Withdrawn = 6
	}

	/// <summary>How the member was exposed (operational, like the WL0 category). Persisted: never renumber.</summary>
	public enum ExposureIncidentRoutes
	{
		/// <summary>Needlestick or sharps injury.</summary>
		Percutaneous = 1,
		MucousMembrane = 2,
		NonIntactSkin = 3,
		/// <summary>Airborne infectious disease (tuberculosis, measles and the like).</summary>
		Airborne = 4,
		Other = 5
	}

	/// <summary>Post-exposure follow-up task kinds. Stored as the code in the Records work assignment's Note: never free text.</summary>
	public static class ExposureCaseFollowUpKinds
	{
		public const string BaselineTesting = "baseline_testing";
		public const string ProphylaxisReview = "prophylaxis_review";
		public const string SixWeekTest = "six_week_test";
		public const string ThreeMonthTest = "three_month_test";
		public const string SixMonthTest = "six_month_test";
		public const string Counselling = "counselling";
		public const string Other = "other";

		public static readonly IReadOnlyList<string> All = new[] { BaselineTesting, ProphylaxisReview, SixWeekTest, ThreeMonthTest, SixMonthTest, Counselling, Other };

		public static bool IsKnown(string kind) => kind != null && All.Contains(kind.Trim().ToLowerInvariant());
	}

	/// <summary>Where an injury record stands. Persisted: never renumber.</summary>
	public enum MemberInjuryStates
	{
		Open = 0,
		/// <summary>The member was cleared to return to full duty.</summary>
		Cleared = 1,
		/// <summary>Recorded in error. Kept for the retention period, never on the OSHA log.</summary>
		Voided = 2
	}

	/// <summary>A member's duty status (operational: supervisors and staffing see it, never why). Persisted: never renumber.</summary>
	public enum MemberDutyStatuses
	{
		FullDuty = 0,
		RestrictedDuty = 1,
		OffDuty = 2
	}

	/// <summary>OSHA Form 300 column M (injury or illness type). Persisted: never renumber.</summary>
	public enum OshaInjuryTypes
	{
		Injury = 1,
		SkinDisorder = 2,
		RespiratoryCondition = 3,
		Poisoning = 4,
		HearingLoss = 5,
		AllOtherIllnesses = 6
	}

	/// <summary>OSHA Form 300 columns G-J: the most serious outcome of the case (derived, never stored).</summary>
	public enum OshaCaseOutcomes
	{
		Death = 1,
		DaysAwayFromWork = 2,
		JobTransferOrRestriction = 3,
		OtherRecordable = 4
	}

	/// <summary>The NFPA 1582 restriction status of a medical clearance (operational). Persisted: never renumber.</summary>
	public enum MedicalClearanceStatuses
	{
		Cleared = 1,
		ClearedWithRestrictions = 2,
		NotCleared = 3
	}

	/// <summary>The member's bloodborne-pathogen training status shown on each exposure case (§13b.3).</summary>
	public enum BbpTrainingStates
	{
		/// <summary>The department has not named its BBP training certification type in the wellness settings.</summary>
		NotTracked = 0,
		Current = 1,
		ExpiringSoon = 2,
		Expired = 3,
		/// <summary>The member holds no record of the type.</summary>
		None = 4
	}

	/// <summary>
	/// A BBP / infectious-disease exposure case (§13b WL1): the designated-officer workflow from report through the source follow-up request,
	/// Ryan White notification tracking and post-exposure follow-up to closure. It hangs off a WL0 exposure entry and points at the call; it
	/// never holds patient identity (no name, record number or date of birth): the receiving facility knows the source patient from the call.
	/// Kept at least 30 years (OSHA 29 CFR 1910.1020) and longer under a legal hold.
	/// </summary>
	public class RmsExposureIncident : IEntity
	{
		public string RmsExposureIncidentId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The exposed member (operational).</summary>
		public string UserId { get; set; }

		/// <summary>The WL0 entry this case follows (category Bloodborne pathogen, or Other for an airborne disease).</summary>
		public string ResponderExposureId { get; set; }

		/// <summary>The call it happened on (operational). The only link to the source patient.</summary>
		public int? CallId { get; set; }

		/// <summary><see cref="ExposureIncidentRoutes"/> (operational).</summary>
		public int Route { get; set; }

		/// <summary>When it happened, UTC (operational).</summary>
		public DateTime OccurredOn { get; set; }

		/// <summary><see cref="ExposureIncidentStages"/>.</summary>
		public int Stage { get; set; }

		public DateTime ReportedOn { get; set; }

		public string ReportedByUserId { get; set; }

		/// <summary>The receiving medical facility asked to follow up (operational: a destination, never a person).</summary>
		public string SourceFacility { get; set; }

		public DateTime? SourceRequestedOn { get; set; }

		/// <summary>When the Ryan White notification request was sent.</summary>
		public DateTime? NotificationRequestedOn { get; set; }

		/// <summary>When the facility's answer is due (48 hours after the request).</summary>
		public DateTime? NotificationDueOn { get; set; }

		public DateTime? NotificationReceivedOn { get; set; }

		/// <summary>When worker 91 told the designated officers the answer is overdue (the claim column).</summary>
		public DateTime? NotificationOverdueNoticeOn { get; set; }

		public DateTime? FollowUpStartedOn { get; set; }

		public DateTime? ClosedOn { get; set; }

		public string ClosedByUserId { get; set; }

		public DateTime? WithdrawnOn { get; set; }

		public string WithdrawnByUserId { get; set; }

		/// <summary>Protected: what the facility was asked and said about the source follow-up.</summary>
		public string SourceFollowUpNotes { get; set; }

		/// <summary>Protected: the Ryan White notification answer.</summary>
		public string NotificationResponse { get; set; }

		/// <summary>Protected: testing, prophylaxis and other medical follow-up notes.</summary>
		public string MedicalNotes { get; set; }

		/// <summary>Protected: the closing summary.</summary>
		public string ClosureNotes { get; set; }

		/// <summary>ADP row marker (catalog 38).</summary>
		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime ModifiedOn { get; set; }

		public string ModifiedByUserId { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		public bool IsOpen => Stage != (int)ExposureIncidentStages.Closed && Stage != (int)ExposureIncidentStages.Withdrawn;

		[NotMapped]
		public object IdValue { get => RmsExposureIncidentId; set => RmsExposureIncidentId = value?.ToString(); }

		[NotMapped]
		public string TableName => "RmsExposureIncidents";

		[NotMapped]
		public string IdName => "RmsExposureIncidentId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	/// <summary>
	/// An injury to a member (§13b WL1): duty status with dates, restrictions and the return-to-duty clearance, plus the OSHA 300/301 facts.
	/// Supervisors and staffing see the duty status only (<see cref="MemberDutyStatus"/>); the record itself is in the restricted tier. Kept
	/// at least five years after the end of the year it happened (OSHA 29 CFR 1904.33) and longer under a legal hold.
	/// </summary>
	public class RmsMemberInjury : IEntity
	{
		public string RmsMemberInjuryId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public int? CallId { get; set; }

		/// <summary>The BBP case this injury was recorded from (a contaminated sharps injury is an OSHA privacy case).</summary>
		public string ExposureIncidentId { get; set; }

		public DateTime OccurredOn { get; set; }

		public DateTime ReportedOn { get; set; }

		public string ReportedByUserId { get; set; }

		/// <summary>The department-local year of the injury: the OSHA log it belongs to.</summary>
		public int OshaYear { get; set; }

		/// <summary>The case number within the year (OSHA 300 column A), assigned on report.</summary>
		public int OshaCaseNumber { get; set; }

		/// <summary>Where the event occurred (operational; OSHA 300 column E).</summary>
		public string Location { get; set; }

		/// <summary>The member's job title at the time (OSHA 300 column C).</summary>
		public string JobTitle { get; set; }

		/// <summary><see cref="MemberInjuryStates"/>.</summary>
		public int State { get; set; }

		/// <summary>The current <see cref="MemberDutyStatuses"/> (operational; the open duty period's status).</summary>
		public int DutyStatus { get; set; }

		public DateTime? DutyStatusSince { get; set; }

		/// <summary>When the member is expected back (operational); worker 91 asks for a review once it passes.</summary>
		public DateTime? ExpectedReturnOn { get; set; }

		/// <summary>When worker 91 last asked for a return-to-duty review (the claim column).</summary>
		public DateTime? ReturnReviewNoticeOn { get; set; }

		public DateTime? ReturnedToDutyOn { get; set; }

		public DateTime? ClearedOn { get; set; }

		public string ClearedByUserId { get; set; }

		/// <summary>An OSHA-recordable case (it goes on the 300 log and gets a 301 report).</summary>
		public bool IsRecordable { get; set; }

		/// <summary><see cref="OshaInjuryTypes"/>.</summary>
		public int OshaInjuryType { get; set; }

		/// <summary>OSHA 29 CFR 1904.29(b)(7): the name is withheld from the 300 log.</summary>
		public bool IsPrivacyCase { get; set; }

		public bool TreatedInEmergencyRoom { get; set; }

		public bool HospitalizedOvernight { get; set; }

		public DateTime? DiedOn { get; set; }

		/// <summary>Protected: what the member was doing and what happened.</summary>
		public string Description { get; set; }

		/// <summary>Protected: the injury or illness, the part of the body and what harmed the member.</summary>
		public string InjuryDetails { get; set; }

		/// <summary>Protected: the current work restrictions.</summary>
		public string Restrictions { get; set; }

		/// <summary>Protected: treating physician, facility and treatment.</summary>
		public string TreatmentDetails { get; set; }

		/// <summary>Protected: the return-to-duty clearance notes.</summary>
		public string ClearanceNotes { get; set; }

		public DateTime? VoidedOn { get; set; }

		public string VoidedByUserId { get; set; }

		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime ModifiedOn { get; set; }

		public string ModifiedByUserId { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		public bool IsOpen => State == (int)MemberInjuryStates.Open;

		[NotMapped]
		public object IdValue { get => RmsMemberInjuryId; set => RmsMemberInjuryId = value?.ToString(); }

		[NotMapped]
		public string TableName => "RmsMemberInjuries";

		[NotMapped]
		public string IdName => "RmsMemberInjuryId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	/// <summary>
	/// One stretch of a duty status on an injury (operational): the history the OSHA day counts come from. Dates are department-local
	/// calendar dates; <see cref="EndsOn"/> is exclusive and null while the stretch is open.
	/// </summary>
	public class RmsInjuryDutyPeriod : IEntity
	{
		public string RmsInjuryDutyPeriodId { get; set; }

		public int DepartmentId { get; set; }

		public string InjuryId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="MemberDutyStatuses"/>.</summary>
		public int DutyStatus { get; set; }

		public DateTime StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		public object IdValue { get => RmsInjuryDutyPeriodId; set => RmsInjuryDutyPeriodId = value?.ToString(); }

		[NotMapped]
		public string TableName => "RmsInjuryDutyPeriods";

		[NotMapped]
		public string IdName => "RmsInjuryDutyPeriodId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// An NFPA 1582 medical clearance (§13b WL1): the evaluation is a certification of the department's "Medical clearance (NFPA 1582)" type
	/// (when one is named in the wellness settings) and this row carries its restriction status. The status is operational, so officers and
	/// staffing see Cleared / Cleared with restrictions / Not cleared; the restriction detail is ADP-protected.
	/// </summary>
	public class RmsMedicalClearance : IEntity
	{
		public string RmsMedicalClearanceId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary>The certification record of the evaluation, when the department names a clearance certification type.</summary>
		public int? PersonnelCertificationId { get; set; }

		/// <summary><see cref="MedicalClearanceStatuses"/> (operational).</summary>
		public int Status { get; set; }

		public DateTime EvaluatedOn { get; set; }

		/// <summary>When the next evaluation is due (operational).</summary>
		public DateTime? ValidUntil { get; set; }

		/// <summary>Protected: the restrictions and their reasons.</summary>
		public string RestrictionDetail { get; set; }

		public DateTime? VoidedOn { get; set; }

		public string VoidedByUserId { get; set; }

		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime ModifiedOn { get; set; }

		public string ModifiedByUserId { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		public bool IsVoided => VoidedOn.HasValue;

		[NotMapped]
		public object IdValue { get => RmsMedicalClearanceId; set => RmsMedicalClearanceId = value?.ToString(); }

		[NotMapped]
		public string TableName => "RmsMedicalClearances";

		[NotMapped]
		public string IdName => "RmsMedicalClearanceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsVoided" };
	}

	/// <summary>
	/// ADP catalog 38 (§13b WL1): the narratives and medical details of an exposure case, an injury and a medical clearance, Personnel family.
	/// Member, call, dates, stage, duty status, clearance status and the OSHA codes are operational and never cataloged (§25.4 item 4c).
	/// </summary>
	public static class MemberHealthProtectedFields
	{
		public const int CatalogVersion = 38;
		public const string Family = "Personnel";

		public static readonly IReadOnlyDictionary<string, (Func<RmsExposureIncident, string> Get, Action<RmsExposureIncident, string> Set)> ExposureIncidents =
			new Dictionary<string, (Func<RmsExposureIncident, string>, Action<RmsExposureIncident, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["rmsexposureincidents.sourcefollowupnotes"] = (e => e.SourceFollowUpNotes, (e, v) => e.SourceFollowUpNotes = v),
				["rmsexposureincidents.notificationresponse"] = (e => e.NotificationResponse, (e, v) => e.NotificationResponse = v),
				["rmsexposureincidents.medicalnotes"] = (e => e.MedicalNotes, (e, v) => e.MedicalNotes = v),
				["rmsexposureincidents.closurenotes"] = (e => e.ClosureNotes, (e, v) => e.ClosureNotes = v)
			};

		public static readonly IReadOnlyDictionary<string, (Func<RmsMemberInjury, string> Get, Action<RmsMemberInjury, string> Set)> Injuries =
			new Dictionary<string, (Func<RmsMemberInjury, string>, Action<RmsMemberInjury, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["rmsmemberinjuries.description"] = (i => i.Description, (i, v) => i.Description = v),
				["rmsmemberinjuries.injurydetails"] = (i => i.InjuryDetails, (i, v) => i.InjuryDetails = v),
				["rmsmemberinjuries.restrictions"] = (i => i.Restrictions, (i, v) => i.Restrictions = v),
				["rmsmemberinjuries.treatmentdetails"] = (i => i.TreatmentDetails, (i, v) => i.TreatmentDetails = v),
				["rmsmemberinjuries.clearancenotes"] = (i => i.ClearanceNotes, (i, v) => i.ClearanceNotes = v)
			};

		public static readonly IReadOnlyDictionary<string, (Func<RmsMedicalClearance, string> Get, Action<RmsMedicalClearance, string> Set)> Clearances =
			new Dictionary<string, (Func<RmsMedicalClearance, string>, Action<RmsMedicalClearance, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["rmsmedicalclearances.restrictiondetail"] = (c => c.RestrictionDetail, (c, v) => c.RestrictionDetail = v)
			};

		/// <summary>(table, key column, column, classification) for the catalog registration and the table bindings.</summary>
		public static IEnumerable<(string Table, string Column, string Classification)> All()
		{
			yield return ("RmsExposureIncidents", "SourceFollowUpNotes", "Phi");
			yield return ("RmsExposureIncidents", "NotificationResponse", "Phi");
			yield return ("RmsExposureIncidents", "MedicalNotes", "Phi");
			yield return ("RmsExposureIncidents", "ClosureNotes", "Phi");
			yield return ("RmsMemberInjuries", "Description", "Phi");
			yield return ("RmsMemberInjuries", "InjuryDetails", "Phi");
			yield return ("RmsMemberInjuries", "Restrictions", "Phi");
			yield return ("RmsMemberInjuries", "TreatmentDetails", "Phi");
			yield return ("RmsMemberInjuries", "ClearanceNotes", "Phi");
			yield return ("RmsMedicalClearances", "RestrictionDetail", "Phi");
		}

		/// <summary>The primary key column of each protected table.</summary>
		public static string KeyColumn(string table) => table switch
		{
			"RmsExposureIncidents" => "RmsExposureIncidentId",
			"RmsMemberInjuries" => "RmsMemberInjuryId",
			"RmsMedicalClearances" => "RmsMedicalClearanceId",
			_ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not a member health table.")
		};

		/// <summary>Operational columns that must never be cataloged: rules, staffing and the OSHA counts read them without a grant.</summary>
		public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> OperationalColumns = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
		{
			["RmsExposureIncidents"] = new[] { "UserId", "CallId", "Route", "OccurredOn", "Stage", "SourceFacility", "NotificationRequestedOn", "NotificationReceivedOn" },
			["RmsMemberInjuries"] = new[] { "UserId", "CallId", "OccurredOn", "DutyStatus", "DutyStatusSince", "ExpectedReturnOn", "ReturnedToDutyOn", "State", "IsRecordable", "OshaInjuryType", "IsPrivacyCase", "Location", "JobTitle" },
			["RmsMedicalClearances"] = new[] { "UserId", "Status", "EvaluatedOn", "ValidUntil" }
		};
	}

	/// <summary>The BBP case state machine (pure). Every transition the service makes is one of these; nothing else moves a case.</summary>
	public static class ExposureIncidentWorkflow
	{
		/// <summary>Ryan White Part G: the medical facility answers a request within 48 hours.</summary>
		public static readonly TimeSpan NotificationResponseWindow = TimeSpan.FromHours(48);

		private static readonly IReadOnlyDictionary<ExposureIncidentStages, ExposureIncidentStages[]> Moves = new Dictionary<ExposureIncidentStages, ExposureIncidentStages[]>
		{
			[ExposureIncidentStages.Reported] = new[] { ExposureIncidentStages.SourceFollowUpRequested, ExposureIncidentStages.NotificationRequested, ExposureIncidentStages.FollowUp, ExposureIncidentStages.Withdrawn },
			[ExposureIncidentStages.SourceFollowUpRequested] = new[] { ExposureIncidentStages.NotificationRequested, ExposureIncidentStages.FollowUp, ExposureIncidentStages.Withdrawn },
			// No answer in time: the officer may start the follow-up anyway (and the overdue notice says so).
			[ExposureIncidentStages.NotificationRequested] = new[] { ExposureIncidentStages.NotificationReceived, ExposureIncidentStages.FollowUp, ExposureIncidentStages.Withdrawn },
			[ExposureIncidentStages.NotificationReceived] = new[] { ExposureIncidentStages.FollowUp, ExposureIncidentStages.Closed, ExposureIncidentStages.Withdrawn },
			[ExposureIncidentStages.FollowUp] = new[] { ExposureIncidentStages.Closed, ExposureIncidentStages.Withdrawn },
			// A closed case reopens into follow-up (a late result); a withdrawn one never moves again.
			[ExposureIncidentStages.Closed] = new[] { ExposureIncidentStages.FollowUp },
			[ExposureIncidentStages.Withdrawn] = Array.Empty<ExposureIncidentStages>()
		};

		public static bool IsKnownStage(int stage) => Enum.IsDefined(typeof(ExposureIncidentStages), stage);

		public static bool CanMove(int from, int to) =>
			IsKnownStage(from) && IsKnownStage(to) && Moves[(ExposureIncidentStages)from].Contains((ExposureIncidentStages)to);

		public static IReadOnlyList<ExposureIncidentStages> NextStages(int from) =>
			IsKnownStage(from) ? Moves[(ExposureIncidentStages)from] : Array.Empty<ExposureIncidentStages>();

		/// <summary>The stages shown as the case journey, in order (Withdrawn is not a step).</summary>
		public static readonly IReadOnlyList<ExposureIncidentStages> Journey = new[]
		{
			ExposureIncidentStages.Reported, ExposureIncidentStages.SourceFollowUpRequested, ExposureIncidentStages.NotificationRequested,
			ExposureIncidentStages.NotificationReceived, ExposureIncidentStages.FollowUp, ExposureIncidentStages.Closed
		};

		/// <summary>A WL0 entry a case may follow: confirmed, and bloodborne (or Other, for an airborne disease).</summary>
		public static bool CanOpenFrom(RmsResponderExposure entry) =>
			entry != null && entry.Counts && (entry.Category == (int)ResponderExposureCategories.BloodbornePathogen || entry.Category == (int)ResponderExposureCategories.Other);

		/// <summary>The WL0 category a newly reported exposure is logged under.</summary>
		public static int CategoryFor(int route) => route == (int)ExposureIncidentRoutes.Airborne ? (int)ResponderExposureCategories.Other : (int)ResponderExposureCategories.BloodbornePathogen;

		public static bool IsNotificationOverdue(RmsExposureIncident incident, DateTime utcNow) =>
			incident != null && incident.Stage == (int)ExposureIncidentStages.NotificationRequested && incident.NotificationDueOn.HasValue && incident.NotificationDueOn.Value <= utcNow;
	}

	/// <summary>OSHA 300/301 rules (pure): day counts, the outcome column, case numbers and the privacy-case name.</summary>
	public static class OshaRules
	{
		/// <summary>OSHA 29 CFR 1904.7(b)(3)(vii): stop counting once days away and restricted together reach 180.</summary>
		public const int DayCap = 180;

		/// <summary>OSHA 29 CFR 1904.33: kept five years after the end of the calendar year the records cover.</summary>
		public const int MinimumRetentionYears = 5;
		public const int MaximumRetentionYears = 100;

		/// <summary>The text that replaces the employee's name on the 300 log for a privacy case (1904.29(b)(6)).</summary>
		public const string PrivacyCaseName = "Privacy case";

		/// <summary>
		/// Calendar days away from work and on restricted duty, from the duty periods, not counting the day of the injury, up to
		/// <paramref name="asOf"/> (inclusive) for a stretch still open, capped at 180 together (days away counted first).
		/// </summary>
		public static (int DaysAway, int DaysRestricted) CountDays(DateTime occurredOnLocal, IEnumerable<RmsInjuryDutyPeriod> periods, DateTime asOfLocal)
		{
			var injuryDay = occurredOnLocal.Date;
			var lastCounted = asOfLocal.Date;
			var away = new HashSet<DateTime>();
			var restricted = new HashSet<DateTime>();
			foreach (var period in (periods ?? Enumerable.Empty<RmsInjuryDutyPeriod>()).Where(p => p != null && p.DutyStatus != (int)MemberDutyStatuses.FullDuty))
			{
				var start = period.StartsOn.Date > injuryDay ? period.StartsOn.Date : injuryDay.AddDays(1);
				var endExclusive = period.EndsOn.HasValue ? period.EndsOn.Value.Date : lastCounted.AddDays(1);
				if (endExclusive > lastCounted.AddDays(1))
					endExclusive = lastCounted.AddDays(1);
				for (var day = start; day < endExclusive; day = day.AddDays(1))
				{
					if (period.DutyStatus == (int)MemberDutyStatuses.OffDuty)
						away.Add(day);
					else
						restricted.Add(day);
				}
			}

			// A day both away and restricted (overlapping stretches) counts once, as away.
			restricted.ExceptWith(away);
			var daysAway = Math.Min(DayCap, away.Count);
			var daysRestricted = Math.Min(DayCap - daysAway, restricted.Count);
			return (daysAway, daysRestricted);
		}

		/// <summary>The most serious outcome: death, then days away, then job transfer or restriction, then other recordable.</summary>
		public static OshaCaseOutcomes Outcome(RmsMemberInjury injury, int daysAway, int daysRestricted)
		{
			if (injury?.DiedOn != null)
				return OshaCaseOutcomes.Death;
			if (daysAway > 0)
				return OshaCaseOutcomes.DaysAwayFromWork;
			if (daysRestricted > 0)
				return OshaCaseOutcomes.JobTransferOrRestriction;
			return OshaCaseOutcomes.OtherRecordable;
		}

		public static string CaseNumber(int year, int number) => year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) + "-" + number.ToString("000", System.Globalization.CultureInfo.InvariantCulture);

		/// <summary>The name printed on the 300 log: "Privacy case" for a privacy case, the member's name otherwise.</summary>
		public static string LogName(RmsMemberInjury injury, string memberName) =>
			injury != null && injury.IsPrivacyCase ? PrivacyCaseName : memberName;

		public static bool IsKnownInjuryType(int type) => Enum.IsDefined(typeof(OshaInjuryTypes), type);

		/// <summary>The injuries on a year's log: recordable, not voided, of that year.</summary>
		public static IEnumerable<RmsMemberInjury> OnLog(IEnumerable<RmsMemberInjury> injuries, int year) =>
			(injuries ?? Enumerable.Empty<RmsMemberInjury>()).Where(i => i != null && i.IsRecordable && i.State != (int)MemberInjuryStates.Voided && i.OshaYear == year)
				.OrderBy(i => i.OshaCaseNumber).ThenBy(i => i.OccurredOn);

		/// <summary>True once an injury record is past its retention: the end of its year plus the retention years.</summary>
		public static bool IsPastRetention(RmsMemberInjury injury, int retentionYears, DateTime utcNow)
		{
			if (injury == null)
				return false;
			var years = Math.Min(MaximumRetentionYears, Math.Max(MinimumRetentionYears, retentionYears));
			var endOfYear = new DateTime(Math.Max(1, injury.OshaYear) + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			return endOfYear.AddYears(years) < utcNow;
		}
	}

	/// <summary>Limits and pure rules of the WL1 records.</summary>
	public static class MemberHealthRules
	{
		public const int NotesMax = 4000;
		public const int FacilityMax = 200;
		public const int LocationMax = 200;
		public const int JobTitleMax = 100;

		/// <summary>BBP training expiring within this many days shows as "expiring soon" on the case.</summary>
		public const int BbpExpiringSoonDays = 30;

		/// <summary>The legal-hold definition keys each record answers to (a department-wide hold covers them too).</summary>
		public const string ExposureIncidentHoldKey = "exposure-incident";
		public const string InjuryHoldKey = "member-injury";
		public const string ClearanceHoldKey = "medical-clearance";

		public static bool IsKnownRoute(int route) => Enum.IsDefined(typeof(ExposureIncidentRoutes), route);

		public static bool IsKnownDutyStatus(int status) => Enum.IsDefined(typeof(MemberDutyStatuses), status);

		public static bool IsKnownClearanceStatus(int status) => Enum.IsDefined(typeof(MedicalClearanceStatuses), status);

		/// <summary>The BBP training status from the member's records of the department's BBP certification type.</summary>
		public static BbpTrainingStatus BbpStatus(IEnumerable<PersonnelCertification> records, DepartmentCertificationType type, DateTime onDate)
		{
			if (type == null)
				return new BbpTrainingStatus { State = BbpTrainingStates.NotTracked };

			var mine = (records ?? Enumerable.Empty<PersonnelCertification>())
				.Where(r => r != null && !r.IsDeleted && r.DepartmentCertificationTypeId == type.DepartmentCertificationTypeId)
				.ToList();
			var status = new BbpTrainingStatus { TypeName = type.Type };
			if (mine.Count == 0)
			{
				status.State = BbpTrainingStates.None;
				return status;
			}

			var valid = mine.Where(r => Certifications.CertificationRequirementEvaluator.IsRecordValid(r, type, onDate, false, false))
				.OrderByDescending(r => r.ExpiresOn ?? DateTime.MaxValue).FirstOrDefault();
			if (valid == null)
			{
				status.State = BbpTrainingStates.Expired;
				status.ExpiresOn = mine.Where(r => r.ExpiresOn.HasValue).Select(r => r.ExpiresOn).OrderByDescending(d => d).FirstOrDefault();
				return status;
			}

			status.ExpiresOn = valid.ExpiresOn;
			status.State = valid.ExpiresOn.HasValue && valid.ExpiresOn.Value.Date <= onDate.Date.AddDays(BbpExpiringSoonDays)
				? BbpTrainingStates.ExpiringSoon
				: BbpTrainingStates.Current;
			return status;
		}

		/// <summary>The member's current clearance: the latest evaluation that was not voided.</summary>
		public static RmsMedicalClearance Current(IEnumerable<RmsMedicalClearance> clearances) =>
			(clearances ?? Enumerable.Empty<RmsMedicalClearance>()).Where(c => c != null && !c.IsVoided)
				.OrderByDescending(c => c.EvaluatedOn).ThenByDescending(c => c.CreatedOn).FirstOrDefault();

		/// <summary>The most restrictive duty status among a member's open injuries (full duty when none is open).</summary>
		public static MemberDutyStatuses DutyStatusOf(IEnumerable<RmsMemberInjury> injuries) =>
			(injuries ?? Enumerable.Empty<RmsMemberInjury>()).Where(i => i != null && i.IsOpen && IsKnownDutyStatus(i.DutyStatus))
				.Select(i => (MemberDutyStatuses)i.DutyStatus).DefaultIfEmpty(MemberDutyStatuses.FullDuty).Max();
	}

	/// <summary>The BBP training status line on an exposure case.</summary>
	public class BbpTrainingStatus
	{
		public BbpTrainingStates State { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string TypeName { get; set; }
	}

	/// <summary>
	/// What supervisors and staffing see about a member (operational only): duty status with dates and the medical clearance status. Never
	/// why: no injury, restriction or clearance detail is ever in it.
	/// </summary>
	public class MemberDutyStatus
	{
		public string UserId { get; set; }
		public MemberDutyStatuses DutyStatus { get; set; }
		public DateTime? DutyStatusSince { get; set; }
		public DateTime? ExpectedReturnOn { get; set; }
		/// <summary>Null when the member has no clearance on record.</summary>
		public MedicalClearanceStatuses? ClearanceStatus { get; set; }
		public DateTime? ClearanceValidUntil { get; set; }
	}

	/// <summary>Who the caller is in the WL1 tier (the web shell and the v4 controller read it).</summary>
	public class MemberHealthAccess
	{
		public bool IsMember { get; set; }
		public bool IsWellnessOfficer { get; set; }
		public bool IsDesignatedOfficer { get; set; }
		public bool IsAdmin { get; set; }
		/// <summary>A department or group administrator: sees duty status and clearance status only.</summary>
		public bool IsSupervisor { get; set; }
		public bool IsHealthOfficer => IsWellnessOfficer || IsDesignatedOfficer;
	}

	/// <summary>A BBP / infectious-disease exposure report: from an existing WL0 entry (<see cref="ResponderExposureId"/>) or a new one.</summary>
	public class ExposureIncidentReport
	{
		public string UserId { get; set; }
		public string ResponderExposureId { get; set; }
		public int? CallId { get; set; }
		public int Route { get; set; }
		public DateTime OccurredOn { get; set; }
		/// <summary>New entries only: the WL0 protected narrative and PPE (never patient identity).</summary>
		public string Narrative { get; set; }
		public string PpeWorn { get; set; }
	}

	/// <summary>One step of the BBP case workflow, with the date and the step's (protected) notes.</summary>
	public class ExposureIncidentStep
	{
		public int ToStage { get; set; }
		/// <summary>When it happened (UTC); now when omitted.</summary>
		public DateTime? On { get; set; }
		public string SourceFacility { get; set; }
		public string Notes { get; set; }
		public long? ExpectedRowVersion { get; set; }
	}

	/// <summary>What a member or an officer types when reporting or editing an injury.</summary>
	public class MemberInjuryInput
	{
		public string UserId { get; set; }
		public int? CallId { get; set; }
		public string ExposureIncidentId { get; set; }
		public DateTime OccurredOn { get; set; }
		public string Location { get; set; }
		public string JobTitle { get; set; }
		public string Description { get; set; }
		public string InjuryDetails { get; set; }
		public string TreatmentDetails { get; set; }
		/// <summary>Officer only: the OSHA facts.</summary>
		public bool IsRecordable { get; set; }
		public int OshaInjuryType { get; set; } = (int)OshaInjuryTypes.Injury;
		public bool IsPrivacyCase { get; set; }
		public bool TreatedInEmergencyRoom { get; set; }
		public bool HospitalizedOvernight { get; set; }
		public DateTime? DiedOn { get; set; }
	}

	/// <summary>What an officer records for a medical clearance.</summary>
	public class MedicalClearanceInput
	{
		public string UserId { get; set; }
		public int Status { get; set; }
		public DateTime EvaluatedOn { get; set; }
		public DateTime? ValidUntil { get; set; }
		public string RestrictionDetail { get; set; }
	}

	/// <summary>One row of the OSHA 300 log (columns A-M), the name already masked for a privacy case.</summary>
	public class OshaLogRow
	{
		public string InjuryId { get; set; }
		public string CaseNumber { get; set; }
		public string EmployeeName { get; set; }
		public string JobTitle { get; set; }
		public DateTime DateOfInjury { get; set; }
		public string Location { get; set; }
		public string Description { get; set; }
		public OshaCaseOutcomes Outcome { get; set; }
		public int DaysAway { get; set; }
		public int DaysRestricted { get; set; }
		public OshaInjuryTypes InjuryType { get; set; }
		public bool IsPrivacyCase { get; set; }
	}

	/// <summary>One OSHA 301 incident report (the facts Resgrid holds; employee home details are completed outside Resgrid).</summary>
	public class Osha301Report
	{
		public string CaseNumber { get; set; }
		public string EmployeeName { get; set; }
		public string JobTitle { get; set; }
		public DateTime DateOfInjury { get; set; }
		public string TimeOfEvent { get; set; }
		public string Location { get; set; }
		public string WhatHappened { get; set; }
		public string InjuryDetails { get; set; }
		public string TreatmentDetails { get; set; }
		public bool TreatedInEmergencyRoom { get; set; }
		public bool HospitalizedOvernight { get; set; }
		public DateTime? DiedOn { get; set; }
		public bool IsPrivacyCase { get; set; }
	}

	/// <summary>A year's OSHA log: the rows and the 300A-style totals.</summary>
	public class OshaLog
	{
		public int Year { get; set; }
		public string EstablishmentName { get; set; }
		public List<OshaLogRow> Rows { get; set; } = new List<OshaLogRow>();
		public List<Osha301Report> Reports { get; set; } = new List<Osha301Report>();
		public bool ProtectedWithheld { get; set; }

		public int Count(OshaCaseOutcomes outcome) => Rows.Count(r => r.Outcome == outcome);
		public int Count(OshaInjuryTypes type) => Rows.Count(r => r.InjuryType == type);
		public int TotalDaysAway => Rows.Sum(r => r.DaysAway);
		public int TotalDaysRestricted => Rows.Sum(r => r.DaysRestricted);
	}
}
