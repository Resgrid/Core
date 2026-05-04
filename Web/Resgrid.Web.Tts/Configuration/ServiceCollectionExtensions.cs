using Microsoft.Extensions.DependencyInjection;
using Resgrid.Config;

namespace Resgrid.Web.Tts.Configuration
{
	public static class ServiceCollectionExtensions
	{
		private const string StaticPromptAdminKeyRequiredMessage = "A static prompt admin key is required.";

		public static IServiceCollection AddTtsConfiguration(this IServiceCollection services)
		{
			services.AddOptions<S3StorageOptions>()
				.Configure(ApplyS3Options)
				.ValidateDataAnnotations()
				.Validate(options => !string.IsNullOrWhiteSpace(options.AccessKey), "S3 access key is required.")
				.Validate(options => !string.IsNullOrWhiteSpace(options.SecretKey), "S3 secret key is required.")
				.Validate(options => !string.IsNullOrWhiteSpace(options.Bucket), "S3 bucket is required.")
				.ValidateOnStart();

			services.AddOptions<TtsOptions>()
				.Configure(ApplyTtsOptions)
				.ValidateDataAnnotations()
				.Validate(options => !string.IsNullOrWhiteSpace(options.DefaultVoice), "A default voice is required.")
				.Validate(options => !string.IsNullOrWhiteSpace(options.StaticPromptAdminKey), StaticPromptAdminKeyRequiredMessage)
				.Validate(options => options.PreGeneratedPrompts is not null, "Pre-generated prompts must be initialized.")
				.ValidateOnStart();

			services.AddOptions<RateLimitOptions>()
				.Configure(ApplyRateLimitOptions)
				.ValidateDataAnnotations()
				.ValidateOnStart();

			return services;
		}

		private static void ApplyS3Options(S3StorageOptions options)
		{
			options.Endpoint = string.IsNullOrWhiteSpace(TtsConfig.S3Endpoint) ? null : TtsConfig.S3Endpoint;
			options.AccessKey = TtsConfig.S3AccessKey;
			options.SecretKey = TtsConfig.S3SecretKey;
			options.Bucket = TtsConfig.S3Bucket;
			options.Region = string.IsNullOrWhiteSpace(TtsConfig.S3Region) ? options.Region : TtsConfig.S3Region;
			options.PublicBaseUrl = string.IsNullOrWhiteSpace(TtsConfig.S3PublicBaseUrl) ? null : TtsConfig.S3PublicBaseUrl;
			options.ForcePathStyle = TtsConfig.S3ForcePathStyle;
			options.UsePresignedUrls = TtsConfig.S3UsePresignedUrls;
			options.UseSsl = TtsConfig.S3UseSsl;
			options.PresignedUrlExpiryMinutes = TtsConfig.S3PresignedUrlExpiryMinutes;
		}

		private static void ApplyTtsOptions(TtsOptions options)
		{
			options.DefaultVoice = string.IsNullOrWhiteSpace(TtsConfig.DefaultVoice) ? options.DefaultVoice : TtsConfig.DefaultVoice;
			options.DefaultSpeed = TtsConfig.DefaultSpeed;
			options.MaxConcurrentGenerations = TtsConfig.MaxConcurrentGenerations;
			options.MaxTextLength = TtsConfig.MaxTextLength;
			options.PiperExecutable = string.IsNullOrWhiteSpace(TtsConfig.PiperExecutable) ? options.PiperExecutable : TtsConfig.PiperExecutable;
			options.PiperModelDirectory = string.IsNullOrWhiteSpace(TtsConfig.PiperModelDirectory) ? options.PiperModelDirectory : TtsConfig.PiperModelDirectory;
			options.FfmpegExecutable = string.IsNullOrWhiteSpace(TtsConfig.FfmpegExecutable) ? options.FfmpegExecutable : TtsConfig.FfmpegExecutable;
			options.TempDirectory = string.IsNullOrWhiteSpace(TtsConfig.TempDirectory) ? options.TempDirectory : TtsConfig.TempDirectory;
			options.CachePrefix = string.IsNullOrWhiteSpace(TtsConfig.CachePrefix) ? options.CachePrefix : TtsConfig.CachePrefix;
			options.NormalizedSampleRate = TtsConfig.NormalizedSampleRate;
			options.NormalizedChannels = TtsConfig.NormalizedChannels;
			options.PlaybackBaseUrl = !string.IsNullOrWhiteSpace(TtsConfig.PlaybackBaseUrl)
				? TtsConfig.PlaybackBaseUrl
				: string.IsNullOrWhiteSpace(TtsConfig.ServiceBaseUrl)
					? options.PlaybackBaseUrl
					: TtsConfig.ServiceBaseUrl;
			options.PlaybackMemoryCacheMinutes = TtsConfig.PlaybackMemoryCacheMinutes;
			options.PlaybackCacheControlSeconds = TtsConfig.PlaybackCacheControlSeconds;
			options.WarmupEnabled = TtsConfig.WarmupEnabled;
			options.StaticPromptAdminKey = TtsConfig.StaticPromptAdminKey;
			options.PreGeneratedPrompts = ParsePrompts(TtsConfig.PreGeneratedPrompts);
		}

		private static void ApplyRateLimitOptions(RateLimitOptions options)
		{
			options.PermitLimit = TtsConfig.RateLimitPermitLimit;
			options.QueueLimit = TtsConfig.RateLimitQueueLimit;
			options.WindowSeconds = TtsConfig.RateLimitWindowSeconds;
		}

		private static List<string> ParsePrompts(string rawPrompts)
		{
			if (string.IsNullOrWhiteSpace(rawPrompts))
			{
				return new List<string>();
			}

			return rawPrompts
				.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.ToList();
		}
	}
}