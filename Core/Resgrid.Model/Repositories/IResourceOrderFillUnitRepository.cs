using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IResourceOrderFillUnitRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ResourceOrderFillUnit}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ResourceOrderFillUnit}" />
	public interface IResourceOrderFillUnitRepository: IRepository<ResourceOrderFillUnit>
	{
		Task<IEnumerable<ResourceOrderFillUnit>> GetAllResourceOrderFillUnitsByFillIdAsync(int resourceOrderFillId);
	}
}
