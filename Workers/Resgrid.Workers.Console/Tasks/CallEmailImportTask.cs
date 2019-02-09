using Autofac;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class CallEmailImport : IQuidjiboHandler<CallEmailImportCommand>
	{
		public string Name => "Call Email Import";
		public int Priority => 1;

		public async Task ProcessAsync(CallEmailImportCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress.Report(1, $"Starting the {Name} Task");

			await Task.Factory.StartNew(() =>
			{
				var _departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				var logic = new CallEmailImporterLogic();

				var items = _departmentsService.GetAllDepartmentEmailSettings();

				if (items != null)
				{
					progress.Report(1, "CallEmailImport::Email Import Settings: " + items.Count);

					foreach (var i in items)
					{
						var cqi = new CallEmailQueueItem();
						cqi.EmailSettings = i;

						progress.Report(1, "CallEmailImport::Processing Email for DepartmentCallEmailId:" + cqi.EmailSettings.DepartmentCallEmailId);

						var result = logic.Process(cqi);

						if (result.Item1)
						{
							progress.Report(1, $"CallEmailImport::Processed Email Import {cqi.EmailSettings.DepartmentCallEmailId} successfully.");
						}
						else
						{
							progress.Report(1, $"CallEmailImport::Failed to Processed Email Import {cqi.EmailSettings.DepartmentCallEmailId} error {result.Item2}");
						}
					}
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

			progress.Report(1, $"Finishing the {Name} Task");
		}
	}
}
