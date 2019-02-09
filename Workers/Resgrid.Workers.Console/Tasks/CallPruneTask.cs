using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using Serilog.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class CallPruneTask : IQuidjiboHandler<CallPruneCommand>
	{
		public string Name => "Call Prune";
		public int Priority => 1;
		public ILogger _logger;

		public CallPruneTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(CallPruneCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress.Report(1, $"Starting the {Name} Task");

			await Task.Factory.StartNew(() =>
			{
				var _departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				var logic = new CallPruneLogic();

				var items = _departmentsService.GetAllDepartmentCallPrunings();

				if (items != null)
				{
					_logger.LogInformation("CallPrune::Call Pruning To Process: " + items.Count);

					foreach (var i in items)
					{
						var item = new CallPruneQueueItem();
						item.PruneSettings = i;

						_logger.LogInformation("CallPrune::Processing Calls for DepartmentId:" + item.PruneSettings.DepartmentId);

						var result = logic.Process(item);

						if (result.Item1)
						{
							_logger.LogInformation($"CallPrune::Processing Calls for Department {item.PruneSettings.DepartmentId} successfully.");
						}
						else
						{
							_logger.LogInformation($"CallPrune::Failed to Process Calls for Department {item.PruneSettings.DepartmentId} error {result.Item2}");
						}
					}
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);


			progress.Report(100, $"Finishing the {Name} Task");
		}
	}
}
