using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Tests.Web.Tts
{
	[TestFixture]
	public class CacheServiceTests
	{
		private Mock<IStorageService> _storageService;
		private Mock<IDistributedCache> _distributedCache;
		private Dictionary<string, byte[]> _cacheEntries;
		private CacheService _service;

		[SetUp]
		public void SetUp()
		{
			_storageService = new Mock<IStorageService>();
			_distributedCache = new Mock<IDistributedCache>(MockBehavior.Strict);
			_cacheEntries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			_distributedCache
				.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string key, CancellationToken _) => _cacheEntries.TryGetValue(key, out var value) ? value : null);
			_distributedCache
				.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
				.Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((key, value, _, _) => _cacheEntries[key] = value.ToArray())
				.Returns(Task.CompletedTask);
			_distributedCache
				.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback<string, CancellationToken>((key, _) => _cacheEntries.Remove(key))
				.Returns(Task.CompletedTask);
			_service = new CacheService(
				_storageService.Object,
				_distributedCache.Object,
				Options.Create(new TtsOptions
				{
					CachePrefix = "tts",
					PlaybackMemoryCacheMinutes = 60
				}));
		}

		[Test]
		public void create_cache_key_should_be_deterministic_for_same_inputs()
		{
			var first = _service.CreateCacheKey("Press 1 for yes", "en-us", 175);
			var second = _service.CreateCacheKey("Press 1 for yes", "en-us", 175);

			first.Should().Be(second);
			first.ObjectKey.Should().Be($"tts/{first.Hash}.wav");
		}

		[Test]
		public void create_cache_key_should_change_when_inputs_change()
		{
			var baseline = _service.CreateCacheKey("Press 1 for yes", "en-us", 175);
			var differentText = _service.CreateCacheKey("Press 2 for no", "en-us", 175);
			var differentVoice = _service.CreateCacheKey("Press 1 for yes", "en-gb", 175);
			var differentSpeed = _service.CreateCacheKey("Press 1 for yes", "en-us", 200);

			differentText.Hash.Should().NotBe(baseline.Hash);
			differentVoice.Hash.Should().NotBe(baseline.Hash);
			differentSpeed.Hash.Should().NotBe(baseline.Hash);
		}

		[Test]
		public async Task store_async_should_seed_redis_cache_for_immediate_playback()
		{
			var cacheKey = _service.CreateCacheKey("Press 1 for yes", "en-us", 175);
			var audioBytes = new byte[] { 1, 2, 3, 4 };

			_storageService
				.Setup(x => x.UploadAsync(cacheKey.ObjectKey, It.IsAny<Stream>(), "audio/wav", It.IsAny<CancellationToken>()))
				.Returns(Task.CompletedTask);
			_storageService
				.Setup(x => x.GetObjectUrlAsync(cacheKey.ObjectKey, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new Uri("https://tts.example.com/tts/audio/abc.wav"));

			await _service.StoreAsync(cacheKey, audioBytes, CancellationToken.None);

			var audio = await _service.TryGetAudioAsync(cacheKey.Hash, CancellationToken.None);

			audio.Should().NotBeNull();
			audio!.AudioBytes.Should().Equal(audioBytes);
			audio.ContentType.Should().Be("audio/wav");
			_storageService.Verify(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task try_get_audio_async_should_cache_storage_result_in_redis()
		{
			var cacheKey = _service.CreateCacheKey("Press 1 for yes", "en-us", 175);
			var storageAudio = new TtsAudioContent(
				new byte[] { 9, 8, 7 },
				"audio/wav",
				"\"etag\"",
				DateTimeOffset.UtcNow);

			_storageService
				.Setup(x => x.GetObjectAsync(cacheKey.ObjectKey, It.IsAny<CancellationToken>()))
				.ReturnsAsync(storageAudio);

			var first = await _service.TryGetAudioAsync(cacheKey.Hash, CancellationToken.None);
			var second = await _service.TryGetAudioAsync(cacheKey.Hash, CancellationToken.None);

			first.Should().BeEquivalentTo(storageAudio);
			second.Should().BeEquivalentTo(storageAudio);
			_storageService.Verify(x => x.GetObjectAsync(cacheKey.ObjectKey, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task try_get_cached_url_async_should_skip_s3_exists_when_audio_is_already_in_redis()
		{
			var cacheKey = _service.CreateCacheKey("Press 1 for yes", "en-us", 175);
			var cachedUrl = new Uri("https://tts.example.com/tts/audio/cached.wav");

			_storageService
				.Setup(x => x.UploadAsync(cacheKey.ObjectKey, It.IsAny<Stream>(), "audio/wav", It.IsAny<CancellationToken>()))
				.Returns(Task.CompletedTask);
			_storageService
				.Setup(x => x.GetObjectUrlAsync(cacheKey.ObjectKey, It.IsAny<CancellationToken>()))
				.ReturnsAsync(cachedUrl);

			await _service.StoreAsync(cacheKey, new byte[] { 5, 4, 3, 2 }, CancellationToken.None);

			var result = await _service.TryGetCachedUrlAsync(cacheKey, CancellationToken.None);

			result.Should().Be(cachedUrl);
			_storageService.Verify(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
