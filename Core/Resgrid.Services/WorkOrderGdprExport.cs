using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
	public partial class GdprDataExportService
	{
		private readonly IWorkOrderRepository _workOrders;
		private async Task<object> BuildWorkOrderDataAsync(string userId, int departmentId)
		{
			if (_workOrders == null) throw new InvalidOperationException("Work-order export storage is unavailable.");
			async Task<T> Safe<T>(T row) where T : WorkOrderRow => await _checklistProtection.Value.ForDisplayAsync(departmentId, row, WorkOrderTables.Fields<T>());
			async Task<List<T>> Children<T>(int id) where T : WorkOrderRow
			{
				var result = new List<T>();
				for (var skip = 0; ; skip += 500) { var batch = await _workOrders.ChildrenAsync<T>(departmentId, id, skip); foreach (var row in batch) result.Add(await Safe(row)); if (batch.Count < 500) return result; }
			}
			var orders = new List<object>();
			for (var page = 0; page <= 10000; page++)
			{
				var rows = await _workOrders.ListAsync(departmentId, new WorkOrderReadScope { All = true, UserId = userId }, new WorkOrderFilter { Page = page });
				foreach (var row in rows.Take(50))
				{
					var activity = await Children<WorkOrderActivity>(row.Id); var labor = await Children<WorkOrderLabor>(row.Id);
					if (row.CreatedBy != userId && row.AssignedToUserId != userId && row.CompletedBy != userId && row.VerifiedBy != userId && !activity.Any(a => a.CreatedBy == userId) && !labor.Any(l => l.UserId == userId || l.CreatedBy == userId)) continue;
					orders.Add(new { Order = await Safe(row), Activity = activity, Labor = labor, Parts = await Children<WorkOrderPart>(row.Id), Files = (await Children<WorkOrderFile>(row.Id)).Select(f => new { f.Id, f.DepartmentId, f.WorkOrderId, f.Content, f.ContentType, f.Size, f.Sha256, f.ScanState, f.WithdrawnOn, f.CreatedOn, f.CreatedBy }) });
				}
				if (rows.Count <= 50) return orders;
			}
			throw new InvalidOperationException("Work-order export exceeds the supported department size.");
		}
	}
}
