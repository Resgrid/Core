using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Web.Tts.Configuration;

namespace Resgrid.Tests.Web.Tts
{
	[TestFixture]
	[NonParallelizable]
	public class TtsConfigurationRegistrationTests
	{
		private string _originalS3AccessKey;
		private string _originalS3SecretKey;
		private string _originalS3Bucket;
		private string _originalS3Endpoint;
		private string _originalDefaultVoice;
		private int _originalDefaultSpeed;
		private string _originalPreGeneratedPrompts;
		private string _originalPlaybackBaseUrl;
		private int _originalPlaybackMemoryCacheMinutes;
		private int _originalPlaybackCacheControlSeconds;
		private int _originalRateLimitPermitLimit;
		private string _originalTempDirectory;
		private string _originalStaticPromptAdminKey;

		[SetUp]
		public void SetUp()
		{
			_originalS3AccessKey = TtsConfig.S3AccessKey;
			_originalS3SecretKey = TtsConfig.S3SecretKey;
			_originalS3Bucket = TtsConfig.S3Bucket;
			_originalS3Endpoint = TtsConfig.S3Endpoint;
			_originalDefaultVoice = TtsConfig.DefaultVoice;
			_originalDefaultSpeed = TtsConfig.DefaultSpeed;
			_originalPreGeneratedPrompts = TtsConfig.PreGeneratedPrompts;
			_originalPlaybackBaseUrl = TtsConfig.PlaybackBaseUrl;
			_originalPlaybackMemoryCacheMinutes = TtsConfig.PlaybackMemoryCacheMinutes;
			_originalPlaybackCacheControlSeconds = TtsConfig.PlaybackCacheControlSeconds;
			_originalRateLimitPermitLimit = TtsConfig.RateLimitPermitLimit;
			_originalTempDirectory = TtsConfig.TempDirectory;
			_originalStaticPromptAdminKey = TtsConfig.StaticPromptAdminKey;
		}

		[TearDown]
		public void TearDown()
		{
			TtsConfig.S3AccessKey = _originalS3AccessKey;
			TtsConfig.S3SecretKey = _originalS3SecretKey;
			TtsConfig.S3Bucket = _originalS3Bucket;
			TtsConfig.S3Endpoint = _originalS3Endpoint;
			TtsConfig.DefaultVoice = _originalDefaultVoice;
			TtsConfig.DefaultSpeed = _originalDefaultSpeed;
			TtsConfig.PreGeneratedPrompts = _originalPreGeneratedPrompts;
			TtsConfig.PlaybackBaseUrl = _originalPlaybackBaseUrl;
			TtsConfig.PlaybackMemoryCacheMinutes = _originalPlaybackMemoryCacheMinutes;
			TtsConfig.PlaybackCacheControlSeconds = _originalPlaybackCacheControlSeconds;
			TtsConfig.RateLimitPermitLimit = _originalRateLimitPermitLimit;
			TtsConfig.TempDirectory = _originalTempDirectory;
			TtsConfig.StaticPromptAdminKey = _originalStaticPromptAdminKey;
		}

		[Test]
		public void add_tts_configuration_should_map_resgrid_config_values_into_options()
		{
			TtsConfig.S3AccessKey = "access-key";
			TtsConfig.S3SecretKey = "secret-key";
			TtsConfig.S3Bucket = "tts-bucket";
			TtsConfig.S3Endpoint = "https://minio.example.com";
			TtsConfig.DefaultVoice = "en-gb";
			TtsConfig.DefaultSpeed = 190;
			TtsConfig.PreGeneratedPrompts = "Alpha;Beta";
			TtsConfig.PlaybackBaseUrl = "https://tts.example.com";
			TtsConfig.PlaybackMemoryCacheMinutes = 90;
			TtsConfig.PlaybackCacheControlSeconds = 7200;
			TtsConfig.RateLimitPermitLimit = 15;
			TtsConfig.TempDirectory = "/tmp/custom-tts";
			TtsConfig.StaticPromptAdminKey = "prompt-admin-key";

			var services = new ServiceCollection();
			services.AddTtsConfiguration();

			using var provider = services.BuildServiceProvider();

			var s3Options = provider.GetRequiredService<IOptions<S3StorageOptions>>().Value;
			var ttsOptions = provider.GetRequiredService<IOptions<TtsOptions>>().Value;
			var rateLimitOptions = provider.GetRequiredService<IOptions<RateLimitOptions>>().Value;

			s3Options.AccessKey.Should().Be("access-key");
			s3Options.SecretKey.Should().Be("secret-key");
			s3Options.Bucket.Should().Be("tts-bucket");
			s3Options.Endpoint.Should().Be("https://minio.example.com");
			ttsOptions.DefaultVoice.Should().Be("en-gb");
			ttsOptions.DefaultSpeed.Should().Be(190);
			ttsOptions.PlaybackBaseUrl.Should().Be("https://tts.example.com");
			ttsOptions.PlaybackMemoryCacheMinutes.Should().Be(90);
			ttsOptions.PlaybackCacheControlSeconds.Should().Be(7200);
			ttsOptions.TempDirectory.Should().Be("/tmp/custom-tts");
			ttsOptions.StaticPromptAdminKey.Should().Be("prompt-admin-key");
			ttsOptions.PreGeneratedPrompts.Should().Equal("Alpha", "Beta");
			rateLimitOptions.PermitLimit.Should().Be(15);
		}

		[Test]
		public void add_tts_configuration_should_require_static_prompt_admin_key()
		{
			TtsConfig.StaticPromptAdminKey = " ";

			var services = new ServiceCollection();
			services.AddTtsConfiguration();

			using var provider = services.BuildServiceProvider();

			FluentActions
				.Invoking(() => provider.GetRequiredService<IOptions<TtsOptions>>().Value)
				.Should()
				.Throw<OptionsValidationException>()
				.WithMessage("*A static prompt admin key is required.*");
		}
	}
}
