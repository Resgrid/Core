using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>What <c>IIncidentReportsService.RefreshFromSourcesAsync</c> changed.</summary>
	public class IncidentReportRefreshResult
	{
		public IncidentReportAggregate Aggregate { get; set; }

		/// <summary>Blank fields the sources filled.</summary>
		public int FilledCount { get; set; }

		/// <summary>Uncorrected prefilled values that followed their source to a newer value.</summary>
		public int UpdatedCount { get; set; }

		/// <summary>Units, mutual aid rows and tactic timestamps added.</summary>
		public int AddedCount { get; set; }

		/// <summary>Sources that could not be read this time.</summary>
		public List<string> Warnings { get; set; } = new List<string>();

		public bool Changed => FilledCount + UpdatedCount + AddedCount > 0;
	}

	/// <summary>Source fact keys for the report-source prefill (alongside <see cref="NerisFactKeys"/>).</summary>
	public static class IncidentSourceFactKeys
	{
		/// <summary>
		/// A unit's staffing from the crew recorded with its statuses. Kept apart from the <c>unit.{id}.</c> time keys, which
		/// decide the unit row's times provenance.
		/// </summary>
		public static string UnitStaffing(int unitId) => $"crew.{unitId}.staffing";

		/// <summary>A mutual aid agency Incident Command tracked on the call.</summary>
		public static string MutualAid(string agencyKey) => $"aid.{agencyKey}";
	}
}
