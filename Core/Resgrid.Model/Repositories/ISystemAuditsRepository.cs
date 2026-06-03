using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ISystemAuditsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.SystemAudit}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.SystemAudit}" />
	public interface ISystemAuditsRepository : IRepository<SystemAudit>
	{
		/// <summary>
		/// Gets a date-ranged, paged set of system audits for a user (e.g. a login timeline).
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="startDate">Inclusive lower bound on LoggedOn (UTC).</param>
		/// <param name="endDate">Exclusive upper bound on LoggedOn (UTC).</param>
		/// <param name="page">1-based page number.</param>
		/// <param name="pageSize">Page size.</param>
		/// <returns>Task&lt;IEnumerable&lt;SystemAudit&gt;&gt;.</returns>
		Task<IEnumerable<SystemAudit>> GetByUserIdPagedAsync(string userId, DateTime startDate, DateTime endDate, int page, int pageSize);

		/// <summary>
		/// Gets a date-ranged, paged set of system audits for a department.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="startDate">Inclusive lower bound on LoggedOn (UTC).</param>
		/// <param name="endDate">Exclusive upper bound on LoggedOn (UTC).</param>
		/// <param name="page">1-based page number.</param>
		/// <param name="pageSize">Page size.</param>
		/// <returns>Task&lt;IEnumerable&lt;SystemAudit&gt;&gt;.</returns>
		Task<IEnumerable<SystemAudit>> GetByDepartmentIdPagedAsync(int departmentId, DateTime startDate, DateTime endDate, int page, int pageSize);
	}
}
