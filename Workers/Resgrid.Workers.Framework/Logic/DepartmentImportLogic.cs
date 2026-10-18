using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker command 47 and the departmentimport queue consumer (wizard plan section 10.4). The minute sweep leases any
	/// runnable run the nudge missed, within a budget so one department never starves another; every five minutes it also
	/// runs the watchdog (stalled runs, pending scans). Services come from a fresh lifetime scope per pass so each run
	/// gets its own unit of work. One pass at a time per process; the run lease keeps two processes off the same run.
	/// </summary>
	public sealed class DepartmentImportLogic
	{
		private static readonly SemaphoreSlim SingleFlight = new SemaphoreSlim(1, 1);
		private static DateTime _lastWatchdogUtc = DateTime.MinValue;

		private static string WorkerName => $"worker47:{Environment.MachineName}:{Environment.ProcessId}";

		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			if (!await SingleFlight.WaitAsync(0, cancellationToken))
				return new Tuple<bool, string>(true, "Department import: a pass is already running in this process.");

			try
			{
				var summary = string.Empty;
				if (DateTime.UtcNow - _lastWatchdogUtc >= TimeSpan.FromMinutes(5))
				{
					_lastWatchdogUtc = DateTime.UtcNow;
					using var watchdogScope = Bootstrapper.GetKernel().BeginLifetimeScope();
					summary = await watchdogScope.Resolve<IDepartmentImportMaintenanceService>().RunWatchdogAsync(cancellationToken) + " ";
				}

				int served;
				using (var scope = Bootstrapper.GetKernel().BeginLifetimeScope())
				{
					var runs = scope.Resolve<IImportRunService>();
					served = await runs.ProcessAvailableAsync(WorkerName, TimeSpan.FromSeconds(Math.Max(10, ImportConfig.RunBudgetSeconds)), cancellationToken);
				}

				// Invitations an administrator chose to send go out in provider-safe batches (wizard plan section 15.4).
				int invited;
				using (var invitationScope = Bootstrapper.GetKernel().BeginLifetimeScope())
					invited = await invitationScope.Resolve<IImportActivationService>().SendDueInvitationsAsync(cancellationToken);

				return new Tuple<bool, string>(true, $"{summary}Department import: served {served} run(s), sent {invited} invitation(s).");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
			finally
			{
				SingleFlight.Release();
			}
		}

		/// <summary>The RabbitMQ nudge: process this run now if it is runnable. Failures are left to the minute sweep.</summary>
		public static async Task ProcessQueueItem(DepartmentImportQueueItem item)
		{
			if (item == null || string.IsNullOrWhiteSpace(item.RunId))
				return;

			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var runs = scope.Resolve<IImportRunService>();
				await runs.ProcessRunAsync(item.RunId, WorkerName, TimeSpan.FromSeconds(Math.Max(10, ImportConfig.RunBudgetSeconds)), CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Department import nudge for run {item.RunId} failed; the minute sweep will claim it");
			}
		}
	}
}
