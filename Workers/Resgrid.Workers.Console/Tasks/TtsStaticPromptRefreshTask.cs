using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class TtsStaticPromptRefreshTask : IQuidjiboHandler<TtsStaticPromptRefreshCommand>
	{
		public const int MaxRetries = 3;
		public static TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

		public string Name => "TTS Static Prompt Refresh";
		public int Priority => 1;
		private readonly ILogger _logger;

		public TtsStaticPromptRefreshTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(TtsStaticPromptRefreshCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				if (string.IsNullOrWhiteSpace(TtsConfig.ServiceBaseUrl) || string.IsNullOrWhiteSpace(TtsConfig.StaticPromptAdminKey))
				{
					_logger.LogInformation("TtsStaticPromptRefresh::Skipping because the TTS service URL or admin key is not configured");
					progress.Report(100, $"Skipping the {Name} Task");
					return;
				}

				var ttsAudioService = Bootstrapper.GetKernel().Resolve<ITtsAudioService>();
				var prompts = TwilioVoicePromptCatalog.GetStaticPrompts();
				Exception lastException = null;

				for (int attempt = 1; attempt <= MaxRetries; attempt++)
				{
					cancellationToken.ThrowIfCancellationRequested();

					try
					{
						_logger.LogInformation("TtsStaticPromptRefresh::Refreshing static prompts (attempt {Attempt}/{MaxRetries})", attempt, MaxRetries);
						await ttsAudioService.RegenerateStaticPromptsAsync(prompts, cancellationToken);

						_logger.LogInformation("TtsStaticPromptRefresh::Successfully refreshed static prompts on attempt {Attempt}", attempt);
						progress.Report(100, $"Finishing the {Name} Task");
						return;
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception ex)
					{
						lastException = ex;

						if (attempt < MaxRetries)
						{
							_logger.LogWarning(ex, "TtsStaticPromptRefresh::Attempt {Attempt}/{MaxRetries} failed, retrying in {DelaySeconds}s", attempt, MaxRetries, (int)RetryDelay.TotalSeconds);
							await Task.Delay(RetryDelay, cancellationToken);
						}
					}
				}

				Resgrid.Framework.Logging.LogException(lastException);
				_logger.LogError(lastException, "TtsStaticPromptRefresh::Failed to refresh static prompts after {MaxRetries} attempts", MaxRetries);
				throw lastException!;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex, "TtsStaticPromptRefresh::Failed to refresh static prompts");
				throw;
			}
		}
	}
}
