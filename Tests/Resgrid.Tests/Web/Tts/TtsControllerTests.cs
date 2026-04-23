using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Controllers;
using Resgrid.Web.Tts.Models;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Tests.Web.Tts
{
	[TestFixture]
	public class TtsControllerTests
	{
		private Mock<ITtsService> _ttsService;
		private Mock<ICacheService> _cacheService;
		private Mock<ITtsPlaybackUrlService> _ttsPlaybackUrlService;
		private TtsController _controller;

		[SetUp]
		public void SetUp()
		{
			_ttsService = new Mock<ITtsService>(MockBehavior.Strict);
			_cacheService = new Mock<ICacheService>(MockBehavior.Strict);
			_ttsPlaybackUrlService = new Mock<ITtsPlaybackUrlService>(MockBehavior.Strict);

			_controller = new TtsController(
				_ttsService.Object,
				_cacheService.Object,
				_ttsPlaybackUrlService.Object,
				Options.Create(new TtsOptions
				{
					PlaybackCacheControlSeconds = 7200
				}))
			{
				ControllerContext = new ControllerContext
				{
					HttpContext = new DefaultHttpContext()
				}
			};
		}

		[Test]
		public async Task generate_async_should_return_playback_api_url()
		{
			var serviceResponse = new TtsResponse
			{
				Hash = new string('a', 64),
				ObjectKey = "tts/audio.wav",
				Url = "https://s3.example.com/tts/audio.wav",
				Voice = "en-us",
				Speed = 175
			};

			_ttsService
				.Setup(x => x.GenerateAsync(It.IsAny<TtsRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(serviceResponse);
			_ttsPlaybackUrlService
				.Setup(x => x.CreatePlaybackUrl(It.IsAny<HttpRequest>(), serviceResponse.Hash))
				.Returns(new Uri($"https://tts.example.com/tts/audio/{serviceResponse.Hash}.wav"));

			var result = await _controller.GenerateAsync(new TtsRequest { Text = "Hello" }, CancellationToken.None);

			var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
			var response = okResult.Value.Should().BeOfType<TtsResponse>().Subject;
			response.Url.Should().Be($"https://tts.example.com/tts/audio/{serviceResponse.Hash}.wav");
		}

		[Test]
		public async Task get_audio_async_should_return_cached_audio_with_cache_headers()
		{
			var hash = new string('b', 64);
			var audio = new TtsAudioContent(
				new byte[] { 1, 2, 3 },
				"audio/wav",
				"\"etag-123\"",
				new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero));

			_cacheService
				.Setup(x => x.TryGetAudioAsync(hash, It.IsAny<CancellationToken>()))
				.ReturnsAsync(audio);

			var result = await _controller.GetAudioAsync(hash, CancellationToken.None);

			var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
			fileResult.FileContents.Should().Equal(audio.AudioBytes);
			fileResult.ContentType.Should().Be("audio/wav");
			fileResult.EnableRangeProcessing.Should().BeTrue();
			fileResult.EntityTag.Should().NotBeNull();
			fileResult.EntityTag!.ToString().Should().Be("\"etag-123\"");
			fileResult.LastModified.Should().Be(audio.LastModified);
			_controller.Response.Headers["Cache-Control"].ToString().Should().Be("public,max-age=7200,immutable");
		}

		[Test]
		public async Task get_audio_async_should_return_not_found_when_audio_missing()
		{
			var hash = new string('c', 64);

			_cacheService
				.Setup(x => x.TryGetAudioAsync(hash, It.IsAny<CancellationToken>()))
				.ReturnsAsync((TtsAudioContent)null);

			var result = await _controller.GetAudioAsync(hash, CancellationToken.None);

			result.Should().BeOfType<NotFoundResult>();
		}
	}
}
