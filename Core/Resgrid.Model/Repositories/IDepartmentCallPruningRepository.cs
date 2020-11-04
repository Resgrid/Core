using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDepartmentCallPruningRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentCallPruning}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentCallPruning}" />
	public interface IDepartmentCallPruningRepository: IRepository<DepartmentCallPruning>
	{
		/// <summary>
		/// Gets the department call pruning settings asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;DepartmentCallPruning&gt;.</returns>
		Task<DepartmentCallPruning> GetDepartmentCallPruningSettingsAsync(int departmentId);
	}
}
