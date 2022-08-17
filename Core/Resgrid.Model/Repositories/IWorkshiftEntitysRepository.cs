using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IWorkshiftEntitysRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.WorkshiftEntity}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.WorkshiftEntity}" />
	public interface IWorkshiftEntitysRepository : IRepository<WorkshiftEntity>
	{
		Task<IEnumerable<WorkshiftEntity>> GetWorkshiftEntitiesByWorkshiftIdAsync(string workshiftId);
	}
}
