using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IWorkshiftsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Workshift}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Workshift}" />
	public interface IWorkshiftsRepository : IRepository<Workshift>
	{
		Task<IEnumerable<Workshift>> GetAllWorkshiftAndDaysByDepartmentIdAsync(int departmentId);

		Task<Workshift> GetWorkshiftByIdAsync(string workshiftId);
	}
}
