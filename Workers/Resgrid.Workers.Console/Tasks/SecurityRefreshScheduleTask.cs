using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class SecurityRefreshScheduleTask : IQuidjiboHandler<SecurityRefreshScheduleCommand>
	{
		public string Name => "Security Refresh";
		public int Priority => 1;
		public ILogger _logger;

		public SecurityRefreshScheduleTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(SecurityRefreshScheduleCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				SecurityLogic logic = new SecurityLogic();
				await logic.UpdatedCachedSecurityForAllDepartments();

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
