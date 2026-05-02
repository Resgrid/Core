using System;
using System.Collections.Generic;
using System.Linq;
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
	public class TtsAdminControllerTests
	{
		private Mock<ITtsService> _ttsService;
		private Mock<ITtsPlaybackUrlService> _ttsPlaybackUrlService;
		private TtsOptions _options;

		[SetUp]
		public void SetUp()
		{
			_ttsService = new Mock<ITtsService>(MockBehavior.Strict);
			_ttsPlaybackUrlService = new Mock<ITtsPlaybackUrlService>(MockBehavior.Strict);
			_ttsPlaybackUrlService
				.Setup(x => x.CreatePlaybackUrl(It.IsAny<HttpRequest>(), It.IsAny<string>()))
				.Returns<HttpRequest, string>((_, hash) => new Uri($"https://tts.example.com/tts/audio/{hash}.wav"));
			_options = new TtsOptions
			{
				DefaultVoice = "en-us+klatt6",
				DefaultSpeed = 175,
				StaticPromptAdminKey = "secret-key",
				PreGeneratedPrompts = new List<string> { "Alpha", "Beta" }
			};
		}

		[Test]
		public async Task regenerate_static_prompts_should_reject_invalid_admin_key()
		{
			var controller = BuildController();

			var result = await controller.RegenerateStaticPromptsAsync("bad-key", new StaticPromptRegenerationRequest(), CancellationToken.None);

			result.Result.Should().BeOfType<UnauthorizedObjectResult>();
			_ttsService.Verify(x => x.GenerateBatchAsync(It.IsAny<IEnumerable<TtsRequest>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task regenerate_static_prompts_should_use_supplied_prompts_when_authorized()
		{
			var responses = new[]
			{
				new TtsResponse { Hash = "a", ObjectKey = "tts/a.wav", Url = "https://cdn.example.com/tts/a.wav", Voice = "en-us", Speed = 175 }
			};
			List<TtsRequest> capturedPrompts = null;
			_ttsService
				.Setup(x => x.GenerateBatchAsync(It.IsAny<IEnumerable<TtsRequest>>(), It.IsAny<CancellationToken>()))
				.Callback<IEnumerable<TtsRequest>, CancellationToken>((requests, _) => capturedPrompts = requests.ToList())
				.ReturnsAsync(responses);

			var controller = BuildController();
			var request = new StaticPromptRegenerationRequest
			{
				Prompts = new List<TtsRequest>
				{
					new TtsRequest { Text = "Prompt 1", Voice = "en-gb", Speed = 180 }
				}
			};

			var result = await controller.RegenerateStaticPromptsAsync("secret-key", request, CancellationToken.None);

			var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
			var response = okResult.Value.Should().BeOfType<StaticPromptRegenerationResponse>().Subject;
			response.PromptCount.Should().Be(1);
			response.Prompts.Should().BeEquivalentTo(responses, options => options.Excluding(x => x.Url));
			response.Prompts.Single().Url.Should().Be("https://tts.example.com/tts/audio/a.wav");
			capturedPrompts.Should().ContainSingle();
			capturedPrompts![0].Text.Should().Be("Prompt 1");
			capturedPrompts[0].Voice.Should().Be("en-gb");
			capturedPrompts[0].Speed.Should().Be(180);
		}

		[Test]
		public async Task regenerate_static_prompts_should_fall_back_to_configured_prompts_when_request_is_empty()
		{
			List<TtsRequest> capturedPrompts = null;
			_ttsService
				.Setup(x => x.GenerateBatchAsync(It.IsAny<IEnumerable<TtsRequest>>(), It.IsAny<CancellationToken>()))
				.Callback<IEnumerable<TtsRequest>, CancellationToken>((requests, _) => capturedPrompts = requests.ToList())
				.ReturnsAsync(new[]
				{
					new TtsResponse { Hash = "a", ObjectKey = "tts/a.wav", Url = "https://cdn.example.com/tts/a.wav", Voice = "en-us+klatt6", Speed = 175 },
					new TtsResponse { Hash = "b", ObjectKey = "tts/b.wav", Url = "https://cdn.example.com/tts/b.wav", Voice = "en-us+klatt6", Speed = 175 }
				});

			var controller = BuildController();

			var result = await controller.RegenerateStaticPromptsAsync("secret-key", new StaticPromptRegenerationRequest(), CancellationToken.None);

			result.Result.Should().BeOfType<OkObjectResult>();
			capturedPrompts.Should().HaveCount(2);
			capturedPrompts!.Select(x => x.Text).Should().Equal("Alpha", "Beta");
			capturedPrompts.Select(x => x.Voice).Should().OnlyContain(x => x == "en-us+klatt6");
			capturedPrompts.Select(x => x.Speed).Should().OnlyContain(x => x == 175);
		}

		private TtsAdminController BuildController()
		{
			return new TtsAdminController(_ttsService.Object, _ttsPlaybackUrlService.Object, Options.Create(_options))
			{
				ControllerContext = new ControllerContext
				{
					HttpContext = new DefaultHttpContext()
				}
			};
		}
	}
}
