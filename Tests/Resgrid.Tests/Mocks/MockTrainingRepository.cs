using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Mocks
{
	/// <summary>
	/// In-memory mock for <see cref="ITrainingRepository"/> that stores trainings
	/// without requiring a database connection.
	/// </summary>
	public sealed class MockTrainingRepository : ITrainingRepository
	{
		private readonly List<Training> _trainings = new List<Training>();
		private int _nextId = 1;

		public List<Training> Trainings => _trainings;

		public Task<IEnumerable<Training>> GetAllAsync()
			=> Task.FromResult<IEnumerable<Training>>(_trainings.ToList());

		public Task<Training> GetByIdAsync(object id)
		{
			var intId = (int)id;
			var training = _trainings.FirstOrDefault(t => t.TrainingId == intId);
			return Task.FromResult(training);
		}

		public Task<IEnumerable<Training>> GetAllByDepartmentIdAsync(int departmentId)
		{
			var result = _trainings.Where(t => t.DepartmentId == departmentId).ToList();
			return Task.FromResult<IEnumerable<Training>>(result);
		}

		public Task<IEnumerable<Training>> GetAllByUserIdAsync(string userId)
			=> Task.FromResult<IEnumerable<Training>>(new List<Training>());

		public Task<Training> InsertAsync(Training entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			entity.TrainingId = _nextId++;
			_trainings.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<Training> UpdateAsync(Training entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var existing = _trainings.FirstOrDefault(t => t.TrainingId == entity.TrainingId);
			if (existing != null)
			{
				_trainings.Remove(existing);
			}
			_trainings.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteAsync(Training entity, CancellationToken cancellationToken)
		{
			var existing = _trainings.FirstOrDefault(t => t.TrainingId == entity.TrainingId);
			if (existing != null)
			{
				_trainings.Remove(existing);
			}
			return Task.FromResult(true);
		}

		public Task<Training> SaveOrUpdateAsync(Training entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			if (entity.TrainingId == 0)
			{
				entity.TrainingId = _nextId++;
				_trainings.Add(entity);
			}
			else
			{
				var existing = _trainings.FirstOrDefault(t => t.TrainingId == entity.TrainingId);
				if (existing != null)
				{
					_trainings.Remove(existing);
				}
				_trainings.Add(entity);
			}
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteMultipleAsync(Training entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken)
			=> Task.FromResult(true);

		public List<Training> GetAllTrainings()
			=> _trainings.ToList();

		public Task<IEnumerable<Training>> GetTrainingsByDepartmentIdAsync(int departmentId)
		{
			var result = _trainings.Where(t => t.DepartmentId == departmentId).ToList();
			return Task.FromResult<IEnumerable<Training>>(result);
		}

		public Task<Training> GetTrainingByTrainingIdAsync(int trainingId)
		{
			var training = _trainings.FirstOrDefault(t => t.TrainingId == trainingId);
			return Task.FromResult(training);
		}

		/// <summary>
		/// Helper method to seed test data
		/// </summary>
		/// <summary>Question counts per training, as the list read returns them (tests seed it).</summary>
		public Dictionary<int, int> QuestionCounts { get; } = new Dictionary<int, int>();

		/// <summary>Completions per training for CountCompletionsAsync (tests seed it).</summary>
		public Dictionary<int, int> Completions { get; } = new Dictionary<int, int>();

		public List<int> NoticeWorkTrainingIds { get; } = new List<int>();

		public Task<Dictionary<int, int>> GetQuestionCountsForDepartmentAsync(int departmentId)
			=> Task.FromResult(new Dictionary<int, int>(QuestionCounts));

		public Task<List<int>> GetTrainingIdsWithNoticeWorkAsync(System.DateTime utcNow, int maxLeadDays)
			=> Task.FromResult(NoticeWorkTrainingIds.ToList());

		public Task<List<Training>> GetTrainingsDueForRepeatAsync(System.DateTime utcNow, int take)
			=> Task.FromResult(_trainings.Where(t => !t.ArchivedOn.HasValue && t.RepeatMonths > 0 && t.NextRepeatOn.HasValue && t.NextRepeatOn.Value <= utcNow).Take(take).ToList());

		public Task<bool> SetArchivedAsync(int departmentId, int trainingId, System.DateTime? archivedOn, string userId, CancellationToken cancellationToken = default)
		{
			var training = _trainings.FirstOrDefault(t => t.TrainingId == trainingId && t.DepartmentId == departmentId);
			if (training == null)
				return Task.FromResult(false);
			training.ArchivedOn = archivedOn;
			training.ArchivedByUserId = archivedOn.HasValue ? userId : null;
			return Task.FromResult(true);
		}

		public Task<bool> SetNextRepeatOnAsync(int trainingId, System.DateTime? nextRepeatOn, CancellationToken cancellationToken = default)
		{
			var training = _trainings.FirstOrDefault(t => t.TrainingId == trainingId);
			if (training == null)
				return Task.FromResult(false);
			training.NextRepeatOn = nextRepeatOn;
			return Task.FromResult(true);
		}

		public Task<int> CountCompletionsAsync(int trainingId)
			=> Task.FromResult(Completions.TryGetValue(trainingId, out var count) ? count : 0);

		public void SeedTraining(Training training)
		{
			if (training.TrainingId == 0)
			{
				training.TrainingId = _nextId++;
			}
			else
			{
				_nextId = System.Math.Max(_nextId, training.TrainingId + 1);
			}
			_trainings.Add(training);
		}
	}
}