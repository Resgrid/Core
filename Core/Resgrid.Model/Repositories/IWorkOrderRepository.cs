using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Model.Repositories
{
	public interface IWorkOrderRepository
	{
		Task LockDepartmentAsync(int departmentId);
		Task<int> ClaimNotificationAsync(WorkOrderNotification row, System.DateTime now);
		Task<bool> FinishNotificationAsync(WorkOrderNotification row, int state, System.DateTime now);
		Task<T> GetAsync<T>(int departmentId, int id, bool includeData = true) where T : WorkOrderRow;
		Task<WorkOrder> RequestAsync(int departmentId, string requestId);
		Task<List<WorkOrder>> ListAsync(int departmentId, WorkOrderReadScope scope, WorkOrderFilter filter);
		Task<List<T>> ChildrenAsync<T>(int departmentId, int orderId, int skip = 0) where T : WorkOrderRow;
		Task<int> NextNumberAsync(int departmentId, int year);
		Task AllocateAsync<T>(T row) where T : WorkOrderRow;
		Task WriteAsync<T>(T row) where T : WorkOrderRow;
	}
}
