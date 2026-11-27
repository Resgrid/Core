using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ITrainingUserRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.TrainingUser}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.TrainingUser}" />
	public interface ITrainingUserRepository: IRepository<TrainingUser>
	{
		/// <summary>
		/// Gets the training user by training identifier and user identifier asynchronous.
		/// </summary>
		/// <param name="trainingId">The training identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;TrainingUser&gt;.</returns>
		Task<TrainingUser> GetTrainingUserByTrainingIdAndUserIdAsync(int trainingId, string userId);

		Task<List<TrainingUser>> GetByTrainingIdAsync(int trainingId);

		/// <summary>
		/// Inserts one assignment. Null when the member already has a row on the training (the unique index on TrainingId and
		/// UserId, T-D14): a double submit or a racing worker never duplicates an assignee.
		/// </summary>
		Task<TrainingUser> InsertAssignmentAsync(TrainingUser assignment, CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes only the named columns of one row (per-row updates, T-D14): saving a training never rewrites its assignees'
		/// progress, so a quiz result that lands during an edit is kept.
		/// </summary>
		Task<int> UpdateColumnsAsync(TrainingUser assignment, IReadOnlyCollection<string> columns, CancellationToken cancellationToken = default);

		/// <summary>Marks the row viewed; ViewedOn (and CompletedOn for a training without a quiz) are written once (T-D13).</summary>
		Task<int> MarkViewedAsync(int trainingUserId, DateTime utcNow, bool completeWithoutQuiz, CancellationToken cancellationToken = default);

		/// <summary>Records a quiz result: the score always (T-D1), Passed apart from Complete (T-D2), CompletedOn once.</summary>
		Task<int> RecordQuizResultAsync(int trainingUserId, double score, bool passed, int attemptCount, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<int> DeleteAssignmentAsync(int trainingId, int trainingUserId, CancellationToken cancellationToken = default);
	}
}
