using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// What a member was exposed to (protocols-training-knowledge-plan §13b, WL0). Persisted: never renumber. The category is
	/// operational (§25.4 item 4c): a nightly rule counts it without a Protected Data grant.
	/// </summary>
	public enum ResponderExposureCategories
	{
		Carcinogen = 1,
		BloodbornePathogen = 2,
		HazardousSubstance = 3,
		/// <summary>A potentially traumatic call. Distinct from <see cref="Call.IsCritical"/>, which is dispatch urgency.</summary>
		CriticalIncident = 4,
		Other = 5
	}

	/// <summary>Where an exposure entry stands. Persisted: never renumber.</summary>
	public enum ResponderExposureStatuses
	{
		/// <summary>Built from dispatch data after a flagged call; counts for nothing until the member or a wellness officer confirms it.</summary>
		Suggested = 0,
		Confirmed = 1,
		/// <summary>A suggestion the member or a wellness officer turned down; kept so the call never suggests it again.</summary>
		Dismissed = 2,
		/// <summary>A confirmed entry recorded in error. Kept for the retention period, never counted.</summary>
		Voided = 3
	}

	/// <summary>How an exposure entry was created. Persisted: never renumber.</summary>
	public enum ResponderExposureSources
	{
		Manual = 0,
		PostCallSuggestion = 1
	}

	/// <summary>The dispatch facts a suggestion was built from (operational, comma-separated on the row).</summary>
	public static class ResponderExposureSuggestionBases
	{
		public const string Status = "status";
		public const string UnitCrew = "unit_crew";
		public const string CheckIn = "check_in";
		public const string UnitResponse = "unit_response";
	}

	/// <summary>
	/// The kind of a screening follow-up task. Stored as the code in the Records work assignment's Note, so no health detail is
	/// ever written to that plaintext column; anything specific belongs in the screening flag's protected plan.
	/// </summary>
	public static class ResponderFollowUpTaskKinds
	{
		public const string ScreeningAppointment = "screening_appointment";
		public const string BaselineTest = "baseline_test";
		public const string MedicalReview = "medical_review";
		public const string WellnessCheckIn = "wellness_check_in";
		public const string Other = "other";

		public static readonly IReadOnlyList<string> All = new[] { ScreeningAppointment, BaselineTest, MedicalReview, WellnessCheckIn, Other };

		public static bool IsKnown(string kind) => kind != null && All.Contains(kind.Trim().ToLowerInvariant());
	}

	/// <summary>
	/// One exposure or critical-incident entry for one member (§13b WL0; RMS plan §5.2 <c>RmsResponderExposure</c>). It is a
	/// different concept from <see cref="RmsExposure"/>, which is NERIS property exposure (a building the fire spread to).
	/// <para>
	/// Operational columns (member, call, category, date, duration, status) stay plaintext so nightly rules count events and send
	/// anniversary reminders without a grant (§25.4 item 4c). Substance, narrative, PPE, decontamination and health details are
	/// ADP catalog 37 (<see cref="WellnessProtectedFields"/>). The whole row is in the restricted tier: the member and the
	/// department's wellness officers read it; department administration alone does not. Kept at least 30 years (OSHA 29 CFR
	/// 1910.1020) and longer under a legal hold; never in Unified Search, the AI corpus or a department export.
	/// </para>
	/// </summary>
	public class RmsResponderExposure : IEntity
	{
		public string RmsResponderExposureId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The member the entry is about (operational).</summary>
		public string UserId { get; set; }

		/// <summary>The call it happened on, when there was one (operational).</summary>
		public int? CallId { get; set; }

		/// <summary><see cref="ResponderExposureCategories"/> (operational).</summary>
		public int Category { get; set; }

		/// <summary>When it happened, UTC (operational).</summary>
		public DateTime OccurredOn { get; set; }

		/// <summary>How long, in minutes (operational; a suggestion takes it from the dispatch timeline).</summary>
		public int? DurationMinutes { get; set; }

		/// <summary><see cref="ResponderExposureStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary><see cref="ResponderExposureSources"/>.</summary>
		public int Source { get; set; }

		/// <summary>The <see cref="RmsCallExposureFlag"/> a suggestion came from.</summary>
		public string CallExposureFlagId { get; set; }

		/// <summary><see cref="ResponderExposureSuggestionBases"/> codes, comma-separated (operational).</summary>
		public string SuggestionBasis { get; set; }

		/// <summary>Protected: what the member was exposed to.</summary>
		public string Substance { get; set; }

		/// <summary>Protected: what happened, in the member's or officer's words.</summary>
		public string Narrative { get; set; }

		/// <summary>Protected: the protective equipment worn.</summary>
		public string PpeWorn { get; set; }

		/// <summary>Protected: decontamination done afterwards.</summary>
		public string Decontamination { get; set; }

		/// <summary>Protected: symptoms, treatment or other health details.</summary>
		public string HealthDetails { get; set; }

		/// <summary>Anniversary reminders to the member (opt-in, critical incidents only).</summary>
		public bool RemindMember { get; set; }

		/// <summary>Anniversary reminders to the peer support team (opt-in by the member, critical incidents only).</summary>
		public bool RemindPeerTeam { get; set; }

		/// <summary>The next anniversary date (department-local date at midnight) a reminder is due for; null when none is.</summary>
		public DateTime? NextAnniversaryOn { get; set; }

		public DateTime? LastReminderSentOn { get; set; }

		public DateTime? ConfirmedOn { get; set; }

		public string ConfirmedByUserId { get; set; }

		public DateTime? DismissedOn { get; set; }

		public string DismissedByUserId { get; set; }

		public DateTime? VoidedOn { get; set; }

		public string VoidedByUserId { get; set; }

		/// <summary>ADP row marker (catalog 37): true once a protected column carries an envelope.</summary>
		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime ModifiedOn { get; set; }

		public string ModifiedByUserId { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		public bool Counts => Status == (int)ResponderExposureStatuses.Confirmed;

		[NotMapped]
		public object IdValue { get => RmsResponderExposureId; set => RmsResponderExposureId = value?.ToString(); }

		[NotMapped]
		public string TableName => "RmsResponderExposures";

		[NotMapped]
		public string IdName => "RmsResponderExposureId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Counts" };
	}

	/// <summary>
	/// A post-call flag (§13b WL0): an officer marks a call as a critical incident, or as one where members were exposed to a
	/// category. Worker 91 then suggests an entry to every member the dispatch data places on the call. The critical-incident flag
	/// is this row with <see cref="ResponderExposureCategories.CriticalIncident"/>; it is not <see cref="Call.IsCritical"/>.
	/// Operational: the fact that a call was flagged carries no health detail.
	/// </summary>
	public class RmsCallExposureFlag : IEntity
	{
		public string RmsCallExposureFlagId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary><see cref="ResponderExposureCategories"/>.</summary>
		public int Category { get; set; }

		public DateTime FlaggedOn { get; set; }

		public string FlaggedByUserId { get; set; }

		/// <summary>When worker 91 built the suggestions; null while they are still to be built.</summary>
		public DateTime? SuggestionsGeneratedOn { get; set; }

		public int SuggestionCount { get; set; }

		public DateTime? ClearedOn { get; set; }

		public string ClearedByUserId { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		public bool IsActive => !ClearedOn.HasValue;

		[NotMapped]
		public object IdValue { get => RmsCallExposureFlagId; set => RmsCallExposureFlagId = value?.ToString(); }

		[NotMapped]
		public string TableName => "RmsCallExposureFlags";

		[NotMapped]
		public string IdName => "RmsCallExposureFlagId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsActive" };
	}

	/// <summary>
	/// An at-risk screening flag on a member (§13b WL0): a wellness officer marks the member for increased screening and keeps the
	/// follow-up plan. Its follow-up tasks are Records work assignments with the purpose
	/// <see cref="RmsWorkAssignmentPurposes.WellnessFollowUp"/> whose RecordId names this flag. One active flag per member.
	/// </summary>
	public class RmsResponderScreeningFlag : IEntity
	{
		public string RmsResponderScreeningFlagId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary>The exposure category that prompted it, when one did (operational).</summary>
		public int? Category { get; set; }

		/// <summary>When the next screening or review is due (operational).</summary>
		public DateTime? ReviewOn { get; set; }

		/// <summary>Protected: the follow-up plan.</summary>
		public string PlanNotes { get; set; }

		public DateTime FlaggedOn { get; set; }

		public string FlaggedByUserId { get; set; }

		public DateTime? ClearedOn { get; set; }

		public string ClearedByUserId { get; set; }

		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public DateTime ModifiedOn { get; set; }

		public string ModifiedByUserId { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		public bool IsActive => !ClearedOn.HasValue;

		[NotMapped]
		public object IdValue { get => RmsResponderScreeningFlagId; set => RmsResponderScreeningFlagId = value?.ToString(); }

		[NotMapped]
		public string TableName => "RmsResponderScreeningFlags";

		[NotMapped]
		public string IdName => "RmsResponderScreeningFlagId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsActive" };
	}

	/// <summary>
	/// ProtoBuf <see cref="DepartmentSettingTypes.WellnessSettings"/> (setting 141), Personnel → Wellness settings. Missing = no
	/// wellness officer (members still see their own entries), no peer support team, the 30-year minimum retention.
	/// </summary>
	[ProtoContract]
	public class WellnessSettings
	{
		/// <summary>
		/// The personnel role whose members are the department's wellness officers: they read every member's exposure entries and keep
		/// screening flags. Department administration alone does not open the wellness tier (§13b.5). 0 = none.
		/// </summary>
		[ProtoMember(1)]
		public int WellnessOfficerRoleId { get; set; }

		/// <summary>
		/// The certification type that marks the peer support team (§13b.3: tracked as a certification type with its own refresher).
		/// Members holding a valid certification of it receive the anniversary reminders members opt in to. 0 = none.
		/// </summary>
		[ProtoMember(2)]
		public int PeerTeamCertificationTypeId { get; set; }

		/// <summary>Years an entry is kept after it happened; never below <see cref="MemberWellnessRules.MinimumRetentionYears"/>.</summary>
		[ProtoMember(3)]
		[DefaultValue(MemberWellnessRules.MinimumRetentionYears)]
		public int RetentionYears { get; set; } = MemberWellnessRules.MinimumRetentionYears;

		/// <summary>
		/// WL1: the personnel role whose members are the department's designated officers (Ryan White Part G): they receive exposure reports
		/// and run the BBP / infectious-disease case workflow. 0 = the wellness officer role serves.
		/// </summary>
		[ProtoMember(4)]
		public int DesignatedOfficerRoleId { get; set; }

		/// <summary>WL1: the certification type of the annual bloodborne-pathogen training shown on each exposure case. 0 = not tracked.</summary>
		[ProtoMember(5)]
		public int BbpTrainingCertificationTypeId { get; set; }

		/// <summary>
		/// WL1: the certification type an NFPA 1582 medical clearance is recorded as (from the "Medical Clearance (NFPA 1582)" template).
		/// 0 = clearances are kept in Wellness only.
		/// </summary>
		[ProtoMember(6)]
		public int MedicalClearanceCertificationTypeId { get; set; }

		/// <summary>WL1: years an injury record is kept after the end of its year; never below <see cref="OshaRules.MinimumRetentionYears"/>.</summary>
		[ProtoMember(7)]
		[DefaultValue(OshaRules.MinimumRetentionYears)]
		public int InjuryRetentionYears { get; set; } = OshaRules.MinimumRetentionYears;

		public WellnessSettings Normalized()
		{
			return new WellnessSettings
			{
				WellnessOfficerRoleId = Math.Max(0, WellnessOfficerRoleId),
				PeerTeamCertificationTypeId = Math.Max(0, PeerTeamCertificationTypeId),
				RetentionYears = Math.Min(MemberWellnessRules.MaximumRetentionYears, Math.Max(MemberWellnessRules.MinimumRetentionYears, RetentionYears)),
				DesignatedOfficerRoleId = Math.Max(0, DesignatedOfficerRoleId),
				BbpTrainingCertificationTypeId = Math.Max(0, BbpTrainingCertificationTypeId),
				MedicalClearanceCertificationTypeId = Math.Max(0, MedicalClearanceCertificationTypeId),
				InjuryRetentionYears = Math.Min(OshaRules.MaximumRetentionYears, Math.Max(OshaRules.MinimumRetentionYears, InjuryRetentionYears))
			};
		}
	}

	/// <summary>
	/// ADP catalog 37 (§13b WL0; RMS plan §5.9.2 Tier 1): the health facts of an exposure entry and the screening plan, Personnel
	/// family. The member link, call, category, dates, duration and statuses are operational and never cataloged (§25.4 item 4c).
	/// The accessor maps drive the generic Records write/read seams.
	/// </summary>
	public static class WellnessProtectedFields
	{
		public const int CatalogVersion = 37;
		public const string Family = "Personnel";

		public static readonly IReadOnlyDictionary<string, (Func<RmsResponderExposure, string> Get, Action<RmsResponderExposure, string> Set)> Exposures =
			new Dictionary<string, (Func<RmsResponderExposure, string>, Action<RmsResponderExposure, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["rmsresponderexposures.substance"] = (e => e.Substance, (e, v) => e.Substance = v),
				["rmsresponderexposures.narrative"] = (e => e.Narrative, (e, v) => e.Narrative = v),
				["rmsresponderexposures.ppeworn"] = (e => e.PpeWorn, (e, v) => e.PpeWorn = v),
				["rmsresponderexposures.decontamination"] = (e => e.Decontamination, (e, v) => e.Decontamination = v),
				["rmsresponderexposures.healthdetails"] = (e => e.HealthDetails, (e, v) => e.HealthDetails = v)
			};

		public static readonly IReadOnlyDictionary<string, (Func<RmsResponderScreeningFlag, string> Get, Action<RmsResponderScreeningFlag, string> Set)> ScreeningFlags =
			new Dictionary<string, (Func<RmsResponderScreeningFlag, string>, Action<RmsResponderScreeningFlag, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["rmsresponderscreeningflags.plannotes"] = (f => f.PlanNotes, (f, v) => f.PlanNotes = v)
			};

		/// <summary>(table, column, classification) for the catalog registration and the table bindings.</summary>
		public static IEnumerable<(string Table, string Column, string Classification)> All()
		{
			yield return ("RmsResponderExposures", "Substance", "Phi");
			yield return ("RmsResponderExposures", "Narrative", "Phi");
			yield return ("RmsResponderExposures", "PpeWorn", "Phi");
			yield return ("RmsResponderExposures", "Decontamination", "Phi");
			yield return ("RmsResponderExposures", "HealthDetails", "Phi");
			yield return ("RmsResponderScreeningFlags", "PlanNotes", "Phi");
		}

		/// <summary>The operational columns that must never be cataloged (a nightly rule reads them without a grant).</summary>
		public static readonly IReadOnlyList<string> OperationalExposureColumns = new[] { "UserId", "CallId", "Category", "OccurredOn", "DurationMinutes", "Status" };
	}

	/// <summary>Pure rules of the wellness log: retention, anniversaries and who addresses what.</summary>
	public static class MemberWellnessRules
	{
		/// <summary>OSHA 29 CFR 1910.1020: exposure records are kept at least 30 years.</summary>
		public const int MinimumRetentionYears = 30;
		public const int MaximumRetentionYears = 100;

		/// <summary>An anniversary reminder goes out this many days before the date ("upcoming").</summary>
		public const int AnniversaryLeadDays = 7;

		/// <summary>Reminders go out from this department-local hour on (worker 91 ticks every 15 minutes; the claim sends once).</summary>
		public const int AnniversaryLocalHour = 9;

		/// <summary>Suggestions for a flag on a call that is still open wait; after this long they are built anyway.</summary>
		public static readonly TimeSpan OpenCallSuggestionDelay = TimeSpan.FromHours(12);

		/// <summary>A suggested duration is capped: a status left set for days is not a 3-day exposure.</summary>
		public const int MaximumSuggestedMinutes = 24 * 60;

		public const int SubstanceMax = 500;
		public const int PpeWornMax = 500;
		public const int DecontaminationMax = 1000;
		public const int NarrativeMax = 4000;
		public const int HealthDetailsMax = 4000;
		public const int PlanNotesMax = 4000;

		public static bool IsKnownCategory(int category) => Enum.IsDefined(typeof(ResponderExposureCategories), category);

		/// <summary>Anniversary reminders exist for critical incidents only.</summary>
		public static bool OffersAnniversaryReminders(int category) => category == (int)ResponderExposureCategories.CriticalIncident;

		/// <summary>The effective retention in years for a department setting (clamped to the 30-year minimum).</summary>
		public static int RetentionYears(WellnessSettings settings) => (settings ?? new WellnessSettings()).Normalized().RetentionYears;

		/// <summary>True when the entry has passed its retention and may be erased (a legal hold still keeps it).</summary>
		public static bool IsPastRetention(RmsResponderExposure entry, WellnessSettings settings, DateTime utcNow)
		{
			if (entry == null)
				return false;
			return entry.OccurredOn.AddYears(RetentionYears(settings)) < utcNow;
		}

		/// <summary>
		/// The next anniversary on or after <paramref name="localToday"/> of an entry that happened on <paramref name="localOccurred"/>
		/// (both department-local dates). The first anniversary is one year on; 29 February falls on 28 February in other years.
		/// </summary>
		public static DateTime NextAnniversary(DateTime localOccurred, DateTime localToday)
		{
			var occurred = localOccurred.Date;
			var today = localToday.Date;
			var years = Math.Max(1, today.Year - occurred.Year);
			var candidate = occurred.AddYears(years);
			while (candidate < today)
			{
				years++;
				candidate = occurred.AddYears(years);
			}
			return DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified);
		}

		/// <summary>The anniversary after <paramref name="anniversary"/>, kept on the occurrence's day of the month.</summary>
		public static DateTime FollowingAnniversary(DateTime localOccurred, DateTime anniversary)
		{
			return NextAnniversary(localOccurred, anniversary.Date.AddDays(1));
		}

		/// <summary>How many whole years separate the occurrence and an anniversary (for the reminder text).</summary>
		public static int YearsSince(DateTime localOccurred, DateTime anniversary)
		{
			return Math.Max(1, anniversary.Year - localOccurred.Year);
		}

		/// <summary>True when a reminder for <paramref name="anniversary"/> is due on <paramref name="localToday"/>.</summary>
		public static bool IsReminderDue(DateTime anniversary, DateTime localToday)
		{
			return anniversary.Date.AddDays(-AnniversaryLeadDays) <= localToday.Date;
		}
	}

	/// <summary>What a member or a wellness officer types when logging or editing an entry.</summary>
	public class ResponderExposureInput
	{
		public string UserId { get; set; }
		public int? CallId { get; set; }
		public int Category { get; set; }
		public DateTime OccurredOn { get; set; }
		public int? DurationMinutes { get; set; }
		public string Substance { get; set; }
		public string Narrative { get; set; }
		public string PpeWorn { get; set; }
		public string Decontamination { get; set; }
		public string HealthDetails { get; set; }
		public bool RemindMember { get; set; }
		public bool RemindPeerTeam { get; set; }
	}

	/// <summary>A member's place in the officer's report: operational counts only.</summary>
	public class MemberExposureSummary
	{
		public string UserId { get; set; }
		public int Total { get; set; }
		public Dictionary<int, int> ByCategory { get; set; } = new Dictionary<int, int>();
		public int CriticalIncidents { get; set; }
		public int PendingSuggestions { get; set; }
		public DateTime? LastOccurredOn { get; set; }
		public bool ScreeningFlagged { get; set; }
		public DateTime? ScreeningReviewOn { get; set; }
		public int OpenFollowUps { get; set; }
	}

	/// <summary>The member's own wellness log as one view (web "My exposures", v4 GetMyExposures, the export).</summary>
	public class MemberWellnessOverview
	{
		public string UserId { get; set; }
		public List<RmsResponderExposure> Entries { get; set; } = new List<RmsResponderExposure>();
		public List<RmsResponderExposure> Suggestions { get; set; } = new List<RmsResponderExposure>();
		public Dictionary<int, int> ConfirmedByCategory { get; set; } = new Dictionary<int, int>();
		public int CriticalIncidentCount { get; set; }
		public RmsResponderScreeningFlag ScreeningFlag { get; set; }
		public List<RmsRecordWorkAssignment> FollowUps { get; set; } = new List<RmsRecordWorkAssignment>();
		/// <summary>True when protected values were withheld (ADP enforced and no grant presented).</summary>
		public bool ProtectedWithheld { get; set; }
	}

	/// <summary>One member the dispatch data places on a call, with the window they were there.</summary>
	public class ExposureSuggestionCandidate
	{
		public string UserId { get; set; }
		public DateTime StartedOn { get; set; }
		public DateTime EndedOn { get; set; }
		public SortedSet<string> Bases { get; } = new SortedSet<string>(StringComparer.Ordinal);

		public int? DurationMinutes
		{
			get
			{
				var minutes = (int)Math.Round((EndedOn - StartedOn).TotalMinutes);
				if (minutes <= 0)
					return null;
				return Math.Min(minutes, MemberWellnessRules.MaximumSuggestedMinutes);
			}
		}
	}

	/// <summary>What one run of worker 91 did.</summary>
	public class WellnessRunResult
	{
		public int FlagsProcessed { get; set; }
		public int SuggestionsCreated { get; set; }
		public int RemindersSent { get; set; }
		public int EntriesErased { get; set; }
		public int Failures { get; set; }

		/// <summary>WL1: overdue Ryan White answers and return-to-duty reviews the officers were told about.</summary>
		public int HealthNoticesSent { get; set; }

		/// <summary>WL1: exposure cases, injuries and clearances erased past their retention.</summary>
		public int HealthRecordsErased { get; set; }
	}

	/// <summary>A wellness rule refused the action (a 400, not a 403).</summary>
	public class MemberWellnessException : Exception
	{
		public MemberWellnessException(string code) : base(code)
		{
			Code = code;
		}

		/// <summary>A stable code the web and v4 layers map to a localized message.</summary>
		public string Code { get; }
	}
}
