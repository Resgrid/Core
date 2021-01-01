using System.Collections.Generic;
using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using System;

namespace Resgrid.Workers.Console.Tasks
{
	public class CallEmailImport : IQuidjiboHandler<CallEmailImportCommand>
	{
		public string Name => "Call Email Import";
		public int Priority => 1;
		public ILogger _logger;

		public CallEmailImport(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(CallEmailImportCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress.Report(1, $"Starting the {Name} Task");

			try
			{
				//await Task.Run(async () =>
				//{
				var _departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				var logic = new CallEmailImporterLogic();

				//var items = await _departmentsService.GetAllDepartmentEmailSettingsAsync();
				var items = new List<DepartmentCallEmail>();

				if (items != null)
				{
					_logger.LogInformation("CallEmailImport::Email Import Settings: " + items.Count);

					foreach (var i in items)
					{
						var cqi = new CallEmailQueueItem();
						cqi.EmailSettings = i;

						_logger.LogInformation("CallEmailImport::Processing Email for DepartmentCallEmailId:" + cqi.EmailSettings.DepartmentCallEmailId);

						var result = await logic.Process(cqi);

						if (result.Item1)
						{
							_logger.LogInformation($"CallEmailImport::Processed Email Import {cqi.EmailSettings.DepartmentCallEmailId} successfully.");
						}
						else
						{
							_logger.LogInformation($"CallEmailImport::Failed to Processed Email Import {cqi.EmailSettings.DepartmentCallEmailId} error {result.Item2}");
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
