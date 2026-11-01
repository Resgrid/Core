using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Business;
using Resgrid.Model.Services;
using Resgrid.Model.Workforce;

namespace Resgrid.Services.Dashboard.Widgets.Business
{
	/// <summary>workforce.fieldCosting (plan §7.9): the newest cost runs (WorkforceController.CostRuns, InternalCosts_View). One read.</summary>
	[DashboardWidget("workforce.fieldCosting")]
	public class WorkforceFieldCostingWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IFieldCostingService _fieldCostingService;

		public WorkforceFieldCostingWidgetProvider(IFieldCostingService fieldCostingService)
		{
			_fieldCostingService = fieldCostingService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var max = context.GetInt("maxRows", 5);
			var runs = (await _fieldCostingService.GetRunsAsync(context.DepartmentId, 0, max) ?? new List<FieldCostRun>())
				.Where(r => r.DepartmentId == context.DepartmentId && !r.IsDeleted)
				.OrderByDescending(r => r.AddedOn).Take(max).ToList();

			return new WorkforceFieldCostingWidgetData
			{
				OpenRuns = runs.Count(r => r.Status is (int)FieldCostRunStatuses.Draft or (int)FieldCostRunStatuses.NeedsReview),
				MissingInputs = runs.Sum(r => r.MissingInputCount),
				Runs = runs.Select(r => new WorkforceFieldCostRunRow
				{
					FieldCostRunId = r.FieldCostRunId,
					ContextType = Enum.GetName(typeof(FieldCostContextTypes), r.ContextType) ?? r.ContextType.ToString(),
					Status = Enum.GetName(typeof(FieldCostRunStatuses), r.Status) ?? r.Status.ToString(),
					Currency = r.Currency,
					TotalLoadedCost = r.TotalLoadedCost,
					RevenueAmount = r.RevenueAmount,
					ContributionMargin = r.ContributionMargin,
					ContributionMarginPercent = r.ContributionMarginPercent,
					MissingInputCount = r.MissingInputCount,
					ThroughDate = r.ThroughDate,
					AddedOn = r.AddedOn
				}).ToList()
			};
		}
	}
}
