using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker command 43 (RMS plan RMS-3): retention, legal hold and attachment purge, plus the rescan pass for
	/// attachments the scanner could not reach at upload time. Drives IRecordsRetentionService; every pass is
	/// bounded per department so an overdue first run cannot monopolise the worker.
	/// </summary>
	public sealed class RmsRetentionAndPurgeLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var service = scope.Resolve<IRecordsRetentionService>();
				var result = await service.SweepAsync(cancellationToken);

				if (result.Errors > 0)
					Logging.LogError($"Records retention sweep finished with {result.Errors} error(s): {result.Message}");

				return new Tuple<bool, string>(true, result.Message);
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
