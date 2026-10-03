using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class TrainingService : ITrainingService
	{
		private readonly ITrainingRepository _trainingRepository;
		private readonly ITrainingAttachmentRepository _trainingAttachmentRepository;
		private readonly ITrainingQuestionRepository _trainingQuestionRepository;
		private readonly ITrainingUserRepository _trainingUserRepository;
		private readonly ICommunicationService _communicationService;
		private readonly IDepartmentsService _departmentService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly Lazy<ISearchProjectionService> _searchProjections;

		public TrainingService(ITrainingRepository trainingRepository, ITrainingAttachmentRepository trainingAttachmentRepository,
			ITrainingUserRepository trainingUserRepository, ITrainingQuestionRepository trainingQuestionRepository, ICommunicationService communicationService, IDepartmentsService departmentService,
			IDepartmentSettingsService departmentSettingsService, Lazy<ISearchProjectionService> searchProjections = null)
		{
			_searchProjections = searchProjections;
			_trainingRepository = trainingRepository;
			_trainingAttachmentRepository = trainingAttachmentRepository;
			_trainingUserRepository = trainingUserRepository;
			_trainingQuestionRepository = trainingQuestionRepository;
			_communicationService = communicationService;
			_departmentService = departmentService;
			_departmentSettingsService = departmentSettingsService;
		}

		public async Task<List<Training>> GetAllTrainingsForDepartmentAsync(int departmentId)
		{
			var list = await _trainingRepository.GetTrainingsByDepartmentIdAsync(departmentId);

			foreach (var item in list)
			{
				item.Questions = (await _trainingQuestionRepository.GetTrainingQuestionsByTrainingIdAsync(item.TrainingId)).ToList();
			}

			return list.ToList();
		}

		public async Task<Training> SaveAsync(Training training, CancellationToken cancellationToken = default(CancellationToken))
		{
			training.Description = StringHelpers.SanitizeHtmlInString(training.Description);
			training.TrainingText = StringHelpers.SanitizeHtmlInString(training.TrainingText);

			// Questions are saved separately from the training row, so on an update reconcile them with what is stored
			// before writing anything (#180): posted questions that carry their id are updated in place, removed ones are
			// deleted. Re-adding every question as a new row duplicated the quiz on each edit and inflated the score divisor.
			List<TrainingQuestion> removedQuestions = null;
			if (training.TrainingId > 0 && training.Questions != null)
				removedQuestions = await ReconcileQuestionsAsync(training);

			var saved = await _trainingRepository.SaveOrUpdateAsync(training, cancellationToken, true);

			if (removedQuestions != null)
			{
				// TrainingQuestionAnswers cascade on delete. Quiz results only keep the score, so nothing references these ids.
				foreach (var removedQuestion in removedQuestions)
					await _trainingQuestionRepository.DeleteAsync(removedQuestion, cancellationToken);
			}

			if (saved.Questions != null && saved.Questions.Any())
			{
				var questions = saved.Questions.ToList();
				for (int i = 0; i < questions.Count; i++)
				{
					questions[i].TrainingId = saved.TrainingId;
					questions[i] = await _trainingQuestionRepository.SaveOrUpdateAsync(questions[i], cancellationToken);
				}

				saved.Questions = questions;
			}

			if (saved.Attachments != null && saved.Attachments.Any())
			{
				var attachments = saved.Attachments.ToList();
				for (int i = 0; i < attachments.Count; i++)
				{
					attachments[i].TrainingId = saved.TrainingId;
					attachments[i] = await _trainingAttachmentRepository.SaveOrUpdateAsync(attachments[i], cancellationToken);
				}

				saved.Attachments = attachments;
			}

			if (saved.Users != null && saved.Users.Any())
			{
				var users = saved.Users.ToList();
				for (int i = 0; i < users.Count; i++)
				{
					users[i].TrainingId = saved.TrainingId;
					users[i] = await _trainingUserRepository.SaveOrUpdateAsync(users[i], cancellationToken);
				}

				saved.Users = users;
			}

			if (_searchProjections != null) await _searchProjections.Value.ProjectTrainingAsync(saved, cancellationToken);
			return saved;
		}

		/// <summary>
		/// Matches the questions on an existing training against the stored ones and returns the stored questions that are
		/// no longer present (to delete). Question/answer ids that do not belong to this training (or question) are cleared
		/// so they are inserted as new rows instead of overwriting someone else's. The repository only removes an updated
		/// question's dropped answers when at least one stored answer id survives, so a question whose answers were all
		/// removed or replaced is recreated rather than updated.
		/// </summary>
		private async Task<List<TrainingQuestion>> ReconcileQuestionsAsync(Training training)
		{
			var storedQuestions = (await _trainingQuestionRepository.GetTrainingQuestionsByTrainingIdAsync(training.TrainingId))?.ToList();

			// The question read swallows errors and returns null; saving without it would duplicate every question again.
			if (storedQuestions == null)
				throw new InvalidOperationException($"Unable to load the existing questions for training {training.TrainingId}.");

			var keptQuestionIds = new HashSet<int>();

			foreach (var question in training.Questions)
			{
				var storedQuestion = question.TrainingQuestionId > 0 && !keptQuestionIds.Contains(question.TrainingQuestionId)
					? storedQuestions.FirstOrDefault(x => x.TrainingQuestionId == question.TrainingQuestionId)
					: null;

				if (storedQuestion == null)
				{
					ClearQuestionIds(question);
					continue;
				}

				var storedAnswerIds = new HashSet<int>(storedQuestion.Answers?.Select(x => x.TrainingQuestionAnswerId) ?? Enumerable.Empty<int>());
				var keptAnswerIds = new HashSet<int>();

				if (question.Answers != null)
				{
					foreach (var answer in question.Answers)
					{
						if (answer.TrainingQuestionAnswerId > 0 && storedAnswerIds.Contains(answer.TrainingQuestionAnswerId) && keptAnswerIds.Add(answer.TrainingQuestionAnswerId))
							continue;

						answer.TrainingQuestionAnswerId = 0;
					}
				}

				if (storedAnswerIds.Count > 0 && keptAnswerIds.Count == 0)
				{
					ClearQuestionIds(question);
					continue;
				}

				keptQuestionIds.Add(question.TrainingQuestionId);
			}

			return storedQuestions.Where(x => !keptQuestionIds.Contains(x.TrainingQuestionId)).ToList();
		}

		private static void ClearQuestionIds(TrainingQuestion question)
		{
			question.TrainingQuestionId = 0;

			if (question.Answers != null)
			{
				foreach (var answer in question.Answers)
				{
					answer.TrainingQuestionAnswerId = 0;
					answer.TrainingQuestionId = 0;
				}
			}
		}

		public async Task<Training> GetTrainingByIdAsync(int trainingId)
		{
			var training = await _trainingRepository.GetTrainingByTrainingIdAsync(trainingId);

			if (training == null)
				return null;

			training.Questions = (await _trainingQuestionRepository.GetTrainingQuestionsByTrainingIdAsync(training.TrainingId)).ToList();
			training.Attachments = (await _trainingAttachmentRepository.GetTrainingAttachmentsByTrainingIdAsync(trainingId)).ToList();

			return training;
		}

		public async Task<TrainingAttachment> GetTrainingAttachmentByIdAsync(int trainingAttachmentId)
		{
			return await _trainingAttachmentRepository.GetByIdAsync(trainingAttachmentId);
		}

		public async Task<TrainingUser> SetTrainingAsViewedAsync(int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var trainingUser = await _trainingUserRepository.GetTrainingUserByTrainingIdAndUserIdAsync(trainingId, userId);

			if (trainingUser != null)
			{
				trainingUser.Training = await GetTrainingByIdAsync(trainingId);
				trainingUser.Viewed = true;
				trainingUser.ViewedOn = DateTime.UtcNow;

				if (trainingUser.Training.Questions == null || trainingUser.Training.Questions.Count <= 0)
				{
					trainingUser.Complete = true;
					trainingUser.CompletedOn = DateTime.UtcNow;
				}

				return await _trainingUserRepository.SaveOrUpdateAsync(trainingUser, cancellationToken, true);
			}

			return null;
		}

		public async Task<TrainingUser> RecordTrainingQuizResultAsync(int trainingId, string userId, double answersCorrect, CancellationToken cancellationToken = default(CancellationToken))
		{
			var training = await GetTrainingByIdAsync(trainingId);
			var trainingUser = await _trainingUserRepository.GetTrainingUserByTrainingIdAndUserIdAsync(trainingId, userId);

			if (trainingUser != null)
			{
				trainingUser.CompletedOn = DateTime.UtcNow;
				trainingUser.Complete = true;

				if (answersCorrect > 0)
					trainingUser.Score = Math.Round(answersCorrect / training.Questions.Count * 100, 2);

				return await _trainingUserRepository.SaveOrUpdateAsync(trainingUser, cancellationToken);
			}

			return null;
		}

		public async Task<bool> DeleteTrainingAsync(int trainingId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var training = await GetTrainingByIdAsync(trainingId);
			var deleted = await _trainingRepository.DeleteAsync(training, cancellationToken);
			if (deleted && training != null && _searchProjections != null)
				await _searchProjections.Value.RemoveAsync(training.DepartmentId, SearchEntityTypes.Training, training.TrainingId.ToString(), cancellationToken);
			return deleted;
		}

		public async Task<TrainingUser> ResetUserAsync(int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var trainingUser = await _trainingUserRepository.GetTrainingUserByTrainingIdAndUserIdAsync(trainingId, userId);

			if (trainingUser != null)
			{
				trainingUser.Viewed = false;
				trainingUser.ViewedOn = null;
				trainingUser.Complete = false;
				trainingUser.CompletedOn = null;
				trainingUser.Score = 0;

				return await _trainingUserRepository.SaveOrUpdateAsync(trainingUser, cancellationToken);
			}

			return null;
		}


		public async Task<bool> SendInitialTrainingNoticeAsync(Training training)
		{
			if (training?.Users == null || !training.Users.Any() || !ConfigHelper.CanTransmit(training.DepartmentId))
				return false;

			// Same wording, title and sending number as the notifier worker's first notice (TrainingNotifierLogic).
			var message = String.Empty;
			if (training.ToBeCompletedBy.HasValue)
				message = string.Format("New Training ({0}) due on {1}", training.Name,
					training.ToBeCompletedBy.Value.ToShortDateString());
			else
				message = string.Format("New Training ({0}) assigned to you", training.Name);

			var department = await _departmentService.GetDepartmentByIdAsync(training.DepartmentId, false);
			var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(training.DepartmentId);

			foreach (var user in training.Users)
			{
				await _communicationService.SendNotificationAsync(user.UserId, training.DepartmentId, message, departmentNumber, department, "New Training Notice");
			}

			return true;
		}

		public async Task<List<Training>> GetTrainingsToNotifyAsync(DateTime currentTime)
		{
			var trainingsToNotify = new List<Training>();

			var trainings = await _trainingRepository.GetAllAsync();

			if (trainings != null && trainings.Any())
			{
				foreach (var training in trainings)
				{
					if (!training.Notified.HasValue)
					{
						trainingsToNotify.Add(training);
					}
					else
					{
						Department d;
						if (training.Department != null)
							d = training.Department;
						else
							d = await _departmentService.GetDepartmentByIdAsync(training.DepartmentId);

						if (d != null)
						{
							var localizedDate = currentTime.TimeConverter(d);
							var setToNotify = new DateTime(localizedDate.Year, localizedDate.Month, localizedDate.Day, 10, 0, 0, 0);

							if (localizedDate == setToNotify.Within(TimeSpan.FromMinutes(13)) && training.ToBeCompletedBy.HasValue)
							{
								if (localizedDate.AddDays(1).ToShortDateString() == training.ToBeCompletedBy.Value.ToShortDateString())
									trainingsToNotify.Add(training);
							}
						}
					}
				}
			}

			return trainingsToNotify;
		}

		public async Task<Training> MarkAsNotifiedAsync(int trainingId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var training = await GetTrainingByIdAsync(trainingId);
			training.Notified = DateTime.UtcNow;

			return await _trainingRepository.SaveOrUpdateAsync(training, cancellationToken);
		}

		public async Task<List<TrainingUser>> GetTrainingUsersForUserAsync(string userId)
		{
			var users = await _trainingUserRepository.GetAllByUserIdAsync(userId);
			if (users != null)
				return users.ToList();

			return new List<TrainingUser>();
		}
	}
}
