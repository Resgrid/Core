using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IAuditService
	{
		Task<AuditLog> SaveAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<AuditLog>> GetAllAuditLogsForDepartmentAsync(int departmentId);
		string GetAuditLogTypeString(AuditLogTypes logType);
		Task<AuditLog> GetAuditLogByIdAsync(int auditLogId);

		/// <summary>
		/// Gets a date-ranged, optionally type-filtered, paged set of audit logs for a department.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="startDate">Inclusive lower bound on LoggedOn (UTC).</param>
		/// <param name="endDate">Exclusive upper bound on LoggedOn (UTC).</param>
		/// <param name="logType">Optional LogType filter; when null all types are returned.</param>
		/// <param name="page">1-based page number.</param>
		/// <param name="pageSize">Page size.</param>
		Task<List<AuditLog>> GetAuditLogsForDepartmentPagedAsync(int departmentId, DateTime startDate, DateTime endDate, AuditLogTypes? logType, int page, int pageSize);
	}
}
