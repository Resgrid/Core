using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Console.Tasks
{
	/// <summary>Worker 96 (LmsSkillsLogic): skill notice digests and task book validity expiry.</summary>
	public class LmsSkillsTask : IQuidjiboHandler<LmsSkillsCommand>
	{
		public string Name => "LMS Skills";
		public int Priority => 1;
		public ILogger _logger;

		public LmsSkillsTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(LmsSkillsCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new LmsSkillsLogic().Process(cancellationToken);
				if (result.NoticesSent > 0 || result.TaskBooksExpired > 0 || result.Failures > 0)
					_logger.LogInformation($"LmsSkills::Notices {result.NoticesSent}, task books expired {result.TaskBooksExpired}, failures {result.Failures}");

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
