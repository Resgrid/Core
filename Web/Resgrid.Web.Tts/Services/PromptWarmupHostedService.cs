using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;

namespace Resgrid.Web.Tts.Services
{
	public sealed class PromptWarmupHostedService : BackgroundService
	{
		private readonly ITtsService _ttsService;
		private readonly TtsOptions _options;
		private readonly ILogger<PromptWarmupHostedService> _logger;

		public PromptWarmupHostedService(
			ITtsService ttsService,
			IOptions<TtsOptions> options,
			ILogger<PromptWarmupHostedService> logger)
		{
			_ttsService = ttsService;
			_options = options.Value;
			_logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			if (!_options.WarmupEnabled)
			{
				_logger.LogInformation("TTS prompt warmup is disabled.");
				return;
			}

			if (_options.PreGeneratedPrompts.Count == 0)
			{
				_logger.LogInformation("No pre-generated TTS prompts were configured.");
				return;
			}

			_logger.LogInformation("Warming {PromptCount} pre-generated TTS prompts.", _options.PreGeneratedPrompts.Count);

			try
			{
				await _ttsService.WarmPromptsAsync(stoppingToken);
				_logger.LogInformation("Finished warming pre-generated TTS prompts.");
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				_logger.LogInformation("TTS prompt warmup was cancelled.");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "TTS prompt warmup failed but will not stop host.");
			}
		}
	}
}
