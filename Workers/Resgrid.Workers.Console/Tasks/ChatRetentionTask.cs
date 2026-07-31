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
	public sealed class ChatRetentionTask : IQuidjiboHandler<ChatRetentionCommand>
	{
		private readonly ILogger _logger;

		public ChatRetentionTask(ILogger logger)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public string Name => "Chat Retention";
		public int Priority => 1;

		public async Task ProcessAsync(ChatRetentionCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress?.Report(1, $"Starting the {Name} Task");

			try
			{
				var logic = new ChatRetentionLogic();
				var result = await logic.Process(cancellationToken);

				if (!result.Item1)
					throw new InvalidOperationException(result.Item2);

				_logger.LogInformation("ChatRetention::{Summary}", result.Item2);
				progress?.Report(100, $"Finishing the {Name} Task");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
		}
	}
}
