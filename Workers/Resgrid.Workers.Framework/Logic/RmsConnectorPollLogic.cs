using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker command 46 (RMS plan section 4.1, external ordering-system connectors): polls every enabled
	/// connector whose own interval has elapsed. Each connector is one bounded run under its own hourly request
	/// limit; a failing connector counts a failure and is switched off after the configured run of them, so one
	/// dead source cannot wedge the sweep or hammer the other party. Off entirely unless RecordsConnectorConfig.Enabled.
	/// </summary>
	public sealed class RmsConnectorPollLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var service = scope.Resolve<IRecordDeploymentConnectorsService>();
				var ran = await service.RunDueAsync(cancellationToken);
				return new Tuple<bool, string>(true, $"Connector poll: {ran} connector(s) run.");
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
