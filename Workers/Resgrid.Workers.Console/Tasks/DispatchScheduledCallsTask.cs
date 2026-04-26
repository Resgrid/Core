using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class DispatchScheduledCallsTask : IQuidjiboHandler<DispatchScheduledCallsCommand>
	{
		public string Name => "Dispatch Scheduled Calls";
		public int Priority => 1;
		public ILogger _logger;

		public DispatchScheduledCallsTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(DispatchScheduledCallsCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
				var callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();
				var queueService = Bootstrapper.GetKernel().Resolve<IQueueService>();
				var callDispatchStatusService = Bootstrapper.GetKernel().Resolve<ICallDispatchStatusService>();

				var pendingCalls = await callsService.GetAllNonDispatchedScheduledCallsWithinDateRange(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));

				if (pendingCalls != null && pendingCalls.Any())
				{
					foreach (var call in pendingCalls)
					{
						var cqi = new CallQueueItem();
						cqi.Call = await callsService.PopulateCallData(call, true, false, false, true, true, true, true, false, false);

						if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
							cqi.Profiles = await userProfileService.GetSelectedUserProfilesAsync(cqi.Call.Dispatches.Select(x => x.UserId).ToList());

						var result = await queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);

						if (result)
						{
							call.HasBeenDispatched = true;
							await callsService.SaveCallAsync(call);
							await callDispatchStatusService.ApplyDispatchStatusesAsync(cqi.Call, cancellationToken: cancellationToken);
						}
					}
				}

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
