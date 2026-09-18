using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker command 70: the global search index maintenance sweep (Unified Search plan R4 Phase 2). The worker process
	/// is the only holder of the index writer; this logic just drives ISearchIndexMaintenanceService, which no-ops
	/// while SearchConfig.Enabled is off.
	/// </summary>
	public sealed class SearchIndexLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var maintenance = scope.Resolve<ISearchIndexMaintenanceService>();
				var result = await maintenance.SweepAsync(cancellationToken);

				if (result.Errors > 0)
					Logging.LogError($"Global search index sweep finished with {result.Errors} error(s): {result.Message}");

				return new Tuple<bool, string>(result.Errors == 0, result.Message);
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
		}
	}
}
