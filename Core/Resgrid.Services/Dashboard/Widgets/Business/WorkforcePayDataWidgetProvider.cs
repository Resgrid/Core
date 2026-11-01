using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Business;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Business
{
	/// <summary>
	/// workforce.payData (plan §7.9): WorkforceController.PayData's readiness and completeness for its default year (last calendar year,
	/// department time). Counts only.
	/// </summary>
	[DashboardWidget("workforce.payData")]
	public class WorkforcePayDataWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly ICaPayDataReportingService _payDataReportingService;
		private readonly IPayDataDemographicsService _payDataDemographicsService;

		public WorkforcePayDataWidgetProvider(ICaPayDataReportingService payDataReportingService, IPayDataDemographicsService payDataDemographicsService)
		{
			_payDataReportingService = payDataReportingService;
			_payDataDemographicsService = payDataDemographicsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var year = DashboardDepartmentClock.Today(context).Year - 1;
			var readiness = await _payDataReportingService.GetReadinessAsync(context.DepartmentId, year);
			var completeness = await _payDataDemographicsService.GetCompletenessAsync(context.DepartmentId, new DateTime(year, 12, 31));

			var active = completeness?.ActiveWorkers ?? 0;
			var withResponse = Math.Min(active, completeness?.WithResponse ?? 0);
			return new WorkforcePayDataWidgetData
			{
				ReportingYear = year,
				ActiveWorkers = active,
				WithResponse = withResponse,
				Missing = completeness?.Missing ?? 0,
				CompletenessPercent = active == 0 ? 100 : (int)Math.Round(withResponse * 100.0 / active),
				DueDate = readiness?.DueDate,
				ProfileAvailable = readiness?.ProfileAvailable == true,
				OpenRuns = readiness?.OpenRuns ?? 0,
				UnresolvedExceptions = readiness?.UnresolvedExceptions ?? 0,
				AnnualFactsMissing = readiness?.AnnualFactsMissing ?? 0,
				HasFrozenRun = readiness?.HasFrozenRun == true,
				HasCertifiedRun = readiness?.HasCertifiedRun == true
			};
		}
	}
}
