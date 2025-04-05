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
		/// Gets all closed calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// /// <param name="year">The year.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllClosedCallsByDepartmentYearAsync(int departmentId, string year);

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

		/// <summary>
		/// Gets all years a call was logged in for a department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;string&gt;&gt;.</returns>
		Task<IEnumerable<string>> SelectCallYearsByDeptAsync(int departmentId);

		/// <summary>
		/// Gets all calls by date range that are to be dispatched asynchronous.
		/// </summary>
		/// <param name="startDate">The start date.</param>
		/// <param name="endDate">The end date.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllNonDispatchedScheduledCallsWithinDateRange(DateTime startDate, DateTime endDate);

		/// <summary>
		/// Gets all non-dispatched scheduled calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets all calls by department and contact asynchronous.
		/// </summary>
		/// <param name="contactId">The contact identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllCallsByContactIdAsync(string contactId, int departmentId);

		Task<int> GetCallsCountByDepartmentDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate);
	}
}
