using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Repositories;
using Resgrid.Workers.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class CleanOIDCScheduleTask : IQuidjiboHandler<Commands.CleanOIDCCommand>
	{
		public string Name => "Clean OIDC Tokens";
		public int Priority => 1;
		public ILogger _logger;

		public CleanOIDCScheduleTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(Commands.CleanOIDCCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var identityRepository = Bootstrapper.GetKernel().Resolve<IIdentityRepository>();
				var result = await identityRepository.CleanUpOIDCTokensAsync(DateTime.UtcNow);

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
