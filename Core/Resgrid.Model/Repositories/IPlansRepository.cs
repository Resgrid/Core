using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IPlansRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Plan}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Plan}" />
	public interface IPlansRepository: IRepository<Plan>
	{
		/// <summary>
		/// Gets the plan by plan identifier asynchronous.
		/// </summary>
		/// <param name="planId">The plan identifier.</param>
		/// <returns>Task&lt;Plan&gt;.</returns>
		Task<Plan> GetPlanByPlanIdAsync(int planId);
	}
}
