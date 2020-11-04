using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDepartmentLinksRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentLink}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentLink}" />
	public interface IDepartmentLinksRepository: IRepository<DepartmentLink>
	{
		/// <summary>
		/// Gets all links for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DepartmentLink&gt;&gt;.</returns>
		Task<IEnumerable<DepartmentLink>> GetAllLinksForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all links for department and identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="departmentLinkId">The department link identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DepartmentLink&gt;&gt;.</returns>
		Task<IEnumerable<DepartmentLink>> GetAllLinksForDepartmentAndIdAsync(int departmentId, int departmentLinkId);
	}
}
