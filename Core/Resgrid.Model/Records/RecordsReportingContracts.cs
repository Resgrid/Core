using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Where a report activity entry came from; totals reconcile across the cutover because both sources feed one shape.</summary>
	public static class ReportActivitySources
	{
		public const string LegacyLog = "Log";
		public const string Record = "Record";
	}

	public class ReportActivityParticipant
	{
		public string UserId { get; set; }
		public int? UnitId { get; set; }
	}

	public class ReportActivityUnit
	{
		public int UnitId { get; set; }
		public DateTime? Dispatched { get; set; }
		public DateTime? Enroute { get; set; }
		public DateTime? OnScene { get; set; }
		public DateTime? Released { get; set; }
		public DateTime? InQuarters { get; set; }
	}

	/// <summary>
	/// Source-agnostic activity row for the department reports (RMS plan section 4.10, "report projections that
	/// span the cutover boundary"): a legacy Log before activation, a finalized Record after it, same shape. Only
	/// the fields the reports aggregate are carried; no narrative, no restricted section.
	/// </summary>
	public class ReportActivityEntry
	{
		public string Source { get; set; }
		public string SourceId { get; set; }
		public RmsOperationalRecordType Type { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? EndedOn { get; set; }
		public DateTime LoggedOn { get; set; }
		public string LoggedByUserId { get; set; }
		public string Course { get; set; }
		public int? CallId { get; set; }
		public string CallNumber { get; set; }
		public string CallName { get; set; }
		public int? StationGroupId { get; set; }
		public List<ReportActivityParticipant> Participants { get; set; } = new List<ReportActivityParticipant>();
		public List<ReportActivityUnit> Units { get; set; } = new List<ReportActivityUnit>();
	}
}
