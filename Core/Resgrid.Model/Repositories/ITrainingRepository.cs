using System.Collections.Generic;
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
	}
}
