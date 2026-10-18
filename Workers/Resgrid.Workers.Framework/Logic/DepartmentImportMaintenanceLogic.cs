using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker command 48 (hourly; wizard plan section 13.2): expires abandoned uploads, workspaces, grants and source
	/// credentials, purges artifacts and staging past retention, sends reminders, works the helpdesk outbox and removes
	/// the stored objects of deleted departments. Single-flight per process; per-department work is idempotent.
	/// </summary>
	public sealed class DepartmentImportMaintenanceLogic
	{
		private static readonly SemaphoreSlim SingleFlight = new SemaphoreSlim(1, 1);

		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			if (!await SingleFlight.WaitAsync(0, cancellationToken))
				return new Tuple<bool, string>(true, "Department import maintenance: already running in this process.");

			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var summary = await scope.Resolve<IDepartmentImportMaintenanceService>().RunHourlyAsync(cancellationToken);
				return new Tuple<bool, string>(true, summary);
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
	}
}
