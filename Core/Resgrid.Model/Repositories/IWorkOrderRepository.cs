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
		Task<List<WorkOrder>> ReportOrdersAsync(int departmentId, WorkOrderReadScope scope, WorkOrderReportQuery query, int take, int[] ids = null);
		Task<List<T>> ReportChildrenAsync<T>(int departmentId, int[] orderIds, int afterId, int take) where T : WorkOrderRow;
		Task CaptureReportSnapshotAsync(WorkOrder row, System.DateTime recordedOn, int? activityId);
		Task<List<WorkOrderReportSnapshot>> ReportSnapshotsAsync(int departmentId, int[] orderIds, System.DateTime asOf);
		Task<List<WorkOrderReportSnapshot>> ReportSnapshotHistoryAsync(int departmentId, int orderId, long afterId);
		Task<List<WorkOrder>> ReportPacketOrdersAsync(int departmentId, WorkOrderReadScope scope, System.DateTime asOf, int[] unitIds, string[] assetIds, int afterId);
		Task<bool> HasUnrecoverableReportHistoryAsync(int departmentId, WorkOrderReadScope scope, System.DateTime asOf);
		Task<List<T>> ChildrenAsync<T>(int departmentId, int orderId, int skip = 0) where T : WorkOrderRow;
		Task<int> NextNumberAsync(int departmentId, int year);
		Task AllocateAsync<T>(T row) where T : WorkOrderRow;
		Task WriteAsync<T>(T row) where T : WorkOrderRow;
	}
}
