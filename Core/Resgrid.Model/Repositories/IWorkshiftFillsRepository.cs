using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IWorkshiftFillsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.WorkshiftFill}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.WorkshiftFill}" />
	public interface IWorkshiftFillsRepository : IRepository<WorkshiftFill>
	{
		Task<IEnumerable<WorkshiftFill>> GetWorkshiftFillsByWorkshiftIdAsync(string workshiftId);
	}
}
