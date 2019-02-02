using System;
using System.Collections.Generic;
using System.Linq;
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
		private readonly IGenericDataRepository<TrainingAttachment> _trainingAttachmentRepository;
		private readonly IGenericDataRepository<TrainingUser> _trainingUserRepository;
		private readonly ICommunicationService _communicationService;
		private readonly IDepartmentsService _departmentService;

		public TrainingService(ITrainingRepository trainingRepository, IGenericDataRepository<TrainingAttachment> trainingAttachmentRepository,
			IGenericDataRepository<TrainingUser> trainingUserRepository, ICommunicationService communicationService, IDepartmentsService departmentService)
		{
			_trainingRepository = trainingRepository;
			_trainingAttachmentRepository = trainingAttachmentRepository;
			_trainingUserRepository = trainingUserRepository;
			_communicationService = communicationService;
			_departmentService = departmentService;
		}

		public List<Training> GetAllTrainingsForDepartment(int departmentId)
		{
			return _trainingRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public Training Save(Training training)
		{
			training.Description = StringHelpers.SanitizeHtmlInString(training.Description);
			training.TrainingText = StringHelpers.SanitizeHtmlInString(training.TrainingText);

			_trainingRepository.SaveOrUpdate(training);

			return training;
		}

		public Training GetTrainingById(int trainingId)
		{
			return _trainingRepository.GetAll().FirstOrDefault(x => x.TrainingId == trainingId);
		}

		public TrainingAttachment GetTrainingAttachmentById(int trainingAttachmentId)
		{
			return _trainingAttachmentRepository.GetAll().FirstOrDefault(x => x.TrainingAttachmentId == trainingAttachmentId);
		}

		public void SetTrainingAsViewed(int trainingId, string userId)
		{
			var trainingUser =
				_trainingUserRepository.GetAll().FirstOrDefault(x => x.TrainingId == trainingId && x.UserId == userId);

			if (trainingUser != null)
			{
				trainingUser.Viewed = true;
				trainingUser.ViewedOn = DateTime.UtcNow;

				if (trainingUser.Training.Questions == null || trainingUser.Training.Questions.Count <= 0)
				{
					trainingUser.Complete = true;
					trainingUser.CompletedOn = DateTime.UtcNow;
				}

				_trainingUserRepository.SaveOrUpdate(trainingUser);
			}
		}

		public void RecordTrainingQuizResult(int trainingId, string userId, double answersCorrect)
		{
			var training = GetTrainingById(trainingId);
			var trainingUser = training.Users.FirstOrDefault(x => x.UserId == userId);

			if (trainingUser != null)
			{
				trainingUser.CompletedOn = DateTime.UtcNow;
				trainingUser.Complete = true;

				if (answersCorrect > 0)
					trainingUser.Score = Math.Round(answersCorrect/training.Questions.Count * 100, 2);

				_trainingUserRepository.SaveOrUpdate(trainingUser);
			}
		}

		public void DeleteTraining(int trainingId)
		{
			var training = GetTrainingById(trainingId);
			_trainingRepository.DeleteOnSubmit(training);
		}

		public void ResetUser(int trainingId, string userId)
		{
			var training = GetTrainingById(trainingId);
			var trainingUser = training.Users.FirstOrDefault(x => x.UserId == userId);

			if (trainingUser != null)
			{
				trainingUser.Viewed = false;
				trainingUser.ViewedOn = null;
				trainingUser.Complete = false;
				trainingUser.CompletedOn = null;
				trainingUser.Score = 0;

				_trainingUserRepository.SaveOrUpdate(trainingUser);
			}
		}

		//public async Task<bool> SendInitalTrainingNotice(Training training)
		public void SendInitalTrainingNotice(Training training)
		{
			//var task = Task.Run(() =>
			//{
				foreach (var user in training.Users)
				{
					var message = String.Empty;
					if (training.ToBeCompletedBy.HasValue)
						message = string.Format("Training ({0}) due on {1}", training.Name,
							training.ToBeCompletedBy.Value.ToShortDateString());
					else
						message = string.Format("Training ({0}) assigned to you", training.Name);

					_communicationService.SendNotification(user.UserId, training.DepartmentId, message, "New Training");
				}

				//return true;
			//});

			//return await task;
		}

		public List<Training> GetTrainingsToNotify(DateTime currentTime)
		{
			var trainingsToNotify = new List<Training>();

			var trainings = _trainingRepository.GetAllTrainings();

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
							d = _departmentService.GetDepartmentById(training.DepartmentId);

						if (d != null)
						{
							var localizedDate = TimeConverterHelper.TimeConverter(currentTime, d);
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

		public void MarkAsNotified(int trainingId)
		{
			var training = GetTrainingById(trainingId);
			training.Notified = DateTime.UtcNow;

			_trainingRepository.SaveOrUpdate(training);
		}
	}
}
