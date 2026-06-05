using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Reporting;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Read/write access to the pre-aggregated <see cref="ReportingDailyRollup"/> store that backs the
	/// heavy analytics. The rollup worker writes daily rows; the reporting service reads them. A null
	/// departmentId targets the system-wide (cross-department) rollup rows.
	/// </summary>
	public interface IReportingRollupRepository
	{
		/// <summary>
		/// Replaces all rollup rows for a (department, day) with the supplied set — delete-then-insert,
		/// so recomputing a day is idempotent. Returns the number of rows written.
		/// </summary>
		Task<int> UpsertDailyRollupAsync(int? departmentId, DateTime bucketDateUtc,
			IEnumerable<ReportingDailyRollup> rows, CancellationToken cancellationToken = default);

		/// <summary>Reads rollup rows for a metric within the day range for the given scope.</summary>
		Task<IEnumerable<ReportingDailyRollup>> GetRollupsAsync(int? departmentId, DateTime startUtc,
			DateTime endUtc, string metric, CancellationToken cancellationToken = default);
	}
}
