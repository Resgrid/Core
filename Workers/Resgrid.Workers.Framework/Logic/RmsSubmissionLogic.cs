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
	/// Worker command 41: the reporting-destination submission sweep (RMS plan sections 5.3/5.5). Drives
	/// IRecordsSubmissionService, which talks to NERIS outside any database transaction; a no-op while
	/// NerisConfig.Enabled is off.
	/// </summary>
	public sealed class RmsSubmissionLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				if (!NerisConfig.Enabled)
					return new Tuple<bool, string>(true, "NERIS submission disabled; nothing to do.");

				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var service = scope.Resolve<IRecordsSubmissionService>();
				var result = await service.SweepAsync(cancellationToken);

				if (result.Errors > 0)
					Logging.LogError($"Records submission sweep finished with {result.Errors} error(s): {result.Message}");

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
