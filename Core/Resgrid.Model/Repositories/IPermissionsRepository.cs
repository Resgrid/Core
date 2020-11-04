using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IPermissionsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Permission}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Permission}" />
	public interface IPermissionsRepository: IRepository<Permission>
	{
		/// <summary>
		/// Gets the permission by department type asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="type">The type.</param>
		/// <returns>Task&lt;IEnumerable&lt;Permission&gt;&gt;.</returns>
		Task<Permission> GetPermissionByDepartmentTypeAsync(int departmentId, int type);
	}
}
