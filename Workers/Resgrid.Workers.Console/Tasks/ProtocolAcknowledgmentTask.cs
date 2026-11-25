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
	/// <summary>Worker 86 (ProtocolAcknowledgmentLogic): file text, quiz-gated completion, reminders and escalation.</summary>
	public class ProtocolAcknowledgmentTask : IQuidjiboHandler<ProtocolAcknowledgmentCommand>
	{
		public string Name => "Protocol Acknowledgments";
		public int Priority => 1;
		public ILogger _logger;

		public ProtocolAcknowledgmentTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(ProtocolAcknowledgmentCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new ProtocolAcknowledgmentLogic().Process(cancellationToken);

				if (result.AttachmentsQueued > 0 || result.TextsExtracted > 0 || result.QuizzesCompleted > 0 || result.RemindersSent > 0 || result.Failures > 0)
					_logger.LogInformation($"ProtocolAcknowledgments::Queued {result.AttachmentsQueued}, extracted {result.TextsExtracted}, quizzes {result.QuizzesCompleted}, reminders {result.RemindersSent}, failures {result.Failures}");

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
