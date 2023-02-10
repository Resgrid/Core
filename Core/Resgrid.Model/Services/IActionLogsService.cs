using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IActionLogsService
	/// </summary>
	public interface IActionLogsService
	{
		/// <summary>
		/// Gets all action logs for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;ActionLog&gt;&gt;.</returns>
		Task<List<ActionLog>> GetAllActionLogsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Invalidates the action logs.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		void InvalidateActionLogs(int departmentId);

		/// <summary>
		/// Gets the last action logs for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="forceDisableAutoAvailable">if set to <c>true</c> [force disable automatic available].</param>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;List&lt;ActionLog&gt;&gt;.</returns>
		Task<List<ActionLog>> GetLastActionLogsForDepartmentAsync(int departmentId, bool forceDisableAutoAvailable = false, bool bypassCache = false);

		/// <summary>
		/// Gets all action logs for user.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;ActionLog&gt;&gt;.</returns>
		Task<List<ActionLog>> GetAllActionLogsForUser(string userId);

		/// <summary>
		/// Gets all action logs for user in date range asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="startDate">The start date.</param>
		/// <param name="endDate">The end date.</param>
		/// <returns>Task&lt;List&lt;ActionLog&gt;&gt;.</returns>
		Task<List<ActionLog>> GetAllActionLogsForUserInDateRangeAsync(string userId, DateTime startDate, DateTime endDate);

		/// <summary>
		/// Gets the last action log for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> GetLastActionLogForUserAsync(string userId, int? departmentId = null);

		/// <summary>
		/// Gets the last action log for user no limit asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> GetLastActionLogForUserNoLimitAsync(string userId);

		/// <summary>
		/// Gets the previous action log asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="actionLogId">The action log identifier.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> GetPreviousActionLogAsync(string userId, int actionLogId);

		/// <summary>
		/// Saves the action log asynchronous.
		/// </summary>
		/// <param name="actionLog">The action log.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> SaveActionLogAsync(ActionLog actionLog, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Saves all action logs asynchronous.
		/// </summary>
		/// <param name="actionLogs">The action logs.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SaveAllActionLogsAsync(List<ActionLog> actionLogs, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the user action asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="actionType">Type of the action.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the user action asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="actionType">Type of the action.</param>
		/// <param name="location">The location.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the user action asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="actionType">Type of the action.</param>
		/// <param name="location">The location.</param>
		/// <param name="note">The note.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location,
			string note, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the user action asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="actionType">Type of the action.</param>
		/// <param name="location">The location.</param>
		/// <param name="destinationId">The destination identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location,
			int destinationId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the user action asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="actionType">Type of the action.</param>
		/// <param name="location">The location.</param>
		/// <param name="destinationId">The destination identifier.</param>
		/// <param name="note">The note.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location,
			int destinationId, string note, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the user action asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="actionType">Type of the action.</param>
		/// <param name="location">The location.</param>
		/// <param name="destinationId">The destination identifier.</param>
		/// <param name="destinationType">Type of the destination.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location,
			int destinationId, int destinationType, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the user action asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="actionType">Type of the action.</param>
		/// <param name="location">The location.</param>
		/// <param name="destinationId">The destination identifier.</param>
		/// <param name="destinationType">Type of the destination.</param>
		/// <param name="note">The note.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> SetUserActionAsync(string userId, int departmentId, int actionType, string location,
			int destinationId, int destinationType, string note, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the action for entire department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="actionType">Type of the action.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SetActionForEntireDepartmentAsync(int departmentId, int actionType, string note);

		/// <summary>
		/// Sets the action for department group asynchronous.
		/// </summary>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <param name="actionType">Type of the action.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SetActionForDepartmentGroupAsync(int departmentGroupId, int actionType, string note);

		/// <summary>
		/// Deletes the action logs for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteActionLogsForUserAsync(string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes all action logs for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteAllActionLogsForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the action log by identifier asynchronous.
		/// </summary>
		/// <param name="actionLogId">The action log identifier.</param>
		/// <returns>Task&lt;ActionLog&gt;.</returns>
		Task<ActionLog> GetActionLogByIdAsync(int actionLogId);

		/// <summary>
		/// Gets the action logs for call asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;List&lt;ActionLog&gt;&gt;.</returns>
		Task<List<ActionLog>> GetActionLogsForCallAsync(int departmentId, int callId);

		Task<List<ActionLog>> GetAllActionLogsInDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate);
	}
}
