using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Resgrid.Config;
using Resgrid.Web.Tts.Configuration;

namespace Resgrid.Web.Tts.Health
{
	public sealed class TtsDependencyHealthCheck : IHealthCheck
	{
		private readonly S3StorageOptions _s3Options;
		private readonly TtsOptions _ttsOptions;

		public TtsDependencyHealthCheck(
			IOptions<S3StorageOptions> s3Options,
			IOptions<TtsOptions> ttsOptions)
		{
			_s3Options = s3Options.Value;
			_ttsOptions = ttsOptions.Value;
		}

		public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
		{
			var validationErrors = new List<string>();

			if (string.IsNullOrWhiteSpace(_s3Options.AccessKey))
			{
				validationErrors.Add("S3 access key is not configured.");
			}

			if (string.IsNullOrWhiteSpace(_s3Options.SecretKey))
			{
				validationErrors.Add("S3 secret key is not configured.");
			}

			if (string.IsNullOrWhiteSpace(_s3Options.Bucket))
			{
				validationErrors.Add("S3 bucket is not configured.");
			}

			if (string.IsNullOrWhiteSpace(_ttsOptions.EspeakExecutable))
			{
				validationErrors.Add("eSpeak NG executable is not configured.");
			}

			if (string.IsNullOrWhiteSpace(_ttsOptions.FfmpegExecutable))
			{
				validationErrors.Add("ffmpeg executable is not configured.");
			}

			if (string.IsNullOrWhiteSpace(CacheConfig.RedisConnectionString))
			{
				validationErrors.Add("Redis connection string is not configured.");
			}

			if (validationErrors.Count == 0)
			{
				return Task.FromResult(HealthCheckResult.Healthy("TTS configuration is ready."));
			}

			return Task.FromResult(HealthCheckResult.Unhealthy(string.Join(" ", validationErrors)));
		}
	}
}
