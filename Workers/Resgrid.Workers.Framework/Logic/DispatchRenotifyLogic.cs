using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 78: every minute, re-notifies dispatched personnel and units that have not acknowledged a call, for departments
	/// that turned dispatch re-notification on (Dispatch Settings). A cheap single indexed query when nothing is due.
	/// </summary>
	public class DispatchRenotifyLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken = default)
		{
			try
			{
				if (!Config.DispatchConfig.RenotifyEnabled)
					return new Tuple<bool, string>(true, "Dispatch re-notification is turned off (DispatchConfig.RenotifyEnabled).");

				// Own scope per sweep: root-scope services would share the process-wide root unit of work.
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var renotificationService = scope.Resolve<IDispatchRenotificationService>();

				var result = await renotificationService.ProcessDueAsync(DateTime.UtcNow, cancellationToken);

				return new Tuple<bool, string>(true, result.ToString());
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
		}
	}
}
