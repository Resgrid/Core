using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IDepartmentLinksService
	{
		/// <summary>
		/// Gets all links for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;DepartmentLink&gt;&gt;.</returns>
		Task<List<DepartmentLink>> GetAllLinksForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the link by identifier asynchronous.
		/// </summary>
		/// <param name="linkId">The link identifier.</param>
		/// <returns>Task&lt;DepartmentLink&gt;.</returns>
		Task<DepartmentLink> GetLinkByIdAsync(int linkId);

		/// <summary>
		/// Saves the asynchronous.
		/// </summary>
		/// <param name="link">The link.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DepartmentLink&gt;.</returns>
		Task<DepartmentLink> SaveAsync(DepartmentLink link, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Doeses the link exist asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="departmentLinkId">The department link identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DoesLinkExistAsync(int departmentId, int departmentLinkId);

		/// <summary>
		/// Gets the department by link code asynchronous.
		/// </summary>
		/// <param name="code">The code.</param>
		/// <returns>Task&lt;Department&gt;.</returns>
		Task<Department> GetDepartmentByLinkCodeAsync(string code);
	}
}
