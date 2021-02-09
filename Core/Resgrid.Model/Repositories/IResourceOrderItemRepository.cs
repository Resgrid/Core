using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IResourceOrderItemRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ResourceOrderItem}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ResourceOrderItem}" />
	public interface IResourceOrderItemRepository: IRepository<ResourceOrderItem>
	{
		Task<IEnumerable<ResourceOrderItem>> GetAllItemsByResourceItemIdAsync(int resourceOrderId);
	}
}
