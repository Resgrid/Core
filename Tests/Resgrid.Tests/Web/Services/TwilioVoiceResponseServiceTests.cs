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

		[Test]
		public async Task append_prompt_async_should_generate_cached_prompt_without_request_cancellation_token()
		{
			var promptUrl = new Uri("https://tts.example.com/tts/audio/abc.wav");
			var requestCancellation = new CancellationTokenSource();
			var capturedToken = CancellationToken.None;
			var ttsAudioService = new Mock<ITtsAudioService>(MockBehavior.Strict);
			ttsAudioService
				.Setup(x => x.GenerateSpeechUrlAsync("Hello from Resgrid", null, null, It.IsAny<CancellationToken>()))
				.Callback<string, string, int?, CancellationToken>((_, _, _, token) => capturedToken = token)
				.ReturnsAsync(promptUrl);

			var service = new TwilioVoiceResponseService(ttsAudioService.Object);
			var response = new VoiceResponse();

			await service.AppendPromptAsync(response, "Hello from Resgrid", requestCancellation.Token);

			capturedToken.CanBeCanceled.Should().BeFalse();
			ttsAudioService.Verify(
				x => x.GenerateSpeechUrlAsync("Hello from Resgrid", null, null, It.IsAny<CancellationToken>()),
				Times.Once);
		}

		[Test]
		public async Task append_prompt_async_should_not_evict_cached_generation_when_a_waiting_request_is_cancelled()
		{
			var promptUrl = new Uri("https://tts.example.com/tts/audio/abc.wav");
			var generationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var promptGeneration = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
			var ttsAudioService = new Mock<ITtsAudioService>(MockBehavior.Strict);
			ttsAudioService
				.Setup(x => x.GenerateSpeechUrlAsync("Hello from Resgrid", null, null, It.IsAny<CancellationToken>()))
				.Returns<string, string, int?, CancellationToken>((_, _, _, _) =>
				{
					generationStarted.TrySetResult(true);
					return promptGeneration.Task;
				});

			var service = new TwilioVoiceResponseService(ttsAudioService.Object);
			using var requestCancellation = new CancellationTokenSource();
			var cancelledResponse = new VoiceResponse();
			var cancelledAppendTask = service.AppendPromptAsync(cancelledResponse, "Hello from Resgrid", requestCancellation.Token);

			await generationStarted.Task;
			requestCancellation.Cancel();

			await FluentActions
				.Awaiting(() => cancelledAppendTask)
				.Should()
				.ThrowAsync<OperationCanceledException>();

			var response = new VoiceResponse();
			var secondAppendTask = service.AppendPromptAsync(response, "Hello from Resgrid", CancellationToken.None);

			promptGeneration.SetResult(promptUrl);
			await secondAppendTask;

			response.ToString().Should().Contain(promptUrl.ToString());
			ttsAudioService.Verify(
				x => x.GenerateSpeechUrlAsync("Hello from Resgrid", null, null, It.IsAny<CancellationToken>()),
				Times.Once);
		}

		[Test]
		public async Task pre_warm_prompt_async_should_not_throw_for_multi_chunk_text()
		{
			// Regression (Sentry RESGRID-API-78): long dispatch text spans multiple TTS chunks.
			// PreWarmPromptAsync previously threw ArgumentException for multi-chunk input, faulting
			// the voice-dispatch pre-warm/redirect path. It must now warm every chunk without throwing.
			var originalMaxLength = Resgrid.Config.TtsConfig.MaxTextLength;
			Resgrid.Config.TtsConfig.MaxTextLength = 30;
			try
			{
				var ttsAudioService = new Mock<ITtsAudioService>();
				ttsAudioService
					.Setup(x => x.GenerateSpeechUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(new Uri("https://tts.example.com/tts/audio/chunk.wav"));

				var service = new TwilioVoiceResponseService(ttsAudioService.Object);
				var text = "Engine one respond to the structure fire. Ladder two stage at the corner. Battalion three assume command. All units use caution.";

				Func<Task> act = () => service.PreWarmPromptAsync(text);

				await act.Should().NotThrowAsync();
			}
			finally
			{
				Resgrid.Config.TtsConfig.MaxTextLength = originalMaxLength;
			}
		}

		[Test]
		public async Task append_prompt_async_should_emit_a_play_per_chunk_for_multi_chunk_text()
		{
			// Regression (Sentry RESGRID-API-78): the dispatch playback path now routes long text
			// through AppendPromptAsync, which must fan multi-chunk text out to one <Play> per chunk
			// rather than throwing like the single-chunk GetPromptUrlAsync did.
			var originalMaxLength = Resgrid.Config.TtsConfig.MaxTextLength;
			Resgrid.Config.TtsConfig.MaxTextLength = 30;
			try
			{
				var ttsAudioService = new Mock<ITtsAudioService>();
				ttsAudioService
					.Setup(x => x.GenerateSpeechUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(new Uri("https://tts.example.com/tts/audio/chunk.wav"));

				var service = new TwilioVoiceResponseService(ttsAudioService.Object);
				var response = new VoiceResponse();
				var text = "Engine one respond to the structure fire. Ladder two stage at the corner. Battalion three assume command. All units use caution.";

				await service.AppendPromptAsync(response, text, CancellationToken.None);

				var playCount = response.ToString().Split("<Play>").Length - 1;
				playCount.Should().BeGreaterThan(1);
			}
			finally
			{
				Resgrid.Config.TtsConfig.MaxTextLength = originalMaxLength;
			}
		}
	}
}
