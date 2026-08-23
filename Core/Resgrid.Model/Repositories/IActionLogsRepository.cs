using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IActionLogsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ActionLog}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ActionLog}" />
	public interface IActionLogsRepository: IRepository<ActionLog>
	{
		/// <summary>
		/// Gets the last action logs for department asynchronous.
		/// </summary>
		/// <remarks>
		/// BREAKING CHANGE: the <paramref name="includeHiddenAndDisabled"/> parameter was added to this
		/// signature. The default value keeps ordinary call sites source compatible, but implementers of
		/// this interface must add the parameter, precompiled assemblies bound to the three parameter
		/// overload must be rebuilt, and expression tree call sites (Moq Setup/Verify, LINQ expressions)
		/// must pass the argument explicitly because C# rejects omitted optional arguments there (CS0854).
		/// See Documentation/breaking-changes.md.
		/// </remarks>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="disableAutoAvailable">if set to <c>true</c> [disable automatic available].</param>
		/// <param name="timeStamp">The time stamp.</param>
		/// <param name="includeHiddenAndDisabled">if set to <c>true</c> include logs for hidden and disabled department members.</param>
		/// <returns>Task&lt;IEnumerable&lt;ActionLog&gt;&gt;.</returns>
		Task<IEnumerable<ActionLog>> GetLastActionLogsForDepartmentAsync(int departmentId, bool disableAutoAvailable, DateTime timeStamp, bool includeHiddenAndDisabled = false);

		/// <summary>
		/// Gets all action logs for user.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ActionLog&gt;&gt;.</returns>
		Task<IEnumerable<ActionLog>> GetAllActionLogsForUser(string userId);

		/// <summary>
		/// Gets all action logs for user in date range asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="startDate">The start date.</param>
		/// <param name="endDate">The end date.</param>
		/// <returns>Task&lt;IEnumerable&lt;ActionLog&gt;&gt;.</returns>
		Task<IEnumerable<ActionLog>> GetAllActionLogsForUserInDateRangeAsync(string userId, DateTime startDate, DateTime endDate);

		/// <summary>
		/// Gets all action logs for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ActionLog&gt;&gt;.</returns>
		Task<IEnumerable<ActionLog>> GetAllActionLogsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the last action logs for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="disableAutoAvailable">if set to <c>true</c> [disable automatic available].</param>
		/// <param name="timeStamp">The time stamp.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> GetLastActionLogsForUserAsync(string userId, bool disableAutoAvailable, DateTime timeStamp);

		/// <summary>
		/// Gets the last action logs for user asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<IEnumerable<ActionLog>> GetActionLogsForCallAsync(int callId);

		/// <summary>
		/// Gets the previous action log asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="actionLogId">The action log identifier.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> GetPreviousActionLogAsync(string userId, int actionLogId);

		/// <summary>
		/// Gets the last action log for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> GetLastActionLogForUserAsync(string userId);

		/// <summary>
		/// Gets the action logs for call and types asynchronous.
		/// </summary>
		/// <param name="destinationId">The destination identifier.</param>
		/// <param name="types">The types.</param>
		/// <returns>Task&lt;IEnumerable&lt;ActionLog&gt;&gt;.</returns>
		Task<IEnumerable<ActionLog>> GetActionLogsForCallAndTypesAsync(int destinationId, List<int> types);

		Task<IEnumerable<ActionLog>> GetAllActionLogsInDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate);
	}
}
