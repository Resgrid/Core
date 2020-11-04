using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ITrainingQuestionRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.TrainingQuestion}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.TrainingQuestion}" />
	public interface ITrainingQuestionRepository: IRepository<TrainingQuestion>
	{
		/// <summary>
		/// Gets the training questions by training identifier asynchronous.
		/// </summary>
		/// <param name="trainingId">The training identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;TrainingQuestion&gt;&gt;.</returns>
		Task<IEnumerable<TrainingQuestion>> GetTrainingQuestionsByTrainingIdAsync(int trainingId);
	}
}
