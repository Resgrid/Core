using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IFormsService
	{
		/// <summary>
		/// Gets all forms for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Form&gt;&gt;.</returns>
		Task<List<Form>> GetAllFormsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all enabled (non-deleted) forms for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Form&gt;&gt;.</returns>
		Task<List<Form>> GetAllNonDeletedFormsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Saves the form asynchronous.
		/// </summary>
		/// <param name="form">The form.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Form&gt;.</returns>
		Task<Form> SaveFormAsync(Form form, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the form by identifier asynchronous.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <returns>Task&lt;Form&gt;.</returns>
		Task<Form> GetFormByIdAsync(string id);

		/// <summary>
		/// Deletes the form.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteForm(string id, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Enables a form by identifier asynchronous.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> EnableFormByIdAsync(string id);

		/// <summary>
		/// Disable a form by identifier asynchronous.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DisableFormByIdAsync(string id);

		/// <summary>
		/// Gets the active new call form by department identifier asynchronous.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <returns>Task&lt;Form&gt;.</returns>
		Task<Form> GetNewCallFormByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the active new call form by department identifier asynchronous.
		/// </summary>
		/// <param name="form">The identifier.</param>
		/// <returns>Task&lt;Form&gt;.</returns>
		FormAutomationData ProcessForm(Form form, string data);
	}
}
