using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.IncidentBackOffice;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>
	/// ibo.finance (plan §7.10): the finance desk's projection block (IncidentCost_View, the widget's gate) and its budget total, shown
	/// only to IncidentBudget_View as the desk shows its budget lines. Business capability required, like the finance desk.
	/// </summary>
	[DashboardWidget("ibo.finance")]
	public class IncidentFinanceWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IIncidentBackOfficeAccessService _accessService;
		private readonly IIncidentActivationService _activationService;
		private readonly IIncidentLedgerService<IncidentActivation> _activations;
		private readonly IPermissionsService _permissionsService;
		private readonly IIncidentLedgerService<IncidentCostProjection> _projections;
		private readonly IIncidentLedgerService<IncidentBudgetLine> _lines;

		public IncidentFinanceWidgetProvider(IIncidentBackOfficeAccessService accessService, IIncidentActivationService activationService,
			IIncidentLedgerService<IncidentActivation> activations, IPermissionsService permissionsService,
			IIncidentLedgerService<IncidentCostProjection> projections, IIncidentLedgerService<IncidentBudgetLine> lines)
		{
			_accessService = accessService;
			_activationService = activationService;
			_activations = activations;
			_permissionsService = permissionsService;
			_projections = projections;
			_lines = lines;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var access = await IncidentWidgetSupport.RequireReadAsync(_accessService, context.DepartmentId, business: true);
			var actor = IncidentWidgetSupport.Actor(context);
			var activation = await IncidentWidgetSupport.ResolveActivationAsync(_activationService, _activations, actor,
				context.GetString(IncidentWidgetSupport.ActivationSetting, IncidentWidgetSupport.MostRecent), cancellationToken);
			var data = IncidentWidgetSupport.Header(new IncidentFinanceWidgetData(), access, activation);
			if (activation == null)
				return data;

			var scope = await IncidentWidgetSupport.ScopeAsync(_activationService, actor, activation, cancellationToken);
			var projection = (await _projections.ListAsync(actor, new IncidentLedgerQuery { ActivationIds = scope }.Order("AddedOn", true).Page(0, 1), cancellationToken)).FirstOrDefault();
			if (projection != null)
			{
				data.CostToDate = projection.CumulativeToDate;
				data.DailyBurn = projection.DailyBurn;
				data.ForecastAtCompletion = projection.ForecastAtCompletion;
				data.ProjectedEndOn = projection.ProjectedEndOn;
				data.ProjectionOn = projection.AddedOn;
				if (!string.IsNullOrWhiteSpace(projection.Currency))
					data.Currency = projection.Currency;
			}

			data.CanViewBudget = await DashboardModulePermissions.HoldsAnyAsync(_permissionsService, context.Viewer, IncidentWidgetSupport.BudgetView);
			if (data.CanViewBudget)
			{
				data.Budget = await _lines.SumAsync(actor, "RevisedAmount", new IncidentLedgerQuery { ActivationIds = scope, Take = 5000 }, cancellationToken);
				if (data.CostToDate.HasValue && data.Budget > 0)
					data.BudgetUsedPercent = (int)Math.Round(data.CostToDate.Value * 100m / data.Budget.Value);
			}

			return data;
		}
	}
}
