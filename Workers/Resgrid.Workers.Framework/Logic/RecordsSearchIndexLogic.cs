using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker command 44: the records search index maintenance sweep (RMS plan section 5.10). The worker process
	/// is the only holder of the index writer; this logic just drives IRecordsSearchIndexMaintenanceService and
	/// stops indexing while SearchConfig.Enabled is off but still completes durable retention erasures.
	/// </summary>
	public sealed class RecordsSearchIndexLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var maintenance = scope.Resolve<IRecordsSearchIndexMaintenanceService>();
				var result = await maintenance.SweepAsync(cancellationToken);

				if (result.Errors > 0)
					Logging.LogError($"Records search index sweep finished with {result.Errors} error(s): {result.Message}");

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
