using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Records;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Records
{
	/// <summary>
	/// records.deployments (plan §7.8): IRecordDeploymentsService.ListAsync(includeClosed: false) — open deployment Records the
	/// viewer may open (record visibility checked per order by the service). Order and incident numbers, status and dates only.
	/// </summary>
	[DashboardWidget("records.deployments")]
	public class RecordsDeploymentsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IRecordDeploymentsService _recordDeploymentsService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;

		public RecordsDeploymentsWidgetProvider(IRecordDeploymentsService recordDeploymentsService, IRecordsAuthorizationService recordsAuthorizationService)
		{
			_recordDeploymentsService = recordDeploymentsService;
			_recordsAuthorizationService = recordsAuthorizationService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 5);
			await RecordsWidgetSupport.RequireActiveMemberAsync(_recordsAuthorizationService, context);
			var orders = (await RecordsWidgetSupport.RunAsync(() => _recordDeploymentsService.ListAsync(context.DepartmentId, context.UserId, includeClosed: false)) ?? new List<RmsExternalOrder>())
				.Where(o => o != null && o.DepartmentId == context.DepartmentId && !o.DeletedOn.HasValue && o.Status != (int)RmsExternalOrderStatus.ClosedOut).ToList();

			return new RecordsDeploymentsWidgetData
			{
				Open = orders.Count,
				Deployments = orders.OrderByDescending(o => o.CreatedOn).Take(maxRows).Select(o => new RecordsDeploymentItem
				{
					OrderId = o.RmsExternalOrderId,
					RecordId = o.RecordId,
					OrderNumber = RecordsWidgetSupport.SafeText(o.OrderNumber),
					IncidentNumber = RecordsWidgetSupport.SafeText(o.IncidentNumber),
					Status = o.Status,
					MobilizedOn = o.MobilizedOn,
					CreatedOn = o.CreatedOn
				}).ToList()
			};
		}
	}
}
