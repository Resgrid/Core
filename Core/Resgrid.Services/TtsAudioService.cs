using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model.Services;
using RestSharp;

namespace Resgrid.Services
{
	public class TtsAudioService : ITtsAudioService
	{
		private const string AdminKeyHeaderName = "X-Resgrid-Admin-Key";
		private readonly Func<RestClient> _restClientFactory;

		public TtsAudioService(Func<RestClient> restClientFactory)
		{
			// The RestClient is resolved lazily (only when TTS audio is actually generated) so that
			// merely constructing this service — and anything that transitively depends on it, such as
			// the Twilio controller used for incoming SMS — never forces the TTS RestClient to be built.
			// The TtsConfig.ServiceBaseUrl validation now fires on first use instead of at activation.
			_restClientFactory = restClientFactory ?? throw new ArgumentNullException(nameof(restClientFactory));
		}

		public async Task<Uri> GenerateSpeechUrlAsync(string text, string voice = null, int? speed = null, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(text))
				throw new ArgumentException("Text is required.", nameof(text));

			var restClient = _restClientFactory();

			var request = new RestRequest("tts", Method.Post);
			request.AddJsonBody(new GenerateSpeechRequest
			{
				Text = text,
				Voice = string.IsNullOrWhiteSpace(voice) ? TtsConfig.DefaultVoice : voice,
				Speed = speed ?? TtsConfig.DefaultSpeed
			});

			var response = await restClient.ExecuteAsync<GenerateSpeechResponse>(request, cancellationToken);

			if (!response.IsSuccessful || response.Data == null || string.IsNullOrWhiteSpace(response.Data.Url))
				throw CreateRequestFailure("generate speech audio", response);

			return new Uri(response.Data.Url, UriKind.Absolute);
		}

		public async Task RegenerateStaticPromptsAsync(IEnumerable<string> prompts, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(TtsConfig.StaticPromptAdminKey))
				throw new InvalidOperationException("TtsConfig.StaticPromptAdminKey must be configured before refreshing static prompts.");

			var promptRequests = prompts?
				.Where(prompt => !string.IsNullOrWhiteSpace(prompt))
				.Select(prompt => new GenerateSpeechRequest
				{
					Text = prompt.Trim(),
					Voice = TtsConfig.DefaultVoice,
					Speed = TtsConfig.DefaultSpeed
				})
				.ToList() ?? new List<GenerateSpeechRequest>();

			if (!promptRequests.Any())
				throw new ArgumentException("At least one static prompt is required.", nameof(prompts));

			var restClient = _restClientFactory();

			var request = new RestRequest("tts/admin/static-prompts", Method.Post);
			request.AddHeader(AdminKeyHeaderName, TtsConfig.StaticPromptAdminKey);
			request.AddJsonBody(new RegenerateStaticPromptsRequest
			{
				Prompts = promptRequests
			});

			var response = await restClient.ExecuteAsync(request, cancellationToken);

			if (!response.IsSuccessful)
				throw CreateRequestFailure("regenerate static prompts", response);
		}

		private static Exception CreateRequestFailure(string operation, RestResponse response)
		{
			if (response.ErrorException is OperationCanceledException or TaskCanceledException)
				return new OperationCanceledException($"The TTS service {operation} was canceled.", response.ErrorException);

			var status = response.StatusCode == 0 ? "no-response" : response.StatusCode.ToString();
			var detail = response.ErrorException?.Message;

			if (string.IsNullOrWhiteSpace(detail))
				detail = response.ErrorMessage;

			if (string.IsNullOrWhiteSpace(detail) && !string.IsNullOrWhiteSpace(response.StatusDescription))
				detail = response.StatusDescription;

			if (string.IsNullOrWhiteSpace(detail))
				detail = "No additional error details were provided.";

			return new InvalidOperationException($"The TTS service failed to {operation}. Status: {status}. Detail: {detail}");
		}

		private sealed class GenerateSpeechRequest
		{
			public string Text { get; set; }
			public string Voice { get; set; }
			public int Speed { get; set; }
		}

		private sealed class GenerateSpeechResponse
		{
			public string Url { get; set; }
		}

		private sealed class RegenerateStaticPromptsRequest
		{
			public List<GenerateSpeechRequest> Prompts { get; set; }
		}
	}
}
