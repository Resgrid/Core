using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
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

		public TrainingService(ITrainingRepository trainingRepository, ITrainingAttachmentRepository trainingAttachmentRepository,
			ITrainingUserRepository trainingUserRepository, ITrainingQuestionRepository trainingQuestionRepository, ICommunicationService communicationService, IDepartmentsService departmentService)
		{
			_trainingRepository = trainingRepository;
			_trainingAttachmentRepository = trainingAttachmentRepository;
			_trainingUserRepository = trainingUserRepository;
			_trainingQuestionRepository = trainingQuestionRepository;
			_communicationService = communicationService;
			_departmentService = departmentService;
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

			var saved = await _trainingRepository.SaveOrUpdateAsync(training, cancellationToken, true);

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

			return saved;
		}

		public async Task<Training> GetTrainingByIdAsync(int trainingId)
		{
			var training = await _trainingRepository.GetTrainingByTrainingIdAsync(trainingId);
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
			return await _trainingRepository.DeleteAsync(training, cancellationToken);
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
			foreach (var user in training.Users)
			{
				var message = String.Empty;
				if (training.ToBeCompletedBy.HasValue)
					message = string.Format("Training ({0}) due on {1}", training.Name,
						training.ToBeCompletedBy.Value.ToShortDateString());
				else
					message = string.Format("Training ({0}) assigned to you", training.Name);

				await _communicationService.SendNotificationAsync(user.UserId, training.DepartmentId, message, "New Training");
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
	}
}
