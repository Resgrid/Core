using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Mocks
{
	/// <summary>
	/// In-memory mock for <see cref="ITrainingQuestionRepository"/>
	/// </summary>
	public sealed class MockTrainingQuestionRepository : ITrainingQuestionRepository
	{
		private readonly List<TrainingQuestion> _questions = new List<TrainingQuestion>();
		private int _nextId = 1;

		public Task<IEnumerable<TrainingQuestion>> GetAllAsync()
			=> Task.FromResult<IEnumerable<TrainingQuestion>>(_questions.ToList());

		public Task<TrainingQuestion> GetByIdAsync(object id)
		{
			var intId = (int)id;
			var question = _questions.FirstOrDefault(q => q.TrainingQuestionId == intId);
			return Task.FromResult(question);
		}

		public Task<IEnumerable<TrainingQuestion>> GetAllByDepartmentIdAsync(int departmentId)
			=> Task.FromResult<IEnumerable<TrainingQuestion>>(new List<TrainingQuestion>());

		public Task<IEnumerable<TrainingQuestion>> GetAllByUserIdAsync(string userId)
			=> Task.FromResult<IEnumerable<TrainingQuestion>>(new List<TrainingQuestion>());

		public Task<TrainingQuestion> InsertAsync(TrainingQuestion entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			entity.TrainingQuestionId = _nextId++;
			_questions.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<TrainingQuestion> UpdateAsync(TrainingQuestion entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var existing = _questions.FirstOrDefault(q => q.TrainingQuestionId == entity.TrainingQuestionId);
			if (existing != null)
			{
				_questions.Remove(existing);
			}
			_questions.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteAsync(TrainingQuestion entity, CancellationToken cancellationToken)
		{
			var existing = _questions.FirstOrDefault(q => q.TrainingQuestionId == entity.TrainingQuestionId);
			if (existing != null)
			{
				_questions.Remove(existing);
			}
			return Task.FromResult(true);
		}

		public Task<TrainingQuestion> SaveOrUpdateAsync(TrainingQuestion entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			if (entity.TrainingQuestionId == 0)
			{
				entity.TrainingQuestionId = _nextId++;
				_questions.Add(entity);
			}
			else
			{
				var existing = _questions.FirstOrDefault(q => q.TrainingQuestionId == entity.TrainingQuestionId);
				if (existing != null)
				{
					_questions.Remove(existing);
				}
				_questions.Add(entity);
			}
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteMultipleAsync(TrainingQuestion entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken)
			=> Task.FromResult(true);

		public Task<IEnumerable<TrainingQuestion>> GetTrainingQuestionsByTrainingIdAsync(int trainingId)
		{
			var result = _questions.Where(q => q.TrainingId == trainingId).ToList();
			return Task.FromResult<IEnumerable<TrainingQuestion>>(result);
		}

		public void SeedQuestion(TrainingQuestion question)
		{
			if (question.TrainingQuestionId == 0)
			{
				question.TrainingQuestionId = _nextId++;
			}
			else
			{
				_nextId = System.Math.Max(_nextId, question.TrainingQuestionId + 1);
			}
			_questions.Add(question);
		}
	}
}