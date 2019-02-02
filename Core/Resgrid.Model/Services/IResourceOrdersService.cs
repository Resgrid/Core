using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IResourceOrdersService
	{
		Task<List<ResourceOrder>> GetAll();
		Task<List<ResourceOrder>> GetAllOpen();
		Task<List<ResourceOrder>> GetOpenOrdersByDepartmentId(int departmentId);
		Task<List<ResourceOrder>> GetOpenAvailableOrders(int departmentId);
		Task<ResourceOrderSetting> GetSettingsByDepartmentId(int departmentId);
		Task<ResourceOrderSetting> SaveSettings(ResourceOrderSetting settings);
		Task<ResourceOrder> CreateOrder(ResourceOrder order);
		Task<ResourceOrder> GetOrderById(int orderId);
		Task<List<ResourceOrder>> GetAllOrdersByDepartmentId(int departmentId);
		Task<ResourceOrderItem> GetOrderItemById(int orderItemId);
		Task<ResourceOrderFill> CreateFill(ResourceOrderFill fill);
		Task<ResourceOrderFill> GetOrderFillById(int orderFillId);
		Task SetFillStatus(int fillId, string userId, bool accepted);
	}
}