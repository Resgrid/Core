using Autofac;
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
					progress.Report(1, "CallPrune::Call Pruning To Process: " + items.Count);

					foreach (var i in items)
					{
						var item = new CallPruneQueueItem();
						item.PruneSettings = i;

						progress.Report(1, "CallPrune::Processing Calls for DepartmentId:" + item.PruneSettings.DepartmentId);

						var result = logic.Process(item);

						if (result.Item1)
						{
							progress.Report(1, $"CallPrune::Processing Calls for Department {item.PruneSettings.DepartmentId} successfully.");
						}
						else
						{
							progress.Report(1, $"CallPrune::Failed to Process Calls for Department {item.PruneSettings.DepartmentId} error {result.Item2}");
						}
					}
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);


			progress.Report(1, $"Finishing the {Name} Task");
		}
	}
}
