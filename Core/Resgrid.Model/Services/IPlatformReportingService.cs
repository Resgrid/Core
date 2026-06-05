using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Reporting;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Platform reporting and analytics. Powers dashboards, widgets and the Dispatch app's realtime
	/// availability display, both at a per-department level and (for the in-process BackOffice only) a
	/// cross-department system level.
	///
	/// SECURITY: a null <c>departmentId</c> means SYSTEM-WIDE aggregation across every department and
	/// is intended ONLY for the Resgrid-staff BackOffice running Core in-process. The department-locked
	/// v4 HTTP controller MUST always pass the caller's claim DepartmentId and never accept a
	/// client-supplied value, to prevent cross-department data leakage. All results are counts/
	/// aggregates only (no PII).
	/// </summary>
	public interface IPlatformReportingService
	{
		/// <summary>
		/// Composite dashboard payload (scalar totals, dense UTC time series, top-N+other breakdowns)
		/// for the window [<paramref name="startUtc"/>, <paramref name="endUtc"/>].
		/// </summary>
		/// <param name="departmentId">Department to scope to; null = system-wide (BackOffice only).</param>
		Task<DashboardReport> GetDashboardReportAsync(int? departmentId, DateTime startUtc, DateTime endUtc,
			ReportGranularity granularity, int topN = 5, bool bypassCache = false, CancellationToken cancellationToken = default);

		/// <summary>Response-time / NFPA analytics (alarm handling, turnout, travel, total response).</summary>
		Task<ResponseTimeReport> GetResponseTimeReportAsync(int? departmentId, DateTime startUtc, DateTime endUtc,
			bool bypassCache = false, CancellationToken cancellationToken = default);

		/// <summary>Unit Hour Utilization and workload analytics.</summary>
		Task<UtilizationReport> GetUtilizationReportAsync(int? departmentId, DateTime startUtc, DateTime endUtc,
			bool bypassCache = false, CancellationToken cancellationToken = default);

		/// <summary>Personnel participation and certification-compliance analytics.</summary>
		Task<ParticipationReport> GetParticipationReportAsync(int? departmentId, DateTime startUtc, DateTime endUtc,
			bool bypassCache = false, CancellationToken cancellationToken = default);

		/// <summary>
		/// Streams a CSV export of incidents for the window using the requested field mapping
		/// (<see cref="ExportProfile"/>). NFIRS/NEMSIS profiles emit standardized incident fields.
		/// </summary>
		Task<Stream> ExportIncidentsCsvAsync(int? departmentId, DateTime startUtc, DateTime endUtc,
			ExportProfile profile, CancellationToken cancellationToken = default);

		/// <summary>
		/// Returns the standardized required fields for a profile that Resgrid does not currently capture
		/// (the export "gap report"). Empty for <see cref="ExportProfile.Generic"/>.
		/// </summary>
		IReadOnlyList<string> GetUnmappedRequiredExportFields(ExportProfile profile);

		/// <summary>
		/// Classifies a person's current status value into a canonical <see cref="AvailabilityClass"/>.
		/// <paramref name="actionTypeId"/> is the latest <c>ActionLog.ActionTypeId</c> — either a built-in
		/// <c>ActionTypes</c> value or a <c>CustomStateDetailId</c>. Reusable by the Dispatch app's realtime
		/// availability display so the "available for a call?" answer is consistent everywhere.
		/// </summary>
		Task<AvailabilityClass> ClassifyPersonnelAvailabilityAsync(int departmentId, int actionTypeId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Classifies a unit's current status value into a canonical <see cref="AvailabilityClass"/>.
		/// <paramref name="unitState"/> is the latest <c>UnitState.State</c> — either a built-in
		/// <c>UnitStateTypes</c> value or a <c>CustomStateDetailId</c>.
		/// </summary>
		Task<AvailabilityClass> ClassifyUnitAvailabilityAsync(int departmentId, int unitState, CancellationToken cancellationToken = default);
	}
}
