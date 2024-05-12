using Resgrid.Model.Identity;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDeleteRepository
	/// </summary>
	public interface IDeleteRepository
	{
		/// <summary>
		/// Deletes a Department
		/// </summary>
		/// <returns>Boolean, true if successful</returns>
		Task<bool> DeleteDepartmentAndUsersAsync(int departmentId);
	}
}
