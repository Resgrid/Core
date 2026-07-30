using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Services;
using System.Diagnostics;

namespace Resgrid.Web.Tts.Health
{
	/// <summary>
	/// Deep dependency probe backing /health/full: proves Redis, S3 and the Piper/ffmpeg
	/// synthesis pipeline actually work, not merely that they are configured (the shallow
	/// <see cref="TtsDependencyHealthCheck"/> wired to the k8s probes covers configuration).
	/// The result is memoized briefly so aggressive monitoring cannot spawn a Piper process
	/// per poll.
	/// </summary>
	public sealed class TtsFullHealthCheck : IHealthCheck
	{
		private static readonly TimeSpan ResultMemoDuration = TimeSpan.FromSeconds(30);
		private static readonly TimeSpan ComponentTimeout = TimeSpan.FromSeconds(10);
		private const string SynthesisProbeText = "Resgrid health check.";

		private readonly IDistributedCache _distributedCache;
		private readonly IStorageService _storageService;
		private readonly IAudioProcessingService _audioProcessingService;
		private readonly TtsOptions _options;
		private readonly SemaphoreSlim _probeLock = new(1, 1);

		private HealthCheckResult? _lastResult;
		private DateTimeOffset _lastResultAt;

		public TtsFullHealthCheck(
			IDistributedCache distributedCache,
			IStorageService storageService,
			IAudioProcessingService audioProcessingService,
			IOptions<TtsOptions> options)
		{
			_distributedCache = distributedCache;
			_storageService = storageService;
			_audioProcessingService = audioProcessingService;
			_options = options.Value;
		}

		public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
		{
			await _probeLock.WaitAsync(cancellationToken);

			try
			{
				if (_lastResult.HasValue && DateTimeOffset.UtcNow - _lastResultAt < ResultMemoDuration)
				{
					return _lastResult.Value;
				}

				var result = await RunProbesAsync(cancellationToken);

				_lastResult = result;
				_lastResultAt = DateTimeOffset.UtcNow;

				return result;
			}
			finally
			{
				_probeLock.Release();
			}
		}

		private async Task<HealthCheckResult> RunProbesAsync(CancellationToken cancellationToken)
		{
			var data = new Dictionary<string, object>();
			var errors = new List<string>();

			await ProbeAsync("redis", data, errors, async token =>
			{
				const string probeKey = "tts-health::probe";
				var expiry = new DistributedCacheEntryOptions
				{
					AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
				};

				await _distributedCache.SetAsync(probeKey, new byte[] { 1 }, expiry, token);

				var payload = await _distributedCache.GetAsync(probeKey, token);

				if (payload is null || payload.Length == 0)
				{
					throw new InvalidOperationException("Redis round-trip returned no payload.");
				}
			}, cancellationToken);

			await ProbeAsync("s3", data, errors, async token =>
			{
				// The existence result is irrelevant — a completed call proves the bucket is
				// reachable with the configured credentials.
				await _storageService.ExistsAsync(GetProbeObjectKey(), token);
			}, cancellationToken);

			await ProbeAsync("synthesis", data, errors, async token =>
			{
				var audio = await _audioProcessingService.GenerateNormalizedWavAsync(
					SynthesisProbeText, _options.DefaultVoice, _options.DefaultSpeed, token);

				if (audio is null || audio.Length == 0)
				{
					throw new InvalidOperationException("Synthesis returned no audio bytes.");
				}
			}, cancellationToken);

			if (errors.Count == 0)
			{
				return HealthCheckResult.Healthy("All TTS dependencies passed functional probes.", data);
			}

			return HealthCheckResult.Unhealthy(string.Join(" ", errors), data: data);
		}

		private string GetProbeObjectKey()
		{
			var prefix = string.IsNullOrWhiteSpace(_options.CachePrefix)
				? string.Empty
				: $"{_options.CachePrefix.Trim().Trim('/')}/";

			return $"{prefix}health-probe.wav";
		}

		private static async Task ProbeAsync(
			string name,
			Dictionary<string, object> data,
			List<string> errors,
			Func<CancellationToken, Task> probe,
			CancellationToken cancellationToken)
		{
			var stopwatch = Stopwatch.StartNew();

			using var timeout = new CancellationTokenSource(ComponentTimeout);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);

			try
			{
				await probe(linked.Token);
				data[name] = $"healthy ({stopwatch.ElapsedMilliseconds}ms)";
			}
			catch (OperationCanceledException) when (timeout.IsCancellationRequested)
			{
				data[name] = $"timeout ({stopwatch.ElapsedMilliseconds}ms)";
				errors.Add($"The {name} probe timed out after {ComponentTimeout.TotalSeconds:0} seconds.");
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				data[name] = $"failed ({stopwatch.ElapsedMilliseconds}ms)";
				errors.Add($"The {name} probe failed: {ex.Message}");
			}
		}
	}
}
