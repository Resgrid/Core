using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IResourceOrdersRepository
	{
		Task<List<ResourceOrder>> GetAll();
		Task<List<ResourceOrder>> GetAllOpen();
		Task<ResourceOrder> GetOrderById(int id);
		Task<List<ResourceOrder>> GetOrdersByDepartmentId(int departmentId);
		Task<List<ResourceOrder>> GetOpenOrdersByDepartmentId(int departmentId);
		Task<ResourceOrderSetting> GetOrderSettingById(int id);
		Task<ResourceOrderSetting> GetOrderSettingByDepartmentId(int departmentId);
		Task<List<ResourceOrder>> GetAllOpenOrdersByRange(int departmentId);
		Task<List<ResourceOrder>> GetAllOpenOrdersUnrestricted(int departmentId);
		Task<List<ResourceOrder>> GetAllOpenOrdersLinked(int departmentId);
		Task<ResourceOrderSetting> SaveSettings(ResourceOrderSetting settings);
		Task<ResourceOrder> SaveOrder(ResourceOrder order);
		Task<ResourceOrderFill> SaveFill(ResourceOrderFill fill);
		Task UpdateFillStatus(int fillId, string userId, bool accepted);
	}
}