using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.IncidentBackOffice;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>
	/// ibo.desk (plan §7.10, multiple instances). Operations and logistics: the desk summary the Operations / Logistics / Command desks
	/// render (GetDeskSummaryAsync — the module's own aggregate, several count reads). Finance: IncidentFinanceController's blocks over
	/// the activation's scope, each to its own claim, gated by the Business capability like the finance desk.
	/// </summary>
	[DashboardWidget("ibo.desk")]
	public class IncidentDeskWidgetProvider : IDashboardWidgetDataProvider
	{
		public const string Operations = "operations";
		public const string Logistics = "logistics";
		public const string Finance = "finance";

		private static readonly string[] OperationsKeys = { "participants.", "resources.", "requests." };
		private static readonly string[] LogisticsKeys = { "fills.", "demob.", "lodging.", "service." };
		private static readonly IReadOnlyList<int> LiveCommitments = new[]
		{
			(int)IncidentCommitmentStatuses.Open, (int)IncidentCommitmentStatuses.PartiallyReceived, (int)IncidentCommitmentStatuses.Received, (int)IncidentCommitmentStatuses.Closed
		};

		private readonly IIncidentBackOfficeAccessService _accessService;
		private readonly IIncidentActivationService _activationService;
		private readonly IIncidentLedgerService<IncidentActivation> _activations;
		private readonly IIncidentOperationalIntelligenceService _intelligenceService;
		private readonly IPermissionsService _permissionsService;
		private readonly IIncidentLedgerService<IncidentBudgetLine> _lines;
		private readonly IIncidentLedgerService<IncidentCommitment> _commitments;
		private readonly IIncidentLedgerService<IncidentCostProjection> _projections;
		private readonly IIncidentLedgerService<IncidentTimeRecord> _time;
		private readonly IIncidentLedgerService<VendorInvoiceReference> _invoices;

		public IncidentDeskWidgetProvider(IIncidentBackOfficeAccessService accessService, IIncidentActivationService activationService,
			IIncidentLedgerService<IncidentActivation> activations, IIncidentOperationalIntelligenceService intelligenceService, IPermissionsService permissionsService,
			IIncidentLedgerService<IncidentBudgetLine> lines, IIncidentLedgerService<IncidentCommitment> commitments, IIncidentLedgerService<IncidentCostProjection> projections,
			IIncidentLedgerService<IncidentTimeRecord> time, IIncidentLedgerService<VendorInvoiceReference> invoices)
		{
			_accessService = accessService;
			_activationService = activationService;
			_activations = activations;
			_intelligenceService = intelligenceService;
			_permissionsService = permissionsService;
			_lines = lines;
			_commitments = commitments;
			_projections = projections;
			_time = time;
			_invoices = invoices;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var access = await IncidentWidgetSupport.RequireReadAsync(_accessService, context.DepartmentId);
			var actor = IncidentWidgetSupport.Actor(context);
			var desk = context.GetString("desk", Operations);
			if (desk != Logistics && desk != Finance)
				desk = Operations;

			var activation = await IncidentWidgetSupport.ResolveActivationAsync(_activationService, _activations, actor,
				context.GetString(IncidentWidgetSupport.ActivationSetting, IncidentWidgetSupport.MostRecent), cancellationToken);
			var data = IncidentWidgetSupport.Header(new IncidentDeskWidgetData { Desk = desk }, access, activation);
			if (activation == null)
				return data;

			if (desk == Finance)
				return await FinanceAsync(context, actor, access, activation, data, cancellationToken);

			var summary = await _intelligenceService.GetDeskSummaryAsync(actor, activation.IncidentActivationId, cancellationToken);
			if (summary == null)
				return data;

			var prefixes = desk == Logistics ? LogisticsKeys : OperationsKeys;
			data.AsOf = summary.AsOf;
			data.Values = summary.Values.Where(v => prefixes.Any(p => v.Key.StartsWith(p, StringComparison.Ordinal)))
				.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
			data.Inbound = Windows(summary.Inbound);
			if (desk == Logistics)
				data.Outbound = Windows(summary.Outbound);
			return data;
		}

		private async Task<IncidentDeskWidgetData> FinanceAsync(DashboardWidgetContext context, IncidentActor actor, IncidentBackOfficeAccess access,
			IncidentActivation activation, IncidentDeskWidgetData data, CancellationToken cancellationToken)
		{
			if (!access.BusinessEnabled)
			{
				data.DeskUnavailable = true;
				return data;
			}

			var claims = await DashboardModulePermissions.HoldsEachAsync(_permissionsService, context.Viewer,
				IncidentWidgetSupport.BudgetView, IncidentWidgetSupport.ProcurementView, IncidentWidgetSupport.CostView, IncidentWidgetSupport.TimeView);
			bool budget = claims[0], procurement = claims[1], cost = claims[2], time = claims[3];
			if (!budget && !procurement && !cost && !time)
			{
				data.DeskUnavailable = true;
				return data;
			}

			var scope = await IncidentWidgetSupport.ScopeAsync(_activationService, actor, activation, cancellationToken);
			IncidentLedgerQuery Query() => new IncidentLedgerQuery { ActivationIds = scope, Take = 5000 };
			data.AsOf = context.NowUtc;

			if (budget || procurement)
			{
				data.Values["finance.committed"] = await _commitments.SumAsync(actor, "Amount", Query().WhereIn("Status", LiveCommitments), cancellationToken);
				data.Values["finance.open_commitments"] = await _commitments.CountAsync(actor,
					Query().WhereIn("Status", new[] { (int)IncidentCommitmentStatuses.Open, (int)IncidentCommitmentStatuses.PartiallyReceived }), cancellationToken);
				if (budget)
					data.Values["finance.budget"] = await _lines.SumAsync(actor, "RevisedAmount", Query(), cancellationToken);
			}

			if (cost)
			{
				var projection = (await _projections.ListAsync(actor, Query().Order("AddedOn", true).Page(0, 1), cancellationToken)).FirstOrDefault();
				if (projection != null)
				{
					data.Values["finance.cost_to_date"] = projection.CumulativeToDate;
					data.Values["finance.daily_burn"] = projection.DailyBurn;
					data.Values["finance.forecast_at_completion"] = projection.ForecastAtCompletion;
					data.ProjectedEndOn = projection.ProjectedEndOn;
					data.AsOf = projection.AddedOn;
				}
			}

			if (time)
			{
				data.Values["finance.time_awaiting_certification"] = await _time.CountAsync(actor,
					Query().WhereIn("Status", new[] { (int)IncidentTimeStatuses.Draft, (int)IncidentTimeStatuses.Submitted }), cancellationToken);
				data.Values["finance.time_awaiting_approval"] = await _time.CountAsync(actor, Query().Where("Status", (int)IncidentTimeStatuses.Certified), cancellationToken);
			}

			if (procurement)
				data.Values["finance.invoice_exceptions"] = await _invoices.CountAsync(actor, Query().Where("Status", (int)IncidentInvoiceStatuses.Exception), cancellationToken);

			return data;
		}

		private static List<IncidentArrivalWindow> Windows(IEnumerable<IncidentArrivalBucket> buckets) =>
			(buckets ?? Enumerable.Empty<IncidentArrivalBucket>()).Select(b => new IncidentArrivalWindow
			{
				Window = b.Window,
				Until = b.Until,
				Count = b.Count,
				Apparatus = b.Apparatus,
				Personnel = b.Personnel,
				Predicted = b.Predicted
			}).ToList();
	}
}
