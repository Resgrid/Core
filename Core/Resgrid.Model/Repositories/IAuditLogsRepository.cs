using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IAuditLogsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.AuditLog}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.AuditLog}" />
	public interface IAuditLogsRepository: IRepository<AuditLog>
	{
		/// <summary>
		/// Gets a date-ranged, optionally type-filtered, paged set of audit logs for a department.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="startDate">Inclusive lower bound on LoggedOn (UTC).</param>
		/// <param name="endDate">Exclusive upper bound on LoggedOn (UTC).</param>
		/// <param name="logType">Optional LogType filter; when null all types are returned.</param>
		/// <param name="page">1-based page number.</param>
		/// <param name="pageSize">Page size.</param>
		/// <returns>Task&lt;IEnumerable&lt;AuditLog&gt;&gt;.</returns>
		Task<IEnumerable<AuditLog>> GetAuditLogsForDepartmentPagedAsync(int departmentId, DateTime startDate, DateTime endDate, int? logType, int page, int pageSize);
	}
}
