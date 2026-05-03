using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Models;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Tests.Web.Tts
{
	[TestFixture]
	public class TtsServiceTests
	{
		private static readonly TtsCacheKey CacheKey = new("abc123", "tts/abc123.wav");

		private Mock<ICacheService> _cacheService;
		private Mock<IAudioProcessingService> _audioProcessingService;
		private TtsService _service;

		[SetUp]
		public void SetUp()
		{
			_cacheService = new Mock<ICacheService>(MockBehavior.Strict);
			_audioProcessingService = new Mock<IAudioProcessingService>(MockBehavior.Strict);

			_service = new TtsService(
				_cacheService.Object,
				_audioProcessingService.Object,
				Options.Create(new TtsOptions
				{
					DefaultVoice = "en-us+klatt4",
					DefaultSpeed = 165,
					MaxConcurrentGenerations = 2,
					MaxTextLength = 500
				}),
				Mock.Of<ILogger<TtsService>>());
		}

		[Test]
		public async Task generate_async_should_return_cached_response_without_generating_audio()
		{
			var cachedUri = new Uri("https://cdn.example.com/tts/abc123.wav");

			_audioProcessingService
				.Setup(x => x.GetEffectiveSynthesisProfile("en-us+klatt4", 165))
				.Returns(("mb-us1", 130));
			_cacheService
				.Setup(x => x.CreateCacheKey("Press 1 for yes", "mb-us1", 130))
				.Returns(CacheKey);
			_cacheService
				.Setup(x => x.TryGetCachedUrlAsync(CacheKey, It.IsAny<CancellationToken>()))
				.ReturnsAsync(cachedUri);

			var result = await _service.GenerateAsync(new TtsRequest { Text = "Press 1 for yes" }, CancellationToken.None);

			result.Cached.Should().BeTrue();
			result.Hash.Should().Be(CacheKey.Hash);
			result.ObjectKey.Should().Be(CacheKey.ObjectKey);
			result.Url.Should().Be(cachedUri.ToString());
			result.Voice.Should().Be("en-us+klatt4");
			result.Speed.Should().Be(165);

			_audioProcessingService.Verify(
				x => x.GenerateNormalizedWavAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
				Times.Never);
			_cacheService.Verify(
				x => x.StoreAsync(It.IsAny<TtsCacheKey>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task generate_async_should_generate_and_store_audio_when_cache_misses()
		{
			var audioBytes = new byte[] { 1, 2, 3, 4 };
			var objectUri = new Uri("https://cdn.example.com/tts/abc123.wav");

			_audioProcessingService
				.Setup(x => x.GetEffectiveSynthesisProfile("en-us+klatt4", 165))
				.Returns(("mb-us1", 130));
			_cacheService
				.Setup(x => x.CreateCacheKey("Press 1 for yes", "mb-us1", 130))
				.Returns(CacheKey);
			_cacheService
				.SetupSequence(x => x.TryGetCachedUrlAsync(CacheKey, It.IsAny<CancellationToken>()))
				.ReturnsAsync((Uri)null)
				.ReturnsAsync((Uri)null);
			_audioProcessingService
				.Setup(x => x.GenerateNormalizedWavAsync("Press 1 for yes", "en-us+klatt4", 165, It.IsAny<CancellationToken>()))
				.ReturnsAsync(audioBytes);
			_cacheService
				.Setup(x => x.StoreAsync(CacheKey, It.Is<byte[]>(bytes => bytes.SequenceEqual(audioBytes)), It.IsAny<CancellationToken>()))
				.ReturnsAsync(objectUri);

			var result = await _service.GenerateAsync(new TtsRequest { Text = "Press 1 for yes" }, CancellationToken.None);

			result.Cached.Should().BeFalse();
			result.Url.Should().Be(objectUri.ToString());
			result.Voice.Should().Be("en-us+klatt4");
			result.Speed.Should().Be(165);

			_audioProcessingService.Verify(
				x => x.GenerateNormalizedWavAsync("Press 1 for yes", "en-us+klatt4", 165, It.IsAny<CancellationToken>()),
				Times.Once);
			_cacheService.Verify(
				x => x.StoreAsync(CacheKey, It.Is<byte[]>(bytes => bytes.SequenceEqual(audioBytes)), It.IsAny<CancellationToken>()),
				Times.Once);
		}

		[Test]
		public async Task generate_async_should_apply_configured_klatt_variant_to_requested_language()
		{
			var cachedUri = new Uri("https://cdn.example.com/tts/xyz789.wav");
			var cacheKey = new TtsCacheKey("xyz789", "tts/xyz789.wav");

			_audioProcessingService
				.Setup(x => x.GetEffectiveSynthesisProfile("fr+klatt4", 165))
				.Returns(("fr+klatt4", 165));
			_cacheService
				.Setup(x => x.CreateCacheKey("Bonjour", "fr+klatt4", 165))
				.Returns(cacheKey);
			_cacheService
				.Setup(x => x.TryGetCachedUrlAsync(cacheKey, It.IsAny<CancellationToken>()))
				.ReturnsAsync(cachedUri);

			var result = await _service.GenerateAsync(new TtsRequest { Text = "Bonjour", Voice = "fr" }, CancellationToken.None);

			result.Cached.Should().BeTrue();
			result.Voice.Should().Be("fr+klatt4");
			result.Url.Should().Be(cachedUri.ToString());

			_audioProcessingService.Verify(
				x => x.GenerateNormalizedWavAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[TestCase("en-us+f3")]
		[TestCase("en-us+klatt6")]
		public async Task generate_async_should_replace_legacy_default_voices_with_configured_klatt_variant(string requestedVoice)
		{
			var cachedUri = new Uri("https://cdn.example.com/tts/legacy.wav");
			var cacheKey = new TtsCacheKey("legacy", "tts/legacy.wav");

			_audioProcessingService
				.Setup(x => x.GetEffectiveSynthesisProfile("en-us+klatt4", 165))
				.Returns(("mb-us1", 130));
			_cacheService
				.Setup(x => x.CreateCacheKey("Press 1 for yes", "mb-us1", 130))
				.Returns(cacheKey);
			_cacheService
				.Setup(x => x.TryGetCachedUrlAsync(cacheKey, It.IsAny<CancellationToken>()))
				.ReturnsAsync(cachedUri);

			var result = await _service.GenerateAsync(new TtsRequest { Text = "Press 1 for yes", Voice = requestedVoice }, CancellationToken.None);

			result.Cached.Should().BeTrue();
			result.Voice.Should().Be("en-us+klatt4");
			result.Url.Should().Be(cachedUri.ToString());

			_audioProcessingService.Verify(
				x => x.GenerateNormalizedWavAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task generate_async_should_reject_blank_text()
		{
			Func<Task> action = async () => await _service.GenerateAsync(new TtsRequest { Text = "   " }, CancellationToken.None);

			await action.Should().ThrowAsync<ArgumentException>()
				.WithMessage("*Text is required*");
		}

		[Test]
		public async Task generate_async_should_deduplicate_concurrent_generation_for_the_same_cache_key()
		{
			var audioBytes = new byte[] { 1, 2, 3, 4 };
			var objectUri = new Uri("https://cdn.example.com/tts/abc123.wav");
			var generationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var allowGenerationCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var cacheLookupCount = 0;

			_audioProcessingService
				.Setup(x => x.GetEffectiveSynthesisProfile("en-us+klatt4", 165))
				.Returns(("mb-us1", 130));
			_cacheService
				.Setup(x => x.CreateCacheKey("Press 1 for yes", "mb-us1", 130))
				.Returns(CacheKey);
			_cacheService
				.Setup(x => x.TryGetCachedUrlAsync(CacheKey, It.IsAny<CancellationToken>()))
				.Returns(() =>
				{
					var attempt = Interlocked.Increment(ref cacheLookupCount);
					return Task.FromResult(attempt < 4 ? null : objectUri);
				});
			_audioProcessingService
				.Setup(x => x.GenerateNormalizedWavAsync("Press 1 for yes", "en-us+klatt4", 165, It.IsAny<CancellationToken>()))
				.Returns(async () =>
				{
					generationStarted.TrySetResult(true);
					await allowGenerationCompletion.Task;
					return audioBytes;
				});
			_cacheService
				.Setup(x => x.StoreAsync(CacheKey, It.Is<byte[]>(bytes => bytes.SequenceEqual(audioBytes)), It.IsAny<CancellationToken>()))
				.ReturnsAsync(objectUri);

			var firstRequest = _service.GenerateAsync(new TtsRequest { Text = "Press 1 for yes" }, CancellationToken.None);
			await generationStarted.Task;
			var secondRequest = _service.GenerateAsync(new TtsRequest { Text = "Press 1 for yes" }, CancellationToken.None);

			await Task.Yield();
			allowGenerationCompletion.TrySetResult(true);

			var responses = await Task.WhenAll(firstRequest, secondRequest);

			responses.Should().HaveCount(2);
			responses.Count(response => response.Cached).Should().Be(1);
			responses.Count(response => !response.Cached).Should().Be(1);
			_audioProcessingService.Verify(
				x => x.GenerateNormalizedWavAsync("Press 1 for yes", "en-us+klatt4", 165, It.IsAny<CancellationToken>()),
				Times.Once);
			_cacheService.Verify(
				x => x.StoreAsync(CacheKey, It.Is<byte[]>(bytes => bytes.SequenceEqual(audioBytes)), It.IsAny<CancellationToken>()),
				Times.Once);
			_cacheService.Verify(
				x => x.TryGetCachedUrlAsync(CacheKey, It.IsAny<CancellationToken>()),
				Times.Exactly(4));
		}
	}

	[TestFixture]
	public class AudioProcessingServiceTests
	{
		[Test]
		public void create_espeak_start_info_should_use_mbrola_profile_for_english_voices()
		{
			var service = CreateService();

			var startInfo = InvokePrivateMethod<ProcessStartInfo>(service, "CreateEspeakStartInfo", "en-gb-x-rp+klatt4", 165, "/tmp/raw.wav");

			startInfo.FileName.Should().Be("espeak-ng");
			startInfo.ArgumentList.Should().Equal(
				"--stdin",
				"-w",
				"/tmp/raw.wav",
				"-v",
				"mb-us1",
				"-s",
				"130",
				"-p",
				"50",
				"-g",
				"3");
		}

		[Test]
		public void create_espeak_start_info_should_keep_requested_voice_and_speed_for_non_english_voices()
		{
			var service = CreateService();

			var startInfo = InvokePrivateMethod<ProcessStartInfo>(service, "CreateEspeakStartInfo", "fr+klatt4", 165, "/tmp/raw.wav");

			startInfo.FileName.Should().Be("espeak-ng");
			startInfo.ArgumentList.Should().Equal(
				"--stdin",
				"-w",
				"/tmp/raw.wav",
				"-v",
				"fr+klatt4",
				"-s",
				"165");
		}

		[Test]
		public void create_ffmpeg_start_info_should_apply_the_requested_telephone_filter()
		{
			var service = CreateService();

			var startInfo = InvokePrivateMethod<ProcessStartInfo>(service, "CreateFfmpegStartInfo", "/tmp/raw.wav", "/tmp/normalized.wav");

			startInfo.FileName.Should().Be("ffmpeg");
			startInfo.ArgumentList.Should().Equal(
				"-nostdin",
				"-loglevel",
				"error",
				"-y",
				"-i",
				"/tmp/raw.wav",
				"-ar",
				"8000",
				"-ac",
				"1",
				"-acodec",
				"pcm_s16le",
				"-af",
				"highpass=f=200, lowpass=f=3000, anequalizer=c0 f=2500 w=1000 g=3 t=1",
				"/tmp/normalized.wav");
		}

		private static AudioProcessingService CreateService()
		{
			return new AudioProcessingService(
				Options.Create(new TtsOptions()),
				Mock.Of<ILogger<AudioProcessingService>>());
		}

		private static T InvokePrivateMethod<T>(object instance, string methodName, params object[] arguments)
		{
			var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
			method.Should().NotBeNull($"{methodName} should exist on {instance.GetType().FullName}");
			return (T)method!.Invoke(instance, arguments)!;
		}
	}
}
