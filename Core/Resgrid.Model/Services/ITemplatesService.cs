using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ITemplatesService
	{
		/// <summary>
		/// Gets all call quick templates for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;CallQuickTemplate&gt;&gt;.</returns>
		Task<List<CallQuickTemplate>> GetAllCallQuickTemplatesForDepartmentAsync(int departmentId);

		/// <summary>
		/// Saves the call quick template asynchronous.
		/// </summary>
		/// <param name="template">The template.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;CallQuickTemplate&gt;.</returns>
		Task<CallQuickTemplate> SaveCallQuickTemplateAsync(CallQuickTemplate template, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the call quick template by identifier asynchronous.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <returns>Task&lt;CallQuickTemplate&gt;.</returns>
		Task<CallQuickTemplate> GetCallQuickTemplateByIdAsync(int id);

		/// <summary>
		/// Deletes the call quick template asynchronous.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteCallQuickTemplateAsync(int id, CancellationToken cancellationToken = default(CancellationToken));
	}
}
