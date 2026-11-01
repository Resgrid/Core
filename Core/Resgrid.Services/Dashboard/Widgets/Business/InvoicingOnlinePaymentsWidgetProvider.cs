using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Business;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Business
{
	/// <summary>
	/// invoicing.onlinePayments (plan §7.9): the status block of Invoicing Settings' Online tab (IInvoicePaymentsService.GetStatusAsync,
	/// which masks the connection). Only switches, dates and the display name leave: no account id, token or provider error text.
	/// </summary>
	[DashboardWidget("invoicing.onlinePayments")]
	public class InvoicingOnlinePaymentsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IInvoicePaymentsService _invoicePaymentsService;

		public InvoicingOnlinePaymentsWidgetProvider(IInvoicePaymentsService invoicePaymentsService)
		{
			_invoicePaymentsService = invoicePaymentsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var status = await _invoicePaymentsService.GetStatusAsync(context.DepartmentId);
			if (status == null)
				return new InvoicingOnlinePaymentsWidgetData { BlockedReason = "payments_not_available_in_region" };

			var connection = status.Connection;
			return new InvoicingOnlinePaymentsWidgetData
			{
				AvailableInCluster = status.AvailableInCluster,
				FlagEnabled = status.FlagEnabled,
				EnabledByDepartment = status.EnabledByDepartment,
				CanCollect = status.CanCollect,
				BlockedReason = status.BlockedReason,
				Connected = connection != null && connection.IsUsable,
				ConnectionName = connection?.DisplayName,
				DefaultCurrency = connection?.DefaultCurrency,
				ConnectedOn = connection?.ConnectedOn,
				LastVerifiedOn = connection?.LastVerifiedOn,
				ConnectionHasError = !string.IsNullOrWhiteSpace(connection?.LastError),
				ChargesEnabled = status.Capabilities != null && status.Capabilities.TryGetValue("charges_enabled", out var charges) && charges,
				PayoutsEnabled = status.Capabilities != null && status.Capabilities.TryGetValue("payouts_enabled", out var payouts) && payouts
			};
		}
	}
}
