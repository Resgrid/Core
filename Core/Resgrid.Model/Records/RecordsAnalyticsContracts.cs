using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Window and filters for the RMS-6 records analytics dashboards (RMS plan section 6, RMS-6). Every dashboard
	/// is computed over finalized Records inside [Start, End) — the window is clamped by the service to at most
	/// <see cref="RecordsAnalyticsLimits.MaxWindowDays"/> and the read to <see cref="RecordsAnalyticsLimits.RowCap"/>
	/// rows, after which the result says it is truncated rather than silently under-counting.
	/// </summary>
	public sealed class RecordsAnalyticsQuery
	{
		/// <summary>Inclusive start (UTC). Defaults to 90 days before <see cref="End"/>.</summary>
		public DateTime? Start { get; set; }

		/// <summary>Exclusive end (UTC). Defaults to now.</summary>
		public DateTime? End { get; set; }

		/// <summary>
		/// Restrict to Records and incident reports whose own station group is this group. The filter is on the Record
		/// header, not on the responding unit's group snapshot, so a dashboard filtered to a station reads that station's
		/// incidents including any mutual-aid unit that ran them.
		/// </summary>
		public int? StationGroupId { get; set; }

		/// <summary>Restrict operational Records to one definition key (e.g. system.run).</summary>
		public string DefinitionKey { get; set; }

		/// <summary>Turnout target in seconds (dispatched to en route). NFPA 1710 uses 80 s for fire, 60 s for EMS.</summary>
		public int TurnoutTargetSeconds { get; set; } = 80;

		/// <summary>Travel target in seconds (en route to on scene). NFPA 1710 uses 240 s for the first arriving engine.</summary>
		public int TravelTargetSeconds { get; set; } = 240;
	}

	public static class RecordsAnalyticsLimits
	{
		/// <summary>Widest window a dashboard computes in one request.</summary>
		public const int MaxWindowDays = 366;

		/// <summary>Plan section 5.12: the saved-report result cap; past it the dashboard reports truncation.</summary>
		public const int RowCap = 50000;

		public const int DefaultWindowDays = 90;
	}

	/// <summary>Fields every dashboard carries: the effective window, how it was scoped and whether the read hit the cap.</summary>
	public abstract class RecordsAnalyticsBase
	{
		public int DepartmentId { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public DateTime GeneratedOn { get; set; } = DateTime.UtcNow;

		/// <summary>True when the viewer is group-scoped, so every figure is over the Records they may open, not the department.</summary>
		public bool GroupScoped { get; set; }

		/// <summary>True when the read stopped at the row cap; narrow the window.</summary>
		public bool Truncated { get; set; }

		/// <summary>Records (operational plus incident reports) that fed the dashboard after scoping.</summary>
		public int RecordsRead { get; set; }

		/// <summary>Set when a section could not be produced; the dashboard degrades rather than failing whole.</summary>
		public List<string> Warnings { get; set; } = new List<string>();
	}

	/// <summary>One row of a grouped count: the grouping key, a display label resolved by the service, and totals.</summary>
	public class RecordsAnalyticsCount
	{
		public string Key { get; set; }
		public string Label { get; set; }
		public int Count { get; set; }

		/// <summary>Hours attributable to the group when the dimension carries time (records, personnel, units); 0 otherwise.</summary>
		public double Hours { get; set; }

		/// <summary>Share of the dimension's total, 0–100, when meaningful.</summary>
		public double? Percent { get; set; }
	}

	/// <summary>Distribution of one interval (turnout, travel, total response, on scene) in seconds.</summary>
	public class RecordsResponseTimeStat
	{
		public int Samples { get; set; }
		public double? AverageSeconds { get; set; }
		public double? MedianSeconds { get; set; }

		/// <summary>90th percentile, nearest-rank — the figure accreditation and NFPA 1710 reporting quote.</summary>
		public double? P90Seconds { get; set; }
		public double? MaxSeconds { get; set; }

		/// <summary>Target this interval was measured against, null when the interval has none.</summary>
		public int? TargetSeconds { get; set; }
		public int WithinTarget { get; set; }
		public double? WithinTargetPercent { get; set; }
	}

	/// <summary>Response-time distributions for one grouping (unit, station group, hour of day, month).</summary>
	public class RecordsResponseTimeRow
	{
		public string Key { get; set; }
		public string Label { get; set; }
		public int Responses { get; set; }
		public RecordsResponseTimeStat Turnout { get; set; } = new RecordsResponseTimeStat();
		public RecordsResponseTimeStat Travel { get; set; } = new RecordsResponseTimeStat();
		public RecordsResponseTimeStat TotalResponse { get; set; } = new RecordsResponseTimeStat();
		public RecordsResponseTimeStat OnScene { get; set; } = new RecordsResponseTimeStat();
	}

	/// <summary>
	/// Response performance (RMS-6): turnout, travel, total response and time on scene from the attested unit
	/// response rows of finalized operational Records and incident reports. A unit time is used only when both
	/// timestamps exist, are ordered, and span less than a day — a mis-keyed time never becomes an outlier.
	/// </summary>
	public class RecordsResponsePerformance : RecordsAnalyticsBase
	{
		/// <summary>Unit responses with at least one usable interval.</summary>
		public int Responses { get; set; }

		/// <summary>Records that contributed at least one usable unit response.</summary>
		public int RecordsWithResponses { get; set; }

		public RecordsResponseTimeStat Turnout { get; set; } = new RecordsResponseTimeStat();
		public RecordsResponseTimeStat Travel { get; set; } = new RecordsResponseTimeStat();
		public RecordsResponseTimeStat TotalResponse { get; set; } = new RecordsResponseTimeStat();
		public RecordsResponseTimeStat OnScene { get; set; } = new RecordsResponseTimeStat();

		/// <summary>First arriving unit per Record: earliest on-scene minus earliest dispatch.</summary>
		public RecordsResponseTimeStat FirstArrival { get; set; } = new RecordsResponseTimeStat();

		public List<RecordsResponseTimeRow> ByUnit { get; set; } = new List<RecordsResponseTimeRow>();
		public List<RecordsResponseTimeRow> ByStationGroup { get; set; } = new List<RecordsResponseTimeRow>();
		public List<RecordsResponseTimeRow> ByHourOfDay { get; set; } = new List<RecordsResponseTimeRow>();
		public List<RecordsResponseTimeRow> ByMonth { get; set; } = new List<RecordsResponseTimeRow>();

		/// <summary>Operational definition versus NERIS incident report.</summary>
		public List<RecordsResponseTimeRow> BySource { get; set; } = new List<RecordsResponseTimeRow>();
	}

	/// <summary>One cell of the weekday-by-hour heat map.</summary>
	public class RecordsAnalyticsHeatCell
	{
		/// <summary>0 = Sunday … 6 = Saturday (System.DayOfWeek).</summary>
		public int Weekday { get; set; }
		public int Hour { get; set; }
		public int Count { get; set; }
	}

	/// <summary>
	/// Workload (RMS-6): what was recorded, by whom, for how long. Hours come from the Record's start/end (or a
	/// participant's own span when it has one); a Record without an end contributes zero rather than a guess.
	/// </summary>
	public class RecordsWorkload : RecordsAnalyticsBase
	{
		public int RecordsFinalized { get; set; }
		public int IncidentReportsFinalized { get; set; }

		/// <summary>Sum of Record durations in hours.</summary>
		public double RecordHours { get; set; }

		/// <summary>Sum of per-participant hours (the Personnel Hours report's figure).</summary>
		public double PersonnelHours { get; set; }
		public double TrainingHours { get; set; }
		public int UnitResponses { get; set; }
		public double UnitOnSceneHours { get; set; }

		public List<RecordsAnalyticsCount> ByDefinition { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsAnalyticsCount> ByMonth { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsAnalyticsCount> ByAuthor { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsAnalyticsCount> ByStationGroup { get; set; } = new List<RecordsAnalyticsCount>();

		/// <summary>Responses and on-scene hours per unit (unit utilization).</summary>
		public List<RecordsAnalyticsCount> ByUnit { get; set; } = new List<RecordsAnalyticsCount>();

		/// <summary>Records participated in and hours per person.</summary>
		public List<RecordsAnalyticsCount> ByPerson { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsAnalyticsHeatCell> ByWeekdayHour { get; set; } = new List<RecordsAnalyticsHeatCell>();
	}

	/// <summary>A headline figure with the same figure for the preceding window of equal length.</summary>
	public class RecordsKpi
	{
		public string Key { get; set; }
		public double Current { get; set; }
		public double Prior { get; set; }

		/// <summary>Percentage change from prior to current; null when the prior figure is zero.</summary>
		public double? ChangePercent { get; set; }
	}

	/// <summary>
	/// Executive summary (RMS-6): the headline figures a chief reads once a month, each against the preceding
	/// window, plus the process measures — time to finalize, review turnaround, return rate, obligations that went
	/// overdue — that only the Records lifecycle can answer.
	/// </summary>
	public class RecordsExecutiveSummary : RecordsAnalyticsBase
	{
		public DateTime PriorStart { get; set; }
		public DateTime PriorEnd { get; set; }
		public List<RecordsKpi> Kpis { get; set; } = new List<RecordsKpi>();

		/// <summary>Median hours from creation to first finalization.</summary>
		public double? MedianHoursToFinalize { get; set; }

		/// <summary>Median hours from submitted-for-review to approval.</summary>
		public double? MedianReviewHours { get; set; }

		/// <summary>Share of finalized Records that were returned at least once, 0–100.</summary>
		public double? ReturnRatePercent { get; set; }
		public double? NerisAcceptanceRatePercent { get; set; }
		public double? TurnoutP90Seconds { get; set; }
		public double? TotalResponseP90Seconds { get; set; }
		public double? FirstArrivalP90Seconds { get; set; }

		/// <summary>Obligations sitting overdue right now (worker 42's persisted due states).</summary>
		public int OverdueNow { get; set; }

		/// <summary>Obligations whose due state changed to overdue inside the window.</summary>
		public int WentOverdue { get; set; }
		public int OpenDrafts { get; set; }
		public int AwaitingReview { get; set; }
		public List<RecordsAnalyticsCount> ByDefinition { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsAnalyticsCount> ByMonth { get; set; } = new List<RecordsAnalyticsCount>();
	}

	public class RecordsInspectionMetrics
	{
		public int Completed { get; set; }
		public int Passed { get; set; }
		public int Failed { get; set; }
		public int Conditional { get; set; }
		public double? PassRatePercent { get; set; }
		public int Cancelled { get; set; }
		public int ReinspectionsRequired { get; set; }
		public int OpenNow { get; set; }
		public int OverdueNow { get; set; }
		public double? AverageDaysScheduledToCompleted { get; set; }

		/// <summary>Completed on or before the scheduled date.</summary>
		public int CompletedOnSchedule { get; set; }
		public double? OnSchedulePercent { get; set; }
		public List<RecordsAnalyticsCount> ByProgram { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsAnalyticsCount> ByMonth { get; set; } = new List<RecordsAnalyticsCount>();
	}

	public class RecordsViolationMetrics
	{
		public int Opened { get; set; }
		public int Corrected { get; set; }
		public int Verified { get; set; }
		public int Waived { get; set; }
		public int Escalated { get; set; }
		public int OpenNow { get; set; }
		public int OverdueNow { get; set; }
		public double? AverageDaysToCorrection { get; set; }
		public List<RecordsAnalyticsCount> BySeverity { get; set; } = new List<RecordsAnalyticsCount>();
	}

	public class RecordsPermitMetrics
	{
		public int Applied { get; set; }
		public int Issued { get; set; }
		public int Denied { get; set; }
		public int Expired { get; set; }
		public int Revoked { get; set; }
		public int ActiveNow { get; set; }
		public double? AverageDaysAppliedToIssued { get; set; }
		public List<RecordsAnalyticsCount> ByType { get; set; } = new List<RecordsAnalyticsCount>();
	}

	public class RecordsHydrantMetrics
	{
		public int Total { get; set; }
		public int InService { get; set; }
		public int OutOfService { get; set; }
		public int TestedWithin12Months { get; set; }
		public double? TestedWithin12MonthsPercent { get; set; }
		public int FlowTestsInWindow { get; set; }
		public List<RecordsAnalyticsCount> ByFlowClass { get; set; } = new List<RecordsAnalyticsCount>();
	}

	public class RecordsOccupancyMetrics
	{
		public int Total { get; set; }
		public int ReviewCurrent { get; set; }
		public double? ReviewCurrentPercent { get; set; }
		public int ReviewOverdue { get; set; }
		public int InspectedWithin12Months { get; set; }
		public double? InspectedWithin12MonthsPercent { get; set; }
		public int HazmatOnSite { get; set; }
		public int NoSprinklers { get; set; }
		public int OccupantsNeedingAssistance { get; set; }
		public int Vacant { get; set; }
		public int OpenViolations { get; set; }
		public List<RecordsAnalyticsCount> ByType { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsAnalyticsCount> ViolationsBySeverity { get; set; } = new List<RecordsAnalyticsCount>();
	}

	public class RecordsTrainingMetrics
	{
		public int Records { get; set; }
		public double Hours { get; set; }

		/// <summary>Distinct participants on Training Records.</summary>
		public int MembersTrained { get; set; }
		public double? HoursPerMember { get; set; }
		public List<RecordsAnalyticsCount> ByMonth { get; set; } = new List<RecordsAnalyticsCount>();
	}

	public class RecordsResponseSummary
	{
		public int Responses { get; set; }
		public double? TurnoutP90Seconds { get; set; }
		public double? TravelP90Seconds { get; set; }
		public double? TotalResponseP90Seconds { get; set; }
		public double? FirstArrivalP90Seconds { get; set; }
		public double? TurnoutWithinTargetPercent { get; set; }
		public double? TravelWithinTargetPercent { get; set; }
	}

	/// <summary>
	/// Accreditation (RMS-6): the CFAI/ISO-style evidence a department assembles from inspection, violation,
	/// permit, hydrant, occupancy, training and response history. Each prevention section is present only when
	/// its module is enabled; the flags say which.
	/// </summary>
	public class RecordsAccreditation : RecordsAnalyticsBase
	{
		public bool OccupancyEnabled { get; set; }
		public bool InspectionsEnabled { get; set; }
		public bool HydrantsEnabled { get; set; }
		public bool PermitsEnabled { get; set; }
		public RecordsInspectionMetrics Inspections { get; set; }
		public RecordsViolationMetrics Violations { get; set; }
		public RecordsPermitMetrics Permits { get; set; }
		public RecordsHydrantMetrics Hydrants { get; set; }
		public RecordsOccupancyMetrics Occupancies { get; set; }
		public RecordsTrainingMetrics Training { get; set; } = new RecordsTrainingMetrics();
		public RecordsResponseSummary Response { get; set; } = new RecordsResponseSummary();
	}

	public class RecordsCrrMetrics
	{
		public int Activities { get; set; }
		public int Audience { get; set; }
		public int SmokeAlarmsInstalled { get; set; }
		public double Hours { get; set; }
		public List<RecordsAnalyticsCount> ByKind { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsAnalyticsCount> ByMonth { get; set; } = new List<RecordsAnalyticsCount>();
	}

	public class RecordsIncidentMetrics
	{
		public int IncidentReports { get; set; }
		public int RunRecords { get; set; }

		/// <summary>Primary NERIS incident type on finalized incident reports.</summary>
		public List<RecordsAnalyticsCount> ByIncidentType { get; set; } = new List<RecordsAnalyticsCount>();

		/// <summary>Call type snapshot on finalized operational Records linked to a call.</summary>
		public List<RecordsAnalyticsCount> ByCallType { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsAnalyticsCount> ByMonth { get; set; } = new List<RecordsAnalyticsCount>();
		public List<RecordsAnalyticsCount> ByHourOfDay { get; set; } = new List<RecordsAnalyticsCount>();
	}

	/// <summary>
	/// Community risk (RMS-6): what the department is exposed to and what it is doing about it — incident mix,
	/// occupancy risk profile, open violations, hydrants out of service, and CRR activity. Prevention sections
	/// appear only when their module is enabled.
	/// </summary>
	public class RecordsCommunityRisk : RecordsAnalyticsBase
	{
		public bool CrrEnabled { get; set; }
		public bool OccupancyEnabled { get; set; }
		public bool InspectionsEnabled { get; set; }
		public bool HydrantsEnabled { get; set; }
		public RecordsCrrMetrics Crr { get; set; }
		public RecordsIncidentMetrics Incidents { get; set; } = new RecordsIncidentMetrics();
		public RecordsOccupancyMetrics Occupancies { get; set; }
		public RecordsHydrantMetrics Hydrants { get; set; }
	}

	/// <summary>Slim projection of an operational Record's call context (no narrative, no restricted section) for a revision.</summary>
	public class RmsRecordCallContext
	{
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		public string CallType { get; set; }
		public int? CallPriority { get; set; }
		public DateTime? CallLoggedOn { get; set; }
		public int? UnitId { get; set; }
		public DateTime? ActivityOn { get; set; }
	}

	/// <summary>Slim projection of a revision header (never the snapshot) for lifecycle analytics.</summary>
	public class RmsRevisionTransitionRow
	{
		public string RmsRevisionId { get; set; }
		public string RecordId { get; set; }
		public int RecordKind { get; set; }
		public int Transition { get; set; }
		public string DefinitionKey { get; set; }
		public string ActorUserId { get; set; }
		public DateTime CreatedOn { get; set; }
	}
}
