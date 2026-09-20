using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 49 (Workforce &amp; Business Operations plan E5), daily. During the California filing season
	/// (WorkforceConfig.FilingSeasonStartMonth–FilingSeasonEndMonth) every department with an active employer profile
	/// or a report run for the prior year gets one value-free readiness digest per day to its administrators: due
	/// date, run state, counts of unresolved exceptions, missing demographic responses and missing annual pay
	/// facts. Never a name, code, earnings figure or rate; never files anything. All year it purges the bytes of
	/// export artifacts past WorkforceConfig.ExportArtifactRetentionDays (the run, snapshots and rows stay).
	/// </summary>
	public sealed class PayDataReportingReadinessLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken ct)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var reporting = scope.Resolve<ICaPayDataReportingService>();
				var access = scope.Resolve<IBusinessOperationsAccessService>();
				var purged = await reporting.PurgeExpiredArtifactsAsync(DateTime.UtcNow, ct);
				var notified = await reporting.RunReadinessSweepAsync(DateTime.UtcNow, access.CanUsePayDataReportingAsync, ct);
				return Tuple.Create(true, $"Pay data reporting readiness: departments notified={notified}; artifacts purged={purged}");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Pay data reporting readiness worker failed.");
				return Tuple.Create(false, "Pay data reporting readiness failed.");
			}
		}
	}
}
