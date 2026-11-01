using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Business;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Business
{
	/// <summary>
	/// invoicing.receivables (plan §7.9). Mirrors InvoicingController.Index: aging buckets, outstanding and overdue balances per
	/// currency; ReadOnly when the add-on lapsed (CanUseInvoicingAsync, the same one evaluation the page makes per request). The aging
	/// rows' invoice lists stay on the server.
	/// </summary>
	[DashboardWidget("invoicing.receivables")]
	public class InvoicingReceivablesWidgetProvider : IDashboardWidgetDataProvider
	{
		private const string CurrentBucket = "Current";

		private readonly IInvoicingService _invoicingService;
		private readonly IBusinessOperationsAccessService _businessOperationsAccessService;

		public InvoicingReceivablesWidgetProvider(IInvoicingService invoicingService, IBusinessOperationsAccessService businessOperationsAccessService)
		{
			_invoicingService = invoicingService;
			_businessOperationsAccessService = businessOperationsAccessService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var aging = await _invoicingService.GetAccountsReceivableAgingAsync(departmentId) ?? new InvoiceAgingReport();
			var buckets = aging.Buckets ?? new List<InvoiceAgingBucket>();
			var overdue = buckets.Where(b => b.Label != CurrentBucket).ToList();

			var overdueBalances = new SortedDictionary<string, decimal>(System.StringComparer.OrdinalIgnoreCase);
			foreach (var bucket in overdue)
				foreach (var (currency, amount) in bucket.BalancesByCurrency ?? new SortedDictionary<string, decimal>())
					InvoiceAgingReport.Accumulate(overdueBalances, currency, amount);

			return new InvoicingReceivablesWidgetData
			{
				ReadOnly = !await _businessOperationsAccessService.CanUseInvoicingAsync(departmentId),
				Buckets = buckets.Select(b => new InvoicingAgingBucket { Key = b.Label, Count = b.Count, Balances = Amounts(b.BalancesByCurrency) }).ToList(),
				Outstanding = Amounts(aging.BalancesByCurrency),
				Overdue = Amounts(overdueBalances),
				OpenCount = aging.TotalCount,
				OverdueCount = overdue.Sum(b => b.Count),
				DraftCount = await _invoicingService.CountInvoicesForDepartmentAsync(departmentId, new InvoiceListFilter { Statuses = new[] { (int)InvoiceStatus.Draft }, Take = 1 })
			};
		}

		private static List<DashboardCurrencyAmount> Amounts(IDictionary<string, decimal> balances) =>
			(balances ?? new Dictionary<string, decimal>()).Select(b => new DashboardCurrencyAmount { Currency = b.Key, Amount = b.Value }).ToList();
	}
}
