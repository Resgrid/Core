using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Twilio;
using Twilio.TwiML;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class TwilioVoiceResponseServiceTests
	{
		[Test]
		public async Task append_prompt_async_should_reuse_generated_tts_url_within_request_scope()
		{
			var promptUrl = new Uri("https://tts.example.com/tts/audio/abc.wav");
			var ttsAudioService = new Mock<ITtsAudioService>(MockBehavior.Strict);
			ttsAudioService
				.Setup(x => x.GenerateSpeechUrlAsync("Hello from Resgrid", null, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(promptUrl);

			var service = new TwilioVoiceResponseService(ttsAudioService.Object);
			var response = new VoiceResponse();

			await service.AppendPromptAsync(response, "Hello from Resgrid", CancellationToken.None);
			await service.AppendPromptAsync(response, "Hello from Resgrid", CancellationToken.None);

			var xml = response.ToString();

			xml.Should().Contain(promptUrl.ToString());
			xml.Split("<Play>").Length.Should().Be(3);
			ttsAudioService.Verify(
				x => x.GenerateSpeechUrlAsync("Hello from Resgrid", null, null, It.IsAny<CancellationToken>()),
				Times.Once);
		}

		[Test]
		public async Task append_prompt_async_should_scope_cache_entries_by_voice()
		{
			var spanishPromptUrl = new Uri("https://tts.example.com/tts/audio/es.wav");
			var frenchPromptUrl = new Uri("https://tts.example.com/tts/audio/fr.wav");
			var ttsAudioService = new Mock<ITtsAudioService>(MockBehavior.Strict);
			ttsAudioService
				.Setup(x => x.GenerateSpeechUrlAsync("Hello from Resgrid", "es", null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(spanishPromptUrl);
			ttsAudioService
				.Setup(x => x.GenerateSpeechUrlAsync("Hello from Resgrid", "fr", null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(frenchPromptUrl);

			var service = new TwilioVoiceResponseService(ttsAudioService.Object);
			var response = new VoiceResponse();

			await service.AppendPromptAsync(response, "Hello from Resgrid", CancellationToken.None, "es");
			await service.AppendPromptAsync(response, "Hello from Resgrid", CancellationToken.None, "es");
			await service.AppendPromptAsync(response, "Hello from Resgrid", CancellationToken.None, "fr");

			var xml = response.ToString();

			xml.Should().Contain(spanishPromptUrl.ToString());
			xml.Should().Contain(frenchPromptUrl.ToString());
			xml.Split("<Play>").Length.Should().Be(4);
			ttsAudioService.Verify(
				x => x.GenerateSpeechUrlAsync("Hello from Resgrid", "es", null, It.IsAny<CancellationToken>()),
				Times.Once);
			ttsAudioService.Verify(
				x => x.GenerateSpeechUrlAsync("Hello from Resgrid", "fr", null, It.IsAny<CancellationToken>()),
				Times.Once);
		}
	}
}
