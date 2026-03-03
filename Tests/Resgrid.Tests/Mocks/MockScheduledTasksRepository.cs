using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Tests.Helpers;

namespace Resgrid.Tests.Mocks
{
	/// <summary>
	/// In-memory mock for <see cref="IScheduledTasksRepository"/> that returns pre-seeded
	/// test data without requiring a database connection.
	/// </summary>
	public sealed class MockScheduledTasksRepository : IScheduledTasksRepository
	{
		private readonly List<ScheduledTask> _tasks = ScheduledTasksHelpers.CreateAllTestScheduledTasks();

		public Task<IEnumerable<ScheduledTask>> GetAllAsync()
			=> Task.FromResult<IEnumerable<ScheduledTask>>(_tasks);

		public Task<ScheduledTask> GetByIdAsync(object id)
		{
			var intId = (int)id;
			var task = _tasks.Find(t => t.ScheduledTaskId == intId);
			return Task.FromResult(task);
		}

		public Task<IEnumerable<ScheduledTask>> GetAllByDepartmentIdAsync(int departmentId)
		{
			var result = _tasks.FindAll(t => t.DepartmentId == departmentId);
			return Task.FromResult<IEnumerable<ScheduledTask>>(result);
		}

		public Task<IEnumerable<ScheduledTask>> GetAllByUserIdAsync(string userId)
		{
			var result = _tasks.FindAll(t => t.UserId == userId);
			return Task.FromResult<IEnumerable<ScheduledTask>>(result);
		}

		public Task<ScheduledTask> InsertAsync(ScheduledTask entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			_tasks.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<ScheduledTask> UpdateAsync(ScheduledTask entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
			=> Task.FromResult(entity);

		public Task<bool> DeleteAsync(ScheduledTask entity, CancellationToken cancellationToken)
		{
			_tasks.Remove(entity);
			return Task.FromResult(true);
		}

		public Task<ScheduledTask> SaveOrUpdateAsync(ScheduledTask entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var existing = _tasks.Find(t => t.ScheduledTaskId == entity.ScheduledTaskId);
			if (existing != null)
				_tasks.Remove(existing);
			_tasks.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteMultipleAsync(ScheduledTask entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken)
			=> Task.FromResult(true);

		public Task<IEnumerable<ScheduledTask>> GetAllActiveTasksForTypesAsync(List<int> types)
		{
			var result = _tasks.FindAll(t => types.Contains(t.TaskType) && t.Active);
			return Task.FromResult<IEnumerable<ScheduledTask>>(result);
		}

		public Task<IEnumerable<ScheduledTask>> GetAllUpcomingOrRecurringReportDeliveryTasksAsync()
			=> Task.FromResult<IEnumerable<ScheduledTask>>(new List<ScheduledTask>());
	}
}

