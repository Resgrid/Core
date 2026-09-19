using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 29 (Workforce &amp; Business Operations plan B5), every 15 minutes. Pass 1: Sent / PartiallyPaid invoices
	/// past DueOn become Overdue (idempotent; each transition publishes InvoiceOverdue once). Passes 2–4 (payment
	/// request reconciliation, connection re-verification, request expiry, event purge) arrive with Phase B2 (M0212).
	/// Cheap when no department has invoicing on: a single indexed query returns nothing.
	/// </summary>
	public sealed class InvoiceMaintenanceLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken ct)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var invoicing = scope.Resolve<IInvoicingService>();
				var access = scope.Resolve<IBusinessOperationsAccessService>();
				var overdue = await invoicing.MarkOverdueInvoicesAsync(DateTime.UtcNow, access.CanUseInvoicingAsync, ct);
				return Tuple.Create(true, $"Invoice maintenance: overdue={overdue}");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Invoice maintenance worker failed.");
				return Tuple.Create(false, "Invoice maintenance failed.");
			}
		}
	}
}
