using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface ITrainingService
	{
		List<Training> GetAllTrainingsForDepartment(int departmentId);
		Training Save(Training training);
		Training GetTrainingById(int trainingId);
		TrainingAttachment GetTrainingAttachmentById(int trainingAttachmentId);
		void SetTrainingAsViewed(int trainingId, string userId);
		void RecordTrainingQuizResult(int trainingId, string userId, double answersCorrect);
		void DeleteTraining(int trainingId);
		//Task<bool> SendInitalTrainingNotice(Training training);
		void SendInitalTrainingNotice(Training training);
		List<Training> GetTrainingsToNotify(DateTime currentTime);
		void MarkAsNotified(int trainingId);
		void ResetUser(int trainingId, string userId);
	}
}