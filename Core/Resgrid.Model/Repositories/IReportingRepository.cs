using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Reporting;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Set-based aggregate data access for platform reporting. Every method returns ONLY aggregates
	/// (counts/buckets), produced by GROUP BY/COUNT over indexed columns — no row materialization and
	/// no per-department iteration. A null <paramref name="departmentId"/> means system-wide
	/// (cross-department), used only by the in-process BackOffice.
	/// </summary>
	public interface IReportingRepository
	{
		// ----- Scalar totals -----

		/// <summary>Count of non-deleted calls. When both dates are null, returns the all-time total for the scope.</summary>
		Task<long> GetCallsCountAsync(int? departmentId, DateTime? startUtc, DateTime? endUtc, CancellationToken cancellationToken = default);

		/// <summary>Count of currently open/active calls for the scope.</summary>
		Task<long> GetActiveCallsCountAsync(int? departmentId, CancellationToken cancellationToken = default);

		Task<long> GetMessagesCountAsync(int? departmentId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

		Task<long> GetNewUsersCountAsync(int? departmentId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

		/// <summary>Count of active members for the scope (excludes deleted/disabled).</summary>
		Task<long> GetPersonnelCountAsync(int? departmentId, CancellationToken cancellationToken = default);

		Task<long> GetUnitsCountAsync(int? departmentId, CancellationToken cancellationToken = default);

		/// <summary>System-wide only: non-deleted department count (optionally new-in-window).</summary>
		Task<long> GetDepartmentsCountAsync(DateTime? startUtc, DateTime? endUtc, CancellationToken cancellationToken = default);

		// ----- Time-bucketed series (sparse; the service zero-fills) -----

		Task<IEnumerable<CountByDateBucketResult>> GetCallsByDateBucketAsync(int? departmentId, DateTime startUtc, DateTime endUtc, ReportGranularity granularity, CancellationToken cancellationToken = default);

		Task<IEnumerable<CountByDateBucketResult>> GetMessagesByDateBucketAsync(int? departmentId, DateTime startUtc, DateTime endUtc, ReportGranularity granularity, CancellationToken cancellationToken = default);

		Task<IEnumerable<CountByDateBucketResult>> GetNewUsersByDateBucketAsync(int? departmentId, DateTime startUtc, DateTime endUtc, ReportGranularity granularity, CancellationToken cancellationToken = default);

		// ----- Breakdowns (capped to top-N + "other" in the service) -----

		Task<IEnumerable<CountByStringKeyResult>> GetCallsBreakdownByTypeAsync(int? departmentId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

		Task<IEnumerable<CountByKeyResult>> GetCallsBreakdownByPriorityAsync(int? departmentId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

		Task<IEnumerable<CountByKeyResult>> GetCallsBreakdownByStateAsync(int? departmentId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

		// ----- Latest-state counts (grouped by the RAW status id over the latest-per-entity set) -----
		// The service resolves each raw id to a canonical base type / AvailabilityClass.

		Task<IEnumerable<CountByKeyResult>> GetLatestPersonnelStateCountsAsync(int? departmentId, CancellationToken cancellationToken = default);

		Task<IEnumerable<CountByKeyResult>> GetLatestUnitStateCountsAsync(int? departmentId, CancellationToken cancellationToken = default);
	}
}
