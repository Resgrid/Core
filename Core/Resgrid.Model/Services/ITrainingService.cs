using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ITrainingService
	{
		/// <summary>
		/// Gets all trainings for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Training&gt;&gt;.</returns>
		Task<List<Training>> GetAllTrainingsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Saves the asynchronous.
		/// </summary>
		/// <param name="training">The training.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Training&gt;.</returns>
		Task<Training> SaveAsync(Training training, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the training by identifier asynchronous.
		/// </summary>
		/// <param name="trainingId">The training identifier.</param>
		/// <returns>Task&lt;Training&gt;.</returns>
		Task<Training> GetTrainingByIdAsync(int trainingId);

		/// <summary>
		/// Gets the training attachment by identifier asynchronous.
		/// </summary>
		/// <param name="trainingAttachmentId">The training attachment identifier.</param>
		/// <returns>Task&lt;TrainingAttachment&gt;.</returns>
		Task<TrainingAttachment> GetTrainingAttachmentByIdAsync(int trainingAttachmentId);

		/// <summary>
		/// Sets the training as viewed asynchronous.
		/// </summary>
		/// <param name="trainingId">The training identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;TrainingUser&gt;.</returns>
		Task<TrainingUser> SetTrainingAsViewedAsync(int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Records the training quiz result asynchronous.
		/// </summary>
		/// <param name="trainingId">The training identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="answersCorrect">The answers correct.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;TrainingUser&gt;.</returns>
		Task<TrainingUser> RecordTrainingQuizResultAsync(int trainingId, string userId, double answersCorrect, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the training asynchronous.
		/// </summary>
		/// <param name="trainingId">The training identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteTrainingAsync(int trainingId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Resets the user asynchronous.
		/// </summary>
		/// <param name="trainingId">The training identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;TrainingUser&gt;.</returns>
		Task<TrainingUser> ResetUserAsync(int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sends the initial training notice asynchronous.
		/// </summary>
		/// <param name="training">The training.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendInitialTrainingNoticeAsync(Training training);

		/// <summary>
		/// Gets the trainings to notify asynchronous.
		/// </summary>
		/// <param name="currentTime">The current time.</param>
		/// <returns>Task&lt;List&lt;Training&gt;&gt;.</returns>
		Task<List<Training>> GetTrainingsToNotifyAsync(DateTime currentTime);

		/// <summary>
		/// Marks as notified asynchronous.
		/// </summary>
		/// <param name="trainingId">The training identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Training&gt;.</returns>
		Task<Training> MarkAsNotifiedAsync(int trainingId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
