using System.Threading.Tasks;
using Resgrid.Model.Custom;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDepartmentsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Department}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Department}" />
	public interface IDepartmentsRepository: IRepository<Department>
	{
		/// <summary>
		/// Gets the department report asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;DepartmentReport&gt;.</returns>
		Task<DepartmentReport> GetDepartmentReportAsync(int departmentId);

		/// <summary>
		/// Gets the department with members by identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;Department&gt;.</returns>
		Task<Department> GetDepartmentWithMembersByIdAsync(int departmentId);

		/// <summary>
		/// Gets the department with members by name asynchronous.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns>Task&lt;Department&gt;.</returns>
		Task<Department> GetDepartmentWithMembersByNameAsync(string name);

		/// <summary>
		/// Gets the department for user by username asynchronous.
		/// </summary>
		/// <param name="userName">Name of the user.</param>
		/// <returns>Task&lt;Department&gt;.</returns>
		Task<Department> GetDepartmentForUserByUsernameAsync(string userName);

		/// <summary>
		/// Gets the department for user by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;Department&gt;.</returns>
		Task<Department> GetDepartmentForUserByUserIdAsync(string userId);

		/// <summary>
		/// Gets the validate user for department data asynchronous.
		/// </summary>
		/// <param name="userName">Name of the user.</param>
		/// <returns>Task&lt;ValidateUserForDepartmentResult&gt;.</returns>
		Task<ValidateUserForDepartmentResult> GetValidateUserForDepartmentDataAsync(string userName);

		/// <summary>
		/// Gets the by link code asynchronous.
		/// </summary>
		/// <param name="code">The code.</param>
		/// <returns>Task&lt;Department&gt;.</returns>
		Task<Department> GetByLinkCodeAsync(string code);

		/// <summary>
		/// Gets user department stats by department id and user id asynchronous.
		/// </summary>
		/// <param name="departmentId">the department id</param>
		/// <param name="userId">your userid</param>
		/// <returns></returns>
		Task<DepartmentStats> GetDepartmentStatsByDepartmentUserIdAsync(int departmentId, string userId);
	}
}
