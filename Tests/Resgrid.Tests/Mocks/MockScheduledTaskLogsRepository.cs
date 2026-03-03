using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Mocks
{
	/// <summary>
	/// In-memory mock for <see cref="IScheduledTaskLogsRepository"/> that returns empty log
	/// collections without requiring a database connection.
	/// </summary>
	public sealed class MockScheduledTaskLogsRepository : IScheduledTaskLogsRepository
	{
		private readonly List<ScheduledTaskLog> _logs = new List<ScheduledTaskLog>();

		public Task<IEnumerable<ScheduledTaskLog>> GetAllAsync()
			=> Task.FromResult<IEnumerable<ScheduledTaskLog>>(_logs);

		public Task<ScheduledTaskLog> GetByIdAsync(object id)
			=> Task.FromResult<ScheduledTaskLog>(null);

		public Task<IEnumerable<ScheduledTaskLog>> GetAllByDepartmentIdAsync(int departmentId)
			=> Task.FromResult<IEnumerable<ScheduledTaskLog>>(new List<ScheduledTaskLog>());

		public Task<IEnumerable<ScheduledTaskLog>> GetAllByUserIdAsync(string userId)
			=> Task.FromResult<IEnumerable<ScheduledTaskLog>>(new List<ScheduledTaskLog>());

		public Task<ScheduledTaskLog> InsertAsync(ScheduledTaskLog entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			_logs.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<ScheduledTaskLog> UpdateAsync(ScheduledTaskLog entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
			=> Task.FromResult(entity);

		public Task<bool> DeleteAsync(ScheduledTaskLog entity, CancellationToken cancellationToken)
		{
			_logs.Remove(entity);
			return Task.FromResult(true);
		}

		public Task<ScheduledTaskLog> SaveOrUpdateAsync(ScheduledTaskLog entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			_logs.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteMultipleAsync(ScheduledTaskLog entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken)
			=> Task.FromResult(true);

		public Task<ScheduledTaskLog> GetLogForTaskAndDateAsync(int scheduledTaskId, DateTime timeStamp)
			=> Task.FromResult<ScheduledTaskLog>(null);

		public Task<IEnumerable<ScheduledTaskLog>> GetAllLogForDateAsync(DateTime timeStamp)
			=> Task.FromResult<IEnumerable<ScheduledTaskLog>>(new List<ScheduledTaskLog>());
	}
}

