using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The Records work queues an officer opens the module to look at (RMS plan RMS-3): incomplete, awaiting
	/// review, rejected, accepted, overdue — plus the statutory disclosure clock and the crosswalk coverage that
	/// decides whether a filing will map cleanly at all.
	/// <para>
	/// Every count is department-scoped and group-scope-aware, so a dashboard never tells a member that records
	/// exist which their queue will not show them.
	/// </para>
	/// </summary>
	public interface IRecordsDashboardService
	{
		Task<RecordsDashboard> GetAsync(int departmentId, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// NERIS crosswalk coverage (RMS plan RMS-3, "crosswalk reporting"): which of the department's own call
		/// types map to a NERIS incident type and which do not. An unmapped type is a filing that will need manual
		/// classification on the night, so this is a gap report, not a statistic.
		/// </summary>
		Task<NerisCrosswalkCoverage> GetCrosswalkCoverageAsync(int departmentId, CancellationToken cancellationToken = default);
	}

	/// <summary>Queue counts across both Records aggregates plus the obligations and requests that carry a clock.</summary>
	public class RecordsDashboard
	{
		public DateTime GeneratedOn { get; set; } = DateTime.UtcNow;

		/// <summary>Operational Records still being authored.</summary>
		public int OperationalDrafts { get; set; }

		public int OperationalAwaitingReview { get; set; }

		public int OperationalReturned { get; set; }

		/// <summary>Incident reports not yet finalized — the "incomplete" queue.</summary>
		public int IncidentIncomplete { get; set; }

		public int IncidentAwaitingReview { get; set; }

		/// <summary>Finalized and queued or in flight to the destination.</summary>
		public int IncidentSubmitted { get; set; }

		public int IncidentAccepted { get; set; }

		/// <summary>Rejected by the destination and not yet corrected; the queue that costs a department money.</summary>
		public int IncidentRejected { get; set; }

		/// <summary>Obligations currently sitting overdue (worker 42's persisted due states).</summary>
		public int Overdue { get; set; }

		/// <summary>Incident analyses finalized but not yet filed, usually waiting on their incident.</summary>
		public int AnalysesAwaitingFiling { get; set; }

		public int DisclosuresOpen { get; set; }

		/// <summary>Open public-records requests past their statutory due date.</summary>
		public int DisclosuresOverdue { get; set; }

		/// <summary>Set when a count could not be produced; the dashboard degrades rather than failing whole.</summary>
		public List<string> Warnings { get; set; } = new List<string>();
	}

	/// <summary>One local code and whether it maps to a NERIS value.</summary>
	public class NerisCrosswalkCoverageItem
	{
		public string SetKey { get; set; }
		public string LocalCode { get; set; }
		public string NerisCode { get; set; }
		public bool Mapped => !string.IsNullOrWhiteSpace(NerisCode);
	}

	/// <summary>Crosswalk gap report for a department.</summary>
	public class NerisCrosswalkCoverage
	{
		public string ContractVersion { get; set; }
		public int TotalLocalCodes { get; set; }
		public int MappedCount { get; set; }
		public int UnmappedCount { get; set; }

		/// <summary>Mapped codes whose NERIS value is not in the pinned contract any more; a silent submission failure waiting to happen.</summary>
		public int StaleMappingCount { get; set; }

		public List<NerisCrosswalkCoverageItem> Items { get; set; } = new List<NerisCrosswalkCoverageItem>();

		public List<string> Warnings { get; set; } = new List<string>();
	}
}
