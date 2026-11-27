using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Mocks
{
	/// <summary>
	/// In-memory mock for <see cref="ITrainingUserRepository"/>
	/// </summary>
	public sealed class MockTrainingUserRepository : ITrainingUserRepository
	{
		private readonly List<TrainingUser> _users = new List<TrainingUser>();
		private int _nextId = 1;

		public Task<IEnumerable<TrainingUser>> GetAllAsync()
			=> Task.FromResult<IEnumerable<TrainingUser>>(_users.ToList());

		public Task<TrainingUser> GetByIdAsync(object id)
		{
			var intId = (int)id;
			var user = _users.FirstOrDefault(u => u.TrainingUserId == intId);
			return Task.FromResult(user);
		}

		public Task<IEnumerable<TrainingUser>> GetAllByDepartmentIdAsync(int departmentId)
			=> Task.FromResult<IEnumerable<TrainingUser>>(new List<TrainingUser>());

		public Task<IEnumerable<TrainingUser>> GetAllByUserIdAsync(string userId)
		{
			var result = _users.Where(u => u.UserId == userId).ToList();
			return Task.FromResult<IEnumerable<TrainingUser>>(result);
		}

		public Task<TrainingUser> InsertAsync(TrainingUser entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			entity.TrainingUserId = _nextId++;
			_users.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<TrainingUser> UpdateAsync(TrainingUser entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var existing = _users.FirstOrDefault(u => u.TrainingUserId == entity.TrainingUserId);
			if (existing != null)
			{
				_users.Remove(existing);
			}
			_users.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteAsync(TrainingUser entity, CancellationToken cancellationToken)
		{
			var existing = _users.FirstOrDefault(u => u.TrainingUserId == entity.TrainingUserId);
			if (existing != null)
			{
				_users.Remove(existing);
			}
			return Task.FromResult(true);
		}

		public Task<TrainingUser> SaveOrUpdateAsync(TrainingUser entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			if (entity.TrainingUserId == 0)
			{
				entity.TrainingUserId = _nextId++;
				_users.Add(entity);
			}
			else
			{
				var existing = _users.FirstOrDefault(u => u.TrainingUserId == entity.TrainingUserId);
				if (existing != null)
				{
					_users.Remove(existing);
				}
				_users.Add(entity);
			}
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteMultipleAsync(TrainingUser entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken)
			=> Task.FromResult(true);

		public Task<TrainingUser> GetTrainingUserByTrainingIdAndUserIdAsync(int trainingId, string userId)
		{
			var user = _users.FirstOrDefault(u => u.TrainingId == trainingId && u.UserId == userId);
			return Task.FromResult(user);
		}

		public Task<List<TrainingUser>> GetByTrainingIdAsync(int trainingId)
			=> Task.FromResult(_users.Where(u => u.TrainingId == trainingId).ToList());

		public Task<TrainingUser> InsertAssignmentAsync(TrainingUser assignment, CancellationToken cancellationToken = default)
		{
			if (_users.Any(u => u.TrainingId == assignment.TrainingId && string.Equals(u.UserId, assignment.UserId, System.StringComparison.OrdinalIgnoreCase)))
				return Task.FromResult<TrainingUser>(null);
			assignment.TrainingUserId = _users.Count == 0 ? 1 : _users.Max(u => u.TrainingUserId) + 1;
			_users.Add(assignment);
			return Task.FromResult(assignment);
		}

		/// <summary>Writes only the named columns onto the stored row, as the SQL does.</summary>
		public Task<int> UpdateColumnsAsync(TrainingUser assignment, IReadOnlyCollection<string> columns, CancellationToken cancellationToken = default)
		{
			var stored = _users.FirstOrDefault(u => u.TrainingUserId == assignment.TrainingUserId);
			if (stored == null)
				return Task.FromResult(0);
			if (!ReferenceEquals(stored, assignment))
				foreach (var column in columns)
				{
					var property = typeof(TrainingUser).GetProperty(column);
					property.SetValue(stored, property.GetValue(assignment));
				}
			return Task.FromResult(1);
		}

		public Task<int> MarkViewedAsync(int trainingUserId, System.DateTime utcNow, bool completeWithoutQuiz, CancellationToken cancellationToken = default)
		{
			var stored = _users.FirstOrDefault(u => u.TrainingUserId == trainingUserId && !u.RemovedOn.HasValue);
			if (stored == null)
				return Task.FromResult(0);
			stored.Viewed = true;
			stored.ViewedOn ??= utcNow;
			if (completeWithoutQuiz)
			{
				stored.Complete = true;
				stored.CompletedOn ??= utcNow;
			}
			return Task.FromResult(1);
		}

		public Task<int> RecordQuizResultAsync(int trainingUserId, double score, bool passed, int attemptCount, System.DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var stored = _users.FirstOrDefault(u => u.TrainingUserId == trainingUserId);
			if (stored == null)
				return Task.FromResult(0);
			stored.Score = score;
			stored.Passed = passed;
			stored.AttemptCount = attemptCount;
			stored.Viewed = true;
			stored.ViewedOn ??= utcNow;
			if (passed)
			{
				stored.Complete = true;
				stored.CompletedOn ??= utcNow;
			}
			return Task.FromResult(1);
		}

		public Task<int> DeleteAssignmentAsync(int trainingId, int trainingUserId, CancellationToken cancellationToken = default)
			=> Task.FromResult(_users.RemoveAll(u => u.TrainingId == trainingId && u.TrainingUserId == trainingUserId));

		public void SeedUser(TrainingUser user)
		{
			if (user.TrainingUserId == 0)
			{
				user.TrainingUserId = _nextId++;
			}
			else
			{
				_nextId = System.Math.Max(_nextId, user.TrainingUserId + 1);
			}
			_users.Add(user);
		}
	}
}