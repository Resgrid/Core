using System;
using System.Linq;
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
					DefaultVoice = "en-us",
					DefaultSpeed = 175,
					MaxConcurrentGenerations = 2,
					MaxTextLength = 500
				}),
				Mock.Of<ILogger<TtsService>>());
		}

		[Test]
		public async Task generate_async_should_return_cached_response_without_generating_audio()
		{
			var cachedUri = new Uri("https://cdn.example.com/tts/abc123.wav");

			_cacheService
				.Setup(x => x.CreateCacheKey("Press 1 for yes", "en-us", 175))
				.Returns(CacheKey);
			_cacheService
				.Setup(x => x.TryGetCachedUrlAsync(CacheKey, It.IsAny<CancellationToken>()))
				.ReturnsAsync(cachedUri);

			var result = await _service.GenerateAsync(new TtsRequest { Text = "Press 1 for yes" }, CancellationToken.None);

			result.Cached.Should().BeTrue();
			result.Hash.Should().Be(CacheKey.Hash);
			result.ObjectKey.Should().Be(CacheKey.ObjectKey);
			result.Url.Should().Be(cachedUri.ToString());
			result.Voice.Should().Be("en-us");
			result.Speed.Should().Be(175);

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

			_cacheService
				.Setup(x => x.CreateCacheKey("Press 1 for yes", "en-us", 175))
				.Returns(CacheKey);
			_cacheService
				.SetupSequence(x => x.TryGetCachedUrlAsync(CacheKey, It.IsAny<CancellationToken>()))
				.ReturnsAsync((Uri)null)
				.ReturnsAsync((Uri)null);
			_audioProcessingService
				.Setup(x => x.GenerateNormalizedWavAsync("Press 1 for yes", "en-us", 175, It.IsAny<CancellationToken>()))
				.ReturnsAsync(audioBytes);
			_cacheService
				.Setup(x => x.StoreAsync(CacheKey, It.Is<byte[]>(bytes => bytes.SequenceEqual(audioBytes)), It.IsAny<CancellationToken>()))
				.ReturnsAsync(objectUri);

			var result = await _service.GenerateAsync(new TtsRequest { Text = "Press 1 for yes" }, CancellationToken.None);

			result.Cached.Should().BeFalse();
			result.Url.Should().Be(objectUri.ToString());
			result.Voice.Should().Be("en-us");
			result.Speed.Should().Be(175);

			_audioProcessingService.Verify(
				x => x.GenerateNormalizedWavAsync("Press 1 for yes", "en-us", 175, It.IsAny<CancellationToken>()),
				Times.Once);
			_cacheService.Verify(
				x => x.StoreAsync(CacheKey, It.Is<byte[]>(bytes => bytes.SequenceEqual(audioBytes)), It.IsAny<CancellationToken>()),
				Times.Once);
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

			_cacheService
				.Setup(x => x.CreateCacheKey("Press 1 for yes", "en-us", 175))
				.Returns(CacheKey);
			_cacheService
				.Setup(x => x.TryGetCachedUrlAsync(CacheKey, It.IsAny<CancellationToken>()))
				.Returns(() =>
				{
					var attempt = Interlocked.Increment(ref cacheLookupCount);
					return Task.FromResult<Uri?>(attempt < 4 ? null : objectUri);
				});
			_audioProcessingService
				.Setup(x => x.GenerateNormalizedWavAsync("Press 1 for yes", "en-us", 175, It.IsAny<CancellationToken>()))
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
				x => x.GenerateNormalizedWavAsync("Press 1 for yes", "en-us", 175, It.IsAny<CancellationToken>()),
				Times.Once);
			_cacheService.Verify(
				x => x.StoreAsync(CacheKey, It.Is<byte[]>(bytes => bytes.SequenceEqual(audioBytes)), It.IsAny<CancellationToken>()),
				Times.Once);
			_cacheService.Verify(
				x => x.TryGetCachedUrlAsync(CacheKey, It.IsAny<CancellationToken>()),
				Times.Exactly(4));
		}
	}
}
