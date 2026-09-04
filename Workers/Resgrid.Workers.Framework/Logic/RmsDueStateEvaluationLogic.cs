using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker command 42 (RMS plan RMS-3): the bounded RecordOverdue evaluation. Drives
	/// IRecordsDueStateService, which decides what to emit from the persisted due-state rows rather than from
	/// when this worker last ran — so a skipped run emits late instead of never, and a repeated run stays quiet.
	/// </summary>
	public sealed class RmsDueStateEvaluationLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				var service = Bootstrapper.GetKernel().Resolve<IRecordsDueStateService>();
				var result = await service.SweepAsync(cancellationToken);

				if (result.Errors > 0)
					Logging.LogError($"Records due-state sweep finished with {result.Errors} error(s): {result.Message}");

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
