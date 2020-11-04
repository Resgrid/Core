using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ILogService
	{
		/// <summary>
		/// Gets all asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;LogEntry&gt;&gt;.</returns>
		Task<List<LogEntry>> GetAllAsync();
		/// <summary>
		/// Gets the log by identifier asynchronous.
		/// </summary>
		/// <param name="logId">The log identifier.</param>
		/// <returns>Task&lt;LogEntry&gt;.</returns>
		Task<LogEntry> GetLogByIdAsync(int logId);
		/// <summary>
		/// Gets the new logs count for last5 days asynchronous.
		/// </summary>
		/// <returns>Task&lt;Dictionary&lt;System.String, System.Int32&gt;&gt;.</returns>
		Task<Dictionary<string, int>> GetNewLogsCountForLast5DaysAsync();
		/// <summary>
		/// Saves the process log asynchronous.
		/// </summary>
		/// <param name="log">The log.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ProcessLog&gt;.</returns>
		Task<ProcessLog> SaveProcessLogAsync(ProcessLog log, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>
		/// Gets the process log for type time asynchronous.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="id">The identifier.</param>
		/// <param name="time">The time.</param>
		/// <returns>Task&lt;ProcessLog&gt;.</returns>
		Task<ProcessLog> GetProcessLogForTypeTimeAsync(ProcessLogTypes type, int id, DateTime time);
		/// <summary>
		/// Sets the process log asynchronous.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="id">The identifier.</param>
		/// <param name="time">The time.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ProcessLog&gt;.</returns>
		Task<ProcessLog> SetProcessLogAsync(ProcessLogTypes type, int id, DateTime time, CancellationToken cancellationToken = default(CancellationToken));
	}
}
