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
	}
}
