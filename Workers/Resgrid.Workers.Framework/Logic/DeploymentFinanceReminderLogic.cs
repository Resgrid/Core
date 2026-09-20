using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 32 (Workforce &amp; Business Operations plan C7), daily. Billable deployments with approved time reports
	/// unbilled longer than the reminder window (default 21 days), or completed with any unbilled report, produce one
	/// digest per department per day to the department administrators. Never generates or sends an invoice. The
	/// Cal OES MARS duties (C-M3): annual Salary Survey / Administrative Rate expiry, agreement expiry, released
	/// resources without a ready or submitted F-42, expense claims missing evidence, returned records and MARS
	/// invoices awaiting local approval → a value-minimized digest per department per day. Never auto-submits,
	/// approves, chooses a rate or changes an observed MARS state.
	/// </summary>
	public sealed class DeploymentFinanceReminderLogic
	{
		public const int UnbilledDays = 21;

		public async Task<Tuple<bool, string>> Process(CancellationToken ct)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var engine = scope.Resolve<IContractorBillingEngine>();
				var access = scope.Resolve<IBusinessOperationsAccessService>();
				var notified = await engine.RunFinanceReminderSweepAsync(DateTime.UtcNow, UnbilledDays, access.CanUseContractorBillingAsync, ct);
				var mars = scope.Resolve<ICalOesMarsService>();
				var marsNotified = await mars.RunReminderSweepAsync(DateTime.UtcNow, access.CanUseCostRecoveryAsync, ct);
				return Tuple.Create(true, $"Deployment finance reminder: departments notified={notified}; Cal OES MARS digests={marsNotified}");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Deployment finance reminder worker failed.");
				return Tuple.Create(false, "Deployment finance reminder failed.");
			}
		}
	}
}
