using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 29 (Workforce &amp; Business Operations plan B5 / B2.4), every 15 minutes. Pass 1: Sent / PartiallyPaid
	/// invoices past DueOn become Overdue (idempotent; each transition publishes InvoiceOverdue once). Pass 2: open
	/// online payment requests older than RequestReconcileAfterMinutes are read back from the provider so a lost
	/// webhook still lands; requests past their expiry are closed. Pass 3: connections not verified in a day are
	/// re-read (a revoked or charges-disabled account is downgraded). Pass 4: webhook bodies older than
	/// EventRetentionDays are purged. Passes 2–4 are no-ops while PaymentConnectConfig.Enabled is false.
	/// Cheap when no department has invoicing on: each pass is a single indexed query returning nothing.
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
				var now = DateTime.UtcNow;
				var overdue = await invoicing.MarkOverdueInvoicesAsync(now, access.CanUseInvoicingAsync, ct);

				var reconciled = 0; var expired = 0; var reverified = 0; var purged = 0;
				if (Resgrid.Config.PaymentConnectConfig.Enabled)
				{
					var payments = scope.Resolve<IInvoicePaymentsService>();
					reconciled = await Guarded(() => payments.ReconcileOpenRequestsAsync(now, ct), "reconcile", ct);
					expired = await Guarded(() => payments.ExpireStaleRequestsAsync(now, ct), "expire", ct);
					reverified = await Guarded(() => payments.ReverifyConnectionsAsync(now, ct), "reverify", ct);
					purged = await Guarded(() => payments.PurgeEventsAsync(now, ct), "purge", ct);
				}

				return Tuple.Create(true, $"Invoice maintenance: overdue={overdue} reconciled={reconciled} expired={expired} reverified={reverified} purged={purged}");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Invoice maintenance worker failed.");
				return Tuple.Create(false, "Invoice maintenance failed.");
			}
		}

		/// <summary>One failing pass never stops the others; the failure is logged and the pass retries next cycle.</summary>
		private static async Task<int> Guarded(Func<Task<int>> pass, string name, CancellationToken ct)
		{
			try { return await pass(); }
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, $"Invoice maintenance pass '{name}' failed.");
				return 0;
			}
		}
	}
}
