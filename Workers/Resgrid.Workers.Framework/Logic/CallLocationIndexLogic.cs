using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker command 73: keeps the call location index (M0259) in step with each department. Purges departments whose
	/// Advanced Data Protection policy left Disabled, resumes ones that came back, then backfills calls newest first within
	/// a time budget; a department picks up from its cursor on the next run. New and edited calls are indexed on save.
	/// </summary>
	public sealed class CallLocationIndexLogic
	{
		private static readonly TimeSpan Budget = TimeSpan.FromSeconds(45);

		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var history = scope.Resolve<ICallLocationHistoryService>();
				var result = await history.RunIndexSweepAsync(Budget, cancellationToken);

				if (result.Errors > 0)
					Logging.LogError(result.Message);

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
