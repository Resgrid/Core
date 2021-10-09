using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IFormsRepository
	/// Implements the <see cref="Form" />
	/// </summary>
	/// <seealso cref="Form" />
	public interface IFormsRepository : IRepository<Form>
	{
		/// <summary>
		/// Gets the dispatch protocol by identifier asynchronous.
		/// </summary>
		/// <param name="formId">The form identifier.</param>
		/// <returns>Task&lt;DispatchProtocol&gt;.</returns>
		Task<Form> GetFormByIdAsync(string formId);

		/// <summary>
		/// Gets the forms by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DispatchProtocol&gt;&gt;.</returns>
		Task<IEnumerable<Form>> GetFormsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the enabled (non-deleted) forms by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DispatchProtocol&gt;&gt;.</returns>
		Task<IEnumerable<Form>> GetNonDeletedFormsByDepartmentIdAsync(int departmentId);

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
	}
}
