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
	/// Cal OES MARS duties join with that milestone.
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
				return Tuple.Create(true, $"Deployment finance reminder: departments notified={notified}");
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
