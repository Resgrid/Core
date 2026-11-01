using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Business;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Business
{
	/// <summary>bids.pipeline (plan §7.9): counts by status in one grouped read (IBidsService.GetBidPipelineAsync).</summary>
	[DashboardWidget("bids.pipeline")]
	public class BidsPipelineWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IBidsService _bidsService;

		public BidsPipelineWidgetProvider(IBidsService bidsService)
		{
			_bidsService = bidsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var days = context.GetInt("expiringDays", 14);
			var pipeline = await _bidsService.GetBidPipelineAsync(context.DepartmentId, context.NowUtc, context.NowUtc.AddDays(days)) ?? new BidPipelineSummary();

			return new BidsPipelineWidgetData
			{
				Draft = pipeline.Count(BidStatuses.Draft),
				Submitted = pipeline.Count(BidStatuses.Submitted),
				Accepted = pipeline.Count(BidStatuses.Accepted),
				Declined = pipeline.Count(BidStatuses.Declined),
				Expired = pipeline.Count(BidStatuses.Expired),
				Withdrawn = pipeline.Count(BidStatuses.Withdrawn),
				ExpiringSoon = pipeline.ExpiringSoon,
				ExpiringDays = days
			};
		}
	}
}
