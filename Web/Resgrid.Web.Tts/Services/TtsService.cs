using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Models;
using System.Collections.Concurrent;
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
		private readonly ConcurrentDictionary<string, GenerationLock> _generationLocks = new(StringComparer.Ordinal);

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
			// 1. Warm static prompts with the default voice and speed.
			var prompts = _options.PreGeneratedPrompts
				.Where(prompt => !string.IsNullOrWhiteSpace(prompt))
				.Select(prompt => prompt.Trim())
				.Distinct(StringComparer.Ordinal)
				.ToList();

			var defaultVoiceWarmed = false;

			foreach (var prompt in prompts)
			{
				try
				{
					await GenerateInternalAsync(new NormalizedTtsRequest(prompt, _options.DefaultVoice, _options.DefaultSpeed), cancellationToken);
					defaultVoiceWarmed = true;
				}
				catch (ArgumentException ex)
				{
					_logger.LogError(ex, "Configured pre-generated prompt is invalid: {Prompt}", prompt);
				}
				catch (HttpRequestException ex)
				{
					_logger.LogError(ex, "Failed to warm prompt {Prompt} because storage returned an error.", prompt);
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

			// 2. Warm one minimal prompt per distinct voice model so that the first
			//    non-default-language request does not incur the model-loading
			//    penalty. The English model is already loaded by step 1, but a
			//    cache hit for English means the model might be skipped — the
			//    explicit per-model warm here guarantees each model file is loaded.
			//    Using the default speed ensures the cache key is consistent.
			const string modelWarmPrompt = "Test";
			var distinctVoices = _audioProcessingService.GetDistinctVoiceIdentifiers();
			var defaultProfile = _audioProcessingService.GetEffectiveSynthesisProfile(_options.DefaultVoice, _options.DefaultSpeed);

			foreach (var voice in distinctVoices)
			{
				// Skip the default voice — its model was already covered by the
				// static prompts above provided at least one prompt is configured.
				var profile = _audioProcessingService.GetEffectiveSynthesisProfile(voice, _options.DefaultSpeed);
				if (defaultVoiceWarmed && string.Equals(profile.Voice, defaultProfile.Voice, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				try
				{
					await GenerateInternalAsync(new NormalizedTtsRequest(modelWarmPrompt, voice, _options.DefaultSpeed), cancellationToken);
				}
				catch (ArgumentException ex)
				{
					_logger.LogError(ex, "Model warm-up prompt is invalid for voice {Voice}: {Prompt}", voice, modelWarmPrompt);
				}
				catch (HttpRequestException ex)
				{
					_logger.LogError(ex, "Failed to warm model for voice {Voice} because storage returned an error.", voice);
				}
				catch (IOException ex)
				{
					_logger.LogError(ex, "Failed to warm model for voice {Voice} because audio files could not be processed.", voice);
				}
				catch (InvalidOperationException ex)
				{
					_logger.LogError(ex, "Failed to warm model for voice {Voice} because audio generation failed.", voice);
				}
			}
		}

		private async Task<TtsResponse> GenerateInternalAsync(NormalizedTtsRequest request, CancellationToken cancellationToken)
		{
			var effectiveProfile = _audioProcessingService.GetEffectiveSynthesisProfile(request.Voice, request.Speed);
			var cacheKey = _cacheService.CreateCacheKey(request.Text, effectiveProfile.Voice, effectiveProfile.Speed);
			var cachedUrl = await _cacheService.TryGetCachedUrlAsync(cacheKey, cancellationToken);

			if (cachedUrl is not null)
			{
				_logger.LogInformation("TTS cache hit for {Hash}", cacheKey.Hash);
				return CreateResponse(cacheKey, request, cachedUrl, cached: true);
			}

			_logger.LogInformation("TTS cache miss for {Hash}", cacheKey.Hash);

			using var generationLock = await AcquireGenerationLockAsync(cacheKey.Hash, cancellationToken);
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

		private async Task<GenerationLockLease> AcquireGenerationLockAsync(string hash, CancellationToken cancellationToken)
		{
			while (true)
			{
				if (_generationLocks.TryGetValue(hash, out var existingLock))
				{
					Interlocked.Increment(ref existingLock.ReferenceCount);

					if (_generationLocks.TryGetValue(hash, out var currentLock) && ReferenceEquals(existingLock, currentLock))
					{
						try
						{
							await existingLock.Semaphore.WaitAsync(cancellationToken);
							return new GenerationLockLease(this, hash, existingLock);
						}
						catch
						{
							ReleaseGenerationLockReference(hash, existingLock);
							throw;
						}
					}

					ReleaseGenerationLockReference(hash, existingLock);
					continue;
				}

				var newLock = new GenerationLock();

				if (_generationLocks.TryAdd(hash, newLock))
				{
					try
					{
						await newLock.Semaphore.WaitAsync(cancellationToken);
						return new GenerationLockLease(this, hash, newLock);
					}
					catch
					{
						ReleaseGenerationLockReference(hash, newLock);
						throw;
					}
				}
			}
		}

		private void ReleaseGenerationLock(string hash, GenerationLock generationLock)
		{
			generationLock.Semaphore.Release();
			ReleaseGenerationLockReference(hash, generationLock);
		}

		private void ReleaseGenerationLockReference(string hash, GenerationLock generationLock)
		{
			if (Interlocked.Decrement(ref generationLock.ReferenceCount) == 0
				&& _generationLocks.TryRemove(new KeyValuePair<string, GenerationLock>(hash, generationLock)))
			{
				generationLock.Semaphore.Dispose();
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

			var voice = NormalizeVoice(request.Voice);
			var speed = request.Speed ?? _options.DefaultSpeed;

			if (speed < 80 || speed > 450)
			{
				throw new ArgumentException("Speed must be between 80 and 450.", nameof(request));
			}

			return new NormalizedTtsRequest(text, voice, speed);
		}

		private string NormalizeVoice(string? voice)
		{
			var configuredDefaultVoice = string.IsNullOrWhiteSpace(_options.DefaultVoice)
				? "en-us+klatt4"
				: _options.DefaultVoice.Trim();
			var requestedVoice = string.IsNullOrWhiteSpace(voice)
				? configuredDefaultVoice
				: voice.Trim();

			if (string.Equals(requestedVoice, "en-us+f3", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(requestedVoice, "en-us+klatt6", StringComparison.OrdinalIgnoreCase))
			{
				return configuredDefaultVoice;
			}

			if (HasExplicitVariant(requestedVoice))
			{
				return requestedVoice;
			}

			var configuredVariantSuffix = GetVariantSuffix(configuredDefaultVoice);
			return string.IsNullOrWhiteSpace(configuredVariantSuffix)
				? requestedVoice
				: $"{requestedVoice}{configuredVariantSuffix}";
		}

		private static bool HasExplicitVariant(string voice)
		{
			return voice.IndexOf('+') > 0;
		}

		private static string? GetVariantSuffix(string voice)
		{
			var variantSeparatorIndex = voice.IndexOf('+');
			return variantSeparatorIndex <= 0 ? null : voice[variantSeparatorIndex..];
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

		private sealed class GenerationLock
		{
			public GenerationLock()
			{
				ReferenceCount = 1;
			}

			public SemaphoreSlim Semaphore { get; } = new(1, 1);

			public int ReferenceCount;
		}

		private sealed class GenerationLockLease : IDisposable
		{
			private readonly TtsService _service;
			private readonly string _hash;
			private readonly GenerationLock _generationLock;
			private bool _disposed;

			public GenerationLockLease(TtsService service, string hash, GenerationLock generationLock)
			{
				_service = service;
				_hash = hash;
				_generationLock = generationLock;
			}

			public void Dispose()
			{
				if (_disposed)
				{
					return;
				}

				_service.ReleaseGenerationLock(_hash, _generationLock);
				_disposed = true;
			}
		}

		private sealed record NormalizedTtsRequest(string Text, string Voice, int Speed);
	}
}
