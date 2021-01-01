using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
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
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				//await Task.Run(async () =>
				//{
				var _departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				var logic = new CallPruneLogic();

				var items = await _departmentsService.GetAllDepartmentCallPruningsAsync();

				if (items != null)
				{
					_logger.LogInformation("CallPrune::Call Pruning To Process: " + items.Count);

					foreach (var i in items)
					{
						var item = new CallPruneQueueItem();
						item.PruneSettings = i;

						_logger.LogInformation("CallPrune::Pruning Calls for DepartmentId:" + item.PruneSettings.DepartmentId);
						item.PruneSettings.Department =
							await _departmentsService.GetDepartmentByIdAsync(item.PruneSettings.DepartmentId);

						var result = await logic.Process(item);

						if (result.Item1)
						{
							_logger.LogInformation($"CallPrune::Pruned Calls for Department {item.PruneSettings.DepartmentId} successfully.");
						}
						else
						{
							_logger.LogInformation($"CallPrune::Failed to Process Calls for Department {item.PruneSettings.DepartmentId} error {result.Item2}");
						}
					}
				}
				//}, cancellationToken);

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
