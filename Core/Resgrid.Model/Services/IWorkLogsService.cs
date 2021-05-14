using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IWorkLogsService
	/// </summary>
	public interface IWorkLogsService
	{
		/// <summary>
		/// Gets all logs for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;Log&gt;&gt;.</returns>
		Task<List<Log>> GetAllLogsForUserAsync(string userId);

		/// <summary>
		/// Gets all logs for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Log&gt;&gt;.</returns>
		Task<List<Log>> GetAllLogsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all call logs for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;CallLog&gt;&gt;.</returns>
		Task<List<CallLog>> GetAllCallLogsForUserAsync(string userId);

		/// <summary>
		/// Gets the work log by identifier asynchronous.
		/// </summary>
		/// <param name="logId">The log identifier.</param>
		/// <returns>Task&lt;Log&gt;.</returns>
		Task<Log> GetWorkLogByIdAsync(int logId);

		/// <summary>
		/// Gets the call log by identifier asynchronous.
		/// </summary>
		/// <param name="callLogId">The call log identifier.</param>
		/// <returns>Task&lt;CallLog&gt;.</returns>
		Task<CallLog> GetCallLogByIdAsync(int callLogId);

		/// <summary>
		/// Saves the log asynchronous.
		/// </summary>
		/// <param name="log">The log.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Log&gt;.</returns>
		Task<Log> SaveLogAsync(Log log, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the logs for call asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;List&lt;Log&gt;&gt;.</returns>
		Task<List<Log>> GetLogsForCallAsync(int callId);

		/// <summary>
		/// Gets all logs by department date range asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="logType">Type of the log.</param>
		/// <param name="start">The start.</param>
		/// <param name="end">The end.</param>
		/// <returns>Task&lt;List&lt;Log&gt;&gt;.</returns>
		Task<List<Log>> GetAllLogsByDepartmentDateRangeAsync(int departmentId, LogTypes logType, DateTime start, DateTime end);

		/// <summary>
		/// Saves the call log asynchronous.
		/// </summary>
		/// <param name="log">The log.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;CallLog&gt;.</returns>
		Task<CallLog> SaveCallLogAsync(CallLog log, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the call log asynchronous.
		/// </summary>
		/// <param name="callLogId">The call log identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteCallLogAsync(int callLogId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the log asynchronous.
		/// </summary>
		/// <param name="logId">The log identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteLogAsync(int logId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the call logs for call asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;List&lt;CallLog&gt;&gt;.</returns>
		Task<List<CallLog>> GetCallLogsForCallAsync(int callId);

		/// <summary>
		/// Saves the log attachment asynchronous.
		/// </summary>
		/// <param name="attachment">The attachment.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;LogAttachment&gt;.</returns>
		Task<LogAttachment> SaveLogAttachmentAsync(LogAttachment attachment, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the attachment by identifier asynchronous.
		/// </summary>
		/// <param name="attachmentId">The attachment identifier.</param>
		/// <returns>Task&lt;LogAttachment&gt;.</returns>
		Task<LogAttachment> GetAttachmentByIdAsync(int attachmentId);

		/// <summary>
		/// Gets the attachments for log asynchronous.
		/// </summary>
		/// <param name="logId">The log identifier.</param>
		/// <returns>Task&lt;List&lt;LogAttachment&gt;&gt;.</returns>
		Task<List<LogAttachment>> GetAttachmentsForLogAsync(int logId);

		/// <summary>
		/// Clears the group for logs asynchronous.
		/// </summary>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> ClearGroupForLogsAsync(int departmentGroupId, CancellationToken cancellationToken = default(CancellationToken));

		Task<Log> PopulateLogData(Log log, bool getUsers, bool getUnits);

		/// <summary>
		/// Gets the log years for a department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;string&gt;&gt;.</returns>
		Task<List<string>> GetLogYearsByDeptartmentAsync(int departmentId);

		/// <summary>
		/// Gets all logs for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="year">The year.</param>
		/// <returns>Task&lt;List&lt;Log&gt;&gt;.</returns>
		Task<List<Log>> GetAllLogsForDepartmentAndYearAsync(int departmentId, string year);
	}
}
