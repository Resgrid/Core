using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICustomStateRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CustomState}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CustomState}" />
	public interface ICustomStateRepository: IRepository<CustomState>
	{
		/// <summary>
		/// Gets the custom states by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CustomState&gt;&gt;.</returns>
		Task<IEnumerable<CustomState>> GetCustomStatesByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the custom states by identifier asynchronous.
		/// </summary>
		/// <param name="customStateId">The custom state identifier.</param>
		/// <returns>Task&lt;CustomState&gt;.</returns>
		Task<CustomState> GetCustomStatesByIdAsync(int customStateId);
	}
}
