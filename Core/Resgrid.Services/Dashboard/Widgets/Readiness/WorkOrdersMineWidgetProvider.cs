using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Readiness;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services.Dashboard.Widgets.Readiness
{
	/// <summary>
	/// workOrders.mine (plan §7.7): IWorkOrdersService.ListAsync with the list's "assigned to me" filter (first page, the read
	/// scope applied by the service), open work orders only, most urgent first.
	/// </summary>
	[DashboardWidget("workOrders.mine")]
	public class WorkOrdersMineWidgetProvider : IDashboardWidgetDataProvider
	{
		private static readonly WorkOrderStatus[] Terminal = { WorkOrderStatus.Completed, WorkOrderStatus.Closed, WorkOrderStatus.Rejected, WorkOrderStatus.Duplicate, WorkOrderStatus.Cancelled };

		private readonly IWorkOrdersService _workOrdersService;

		public WorkOrdersMineWidgetProvider(IWorkOrdersService workOrdersService)
		{
			_workOrdersService = workOrdersService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 5);
			var actor = ReadinessWidgetSupport.ChecklistActor(context);
			var page = await ReadinessWidgetSupport.WorkOrdersAsync(() => _workOrdersService.ListAsync(actor, new WorkOrderFilter { AssignedToMe = true })) ?? new WorkOrderPage();
			var now = context.NowUtc;

			var open = (page.Items ?? new List<WorkOrderSummary>()).Where(o => o != null && !Terminal.Contains(o.Status)).Select(o =>
			{
				var overdue = o.DueOn.HasValue && o.DueOn < now;
				return new WorkOrdersMineItem
				{
					Id = o.Id,
					Number = o.Number,
					Title = ReadinessWidgetSupport.SafeText(o.Title),
					Status = (int)o.Status,
					Priority = (int)o.Priority,
					DueOn = o.DueOn,
					UpdatedOn = o.UpdatedOn,
					Overdue = overdue,
					Tone = o.Priority == WorkOrderPriority.Emergency || overdue ? "danger" : o.Priority == WorkOrderPriority.High ? "warning" : o.Priority == WorkOrderPriority.Normal ? "info" : "default"
				};
			}).ToList();

			return new WorkOrdersMineWidgetData
			{
				Open = open.Count,
				Overdue = open.Count(o => o.Overdue),
				HasMore = page.HasMore,
				Items = open.OrderByDescending(o => o.Priority).ThenByDescending(o => o.Overdue).ThenBy(o => o.DueOn ?? System.DateTime.MaxValue).ThenByDescending(o => o.UpdatedOn)
					.Take(maxRows).ToList()
			};
		}
	}
}
