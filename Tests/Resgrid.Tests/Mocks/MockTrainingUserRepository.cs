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