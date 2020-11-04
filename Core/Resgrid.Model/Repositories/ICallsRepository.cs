using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallsRepository
	/// Implements the <see cref="Call" />
	/// </summary>
	/// <seealso cref="Call" />
	public interface ICallsRepository: IRepository<Call>
	{
		/// <summary>
		/// Gets all calls by department date range asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="startDate">The start date.</param>
		/// <param name="endDate">The end date.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllCallsByDepartmentDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate);

		/// <summary>
		/// Gets all closed calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllClosedCallsByDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all open calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllOpenCallsByDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all calls by department identifier logged on asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="loggedOn">The logged on.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllCallsByDepartmentIdLoggedOnAsync(int departmentId, DateTime loggedOn);
	}
}
