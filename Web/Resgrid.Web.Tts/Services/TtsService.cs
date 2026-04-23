using Amazon.Runtime;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Models;
using System.Diagnostics;

namespace Resgrid.Web.Tts.Services
{
	public sealed class TtsService : ITtsService
	{
		private readonly ICacheService _cacheService;
		private readonly IAudioProcessingService _audioProcessingService;
		private readonly TtsOptions _options;
		private readonly ILogger<TtsService> _logger;
		private readonly SemaphoreSlim _generationSemaphore;

		public TtsService(
			ICacheService cacheService,
			IAudioProcessingService audioProcessingService,
			IOptions<TtsOptions> options,
			ILogger<TtsService> logger)
		{
			_cacheService = cacheService;
			_audioProcessingService = audioProcessingService;
			_options = options.Value;
			_logger = logger;
			_generationSemaphore = new SemaphoreSlim(_options.MaxConcurrentGenerations, _options.MaxConcurrentGenerations);
		}

		public async Task<TtsResponse> GenerateAsync(TtsRequest request, CancellationToken cancellationToken)
		{
			var normalizedRequest = NormalizeRequest(request);
			return await GenerateInternalAsync(normalizedRequest, cancellationToken);
		}

		public async Task<IReadOnlyCollection<TtsResponse>> GenerateBatchAsync(IEnumerable<TtsRequest> requests, CancellationToken cancellationToken)
		{
			ArgumentNullException.ThrowIfNull(requests);

			var normalizedRequests = requests.Select(NormalizeRequest).ToList();

			if (normalizedRequests.Count == 0)
			{
				throw new ArgumentException("At least one TTS request is required.", nameof(requests));
			}

			var responses = await Task.WhenAll(normalizedRequests.Select(request => GenerateInternalAsync(request, cancellationToken)));
			return responses;
		}

		public async Task WarmPromptsAsync(CancellationToken cancellationToken)
		{
			var prompts = _options.PreGeneratedPrompts
				.Where(prompt => !string.IsNullOrWhiteSpace(prompt))
				.Select(prompt => prompt.Trim())
				.Distinct(StringComparer.Ordinal)
				.ToList();

			foreach (var prompt in prompts)
			{
				try
				{
					await GenerateInternalAsync(new NormalizedTtsRequest(prompt, _options.DefaultVoice, _options.DefaultSpeed), cancellationToken);
				}
				catch (ArgumentException ex)
				{
					_logger.LogError(ex, "Configured pre-generated prompt is invalid: {Prompt}", prompt);
				}
				catch (AmazonServiceException ex)
				{
					_logger.LogError(ex, "Failed to warm prompt {Prompt} because S3 returned an error.", prompt);
				}
				catch (HttpRequestException ex)
				{
					_logger.LogError(ex, "Failed to warm prompt {Prompt} because storage connectivity failed.", prompt);
				}
				catch (IOException ex)
				{
					_logger.LogError(ex, "Failed to warm prompt {Prompt} because audio files could not be processed.", prompt);
				}
				catch (InvalidOperationException ex)
				{
					_logger.LogError(ex, "Failed to warm prompt {Prompt} because audio generation failed.", prompt);
				}
			}
		}

		private async Task<TtsResponse> GenerateInternalAsync(NormalizedTtsRequest request, CancellationToken cancellationToken)
		{
			var cacheKey = _cacheService.CreateCacheKey(request.Text, request.Voice, request.Speed);
			var cachedUrl = await _cacheService.TryGetCachedUrlAsync(cacheKey, cancellationToken);

			if (cachedUrl is not null)
			{
				_logger.LogInformation("TTS cache hit for {Hash}", cacheKey.Hash);
				return CreateResponse(cacheKey, request, cachedUrl, cached: true);
			}

			_logger.LogInformation("TTS cache miss for {Hash}", cacheKey.Hash);

			await _generationSemaphore.WaitAsync(cancellationToken);

			try
			{
				cachedUrl = await _cacheService.TryGetCachedUrlAsync(cacheKey, cancellationToken);

				if (cachedUrl is not null)
				{
					_logger.LogInformation("TTS cache filled while waiting for {Hash}", cacheKey.Hash);
					return CreateResponse(cacheKey, request, cachedUrl, cached: true);
				}

				var generationTimer = Stopwatch.StartNew();
				var audioBytes = await _audioProcessingService.GenerateNormalizedWavAsync(request.Text, request.Voice, request.Speed, cancellationToken);
				var objectUrl = await _cacheService.StoreAsync(cacheKey, audioBytes, cancellationToken);
				generationTimer.Stop();

				_logger.LogInformation("Generated audio for {Hash} in {ElapsedMilliseconds} ms", cacheKey.Hash, generationTimer.ElapsedMilliseconds);

				return CreateResponse(cacheKey, request, objectUrl, cached: false);
			}
			finally
			{
				_generationSemaphore.Release();
			}
		}

		private NormalizedTtsRequest NormalizeRequest(TtsRequest? request)
		{
			if (request is null)
			{
				throw new ArgumentException("A TTS request payload is required.", nameof(request));
			}

			var text = request.Text?.Trim();

			if (string.IsNullOrWhiteSpace(text))
			{
				throw new ArgumentException("Text is required.", nameof(request));
			}

			if (text.Length > _options.MaxTextLength)
			{
				throw new ArgumentException($"Text exceeds the configured maximum length of {_options.MaxTextLength} characters.", nameof(request));
			}

			var voice = string.IsNullOrWhiteSpace(request.Voice)
				? _options.DefaultVoice
				: request.Voice.Trim();
			var speed = request.Speed ?? _options.DefaultSpeed;

			if (speed < 80 || speed > 450)
			{
				throw new ArgumentException("Speed must be between 80 and 450.", nameof(request));
			}

			return new NormalizedTtsRequest(text, voice, speed);
		}

		private static TtsResponse CreateResponse(TtsCacheKey cacheKey, NormalizedTtsRequest request, Uri objectUrl, bool cached)
		{
			return new TtsResponse
			{
				Hash = cacheKey.Hash,
				ObjectKey = cacheKey.ObjectKey,
				Url = objectUrl.ToString(),
				Voice = request.Voice,
				Speed = request.Speed,
				Cached = cached
			};
		}

		private sealed record NormalizedTtsRequest(string Text, string Voice, int Speed);
	}
}
