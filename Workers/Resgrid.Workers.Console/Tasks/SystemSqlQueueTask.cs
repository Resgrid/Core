using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using Resgrid.Workers.Framework.Workers.TrainingNotifier;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class SystemSqlQueueTask : IQuidjiboHandler<SystemSqlQueueCommand>
	{
		public string Name => "System SQL Queue";
		public int Priority => 1;
		public ILogger _logger;

		public SystemSqlQueueTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(SystemSqlQueueCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var queueService = Bootstrapper.GetKernel().Resolve<IQueueService>();
				var deleteService = Bootstrapper.GetKernel().Resolve<IDeleteService>();

				var pendingDepartmentDeleteRequests = await queueService.GetAllPendingDeleteDepartmentQueueItemsAsync();

				if (pendingDepartmentDeleteRequests != null && pendingDepartmentDeleteRequests.Any())
				{
					_logger.LogInformation("SystemSqlQueue::Pending Department Deletions: " + pendingDepartmentDeleteRequests.Count());

					foreach (var request in pendingDepartmentDeleteRequests)
					{
						await deleteService.HandlePendingDepartmentDeletionRequestAsync(request, cancellationToken);
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
