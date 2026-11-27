using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ITrainingRepository
	/// Implements the <see cref="Training" />
	/// </summary>
	/// <seealso cref="Training" />
	public interface ITrainingRepository: IRepository<Training>
	{
		/// <summary>
		/// Gets all trainings.
		/// </summary>
		/// <returns>List&lt;Training&gt;.</returns>
		List<Training> GetAllTrainings();

		/// <summary>
		/// Gets the trainings by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Training&gt;&gt;.</returns>
		Task<IEnumerable<Training>> GetTrainingsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the training by training identifier asynchronous.
		/// </summary>
		/// <param name="trainingId">The training identifier.</param>
		/// <returns>Task&lt;Training&gt;.</returns>
		Task<Training> GetTrainingByTrainingIdAsync(int trainingId);

		/// <summary>Question counts per training of the department (one query for a list, T-D22).</summary>
		Task<Dictionary<int, int>> GetQuestionCountsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Trainings that may have notifier work on this run: not archived, started, with an open assignment that was never
		/// announced or whose due moment is within <paramref name="maxLeadDays"/> days or past. Every department, one query.
		/// </summary>
		Task<List<int>> GetTrainingIdsWithNoticeWorkAsync(DateTime utcNow, int maxLeadDays);

		/// <summary>Trainings whose repeat is due (NextRepeatOn passed), not archived. Rows only.</summary>
		Task<List<Training>> GetTrainingsDueForRepeatAsync(DateTime utcNow, int take);

		/// <summary>Writes only the archive columns of one training of the department.</summary>
		Task<bool> SetArchivedAsync(int departmentId, int trainingId, DateTime? archivedOn, string userId, CancellationToken cancellationToken = default);

		/// <summary>Writes only NextRepeatOn.</summary>
		Task<bool> SetNextRepeatOnAsync(int trainingId, DateTime? nextRepeatOn, CancellationToken cancellationToken = default);

		/// <summary>Completed assignments plus completion history rows of the training (a training with none may be deleted).</summary>
		Task<int> CountCompletionsAsync(int trainingId);
	}
}
