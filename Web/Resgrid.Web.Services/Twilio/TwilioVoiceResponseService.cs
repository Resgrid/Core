using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Services;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Resgrid.Web.Services.Twilio
{
	public class TwilioVoiceResponseService : ITwilioVoiceResponseService
	{
		private readonly ITtsAudioService _ttsAudioService;
		private readonly ConcurrentDictionary<string, Lazy<Task<Uri>>> _promptUrlCache = new(StringComparer.Ordinal);

		public TwilioVoiceResponseService(ITtsAudioService ttsAudioService)
		{
			_ttsAudioService = ttsAudioService;
		}

		public async System.Threading.Tasks.Task AppendPromptAsync(VoiceResponse response, string text, CancellationToken cancellationToken = default, string voice = null)
		{
			foreach (var verb in await CreatePlayVerbsAsync(text, voice, cancellationToken))
			{
				response.Append(verb);
			}
		}

		public async System.Threading.Tasks.Task AppendPromptAsync(Gather gather, string text, CancellationToken cancellationToken = default, string voice = null)
		{
			foreach (var verb in await CreatePlayVerbsAsync(text, voice, cancellationToken))
			{
				gather.Append(verb);
			}
		}

		public async System.Threading.Tasks.Task AppendPromptsAsync(VoiceResponse response, IEnumerable<string> prompts, CancellationToken cancellationToken = default, string voice = null)
		{
			foreach (var prompt in prompts)
			{
				await AppendPromptAsync(response, prompt, cancellationToken, voice);
			}
		}

		public async System.Threading.Tasks.Task AppendPromptsAsync(Gather gather, IEnumerable<string> prompts, CancellationToken cancellationToken = default, string voice = null)
		{
			foreach (var prompt in prompts)
			{
				await AppendPromptAsync(gather, prompt, cancellationToken, voice);
			}
		}

		public void AppendSayFallback(TwiML parent, string text)
		{
			if (!TtsConfig.TwilioSayFallbackEnabled)
			{
				LogSayFallbackSkipped(text);
				return;
			}

			foreach (var chunk in ChunkText(text))
			{
				parent.Append(new Say(chunk));
			}
		}

		private static void LogSayFallbackSkipped(string text)
		{
			// Deliberate silence: TTS couldn't produce this prompt and the paid <Say>
			// fallback is disabled (TtsConfig.TwilioSayFallbackEnabled). Log so a TTS
			// outage is visible as skipped prompts rather than passing unnoticed.
			Logging.LogInfo($"[Twilio Voice] TTS unavailable and Say fallback is disabled; skipping prompt ({text?.Length ?? 0} chars).");
		}

		private async Task<IReadOnlyCollection<TwiML>> CreatePlayVerbsAsync(string text, string voice, CancellationToken cancellationToken)
		{
			var chunks = ChunkText(text).ToList();

			if (!chunks.Any())
			{
				return new List<TwiML>();
			}

			try
			{
				var urls = await System.Threading.Tasks.Task.WhenAll(chunks.Select(chunk => GetOrCreatePromptUrlAsync(chunk, voice, cancellationToken)));
				return urls.Select(CreatePlay).Cast<TwiML>().ToList();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// Caller-driven cancellation (e.g. the dispatch playback timeout) is control
				// flow, not a failure — the caller decides what to do next.
				throw;
			}
			catch (Exception ex)
			{
				// The TTS microservice is unavailable or errored. Never bubble a 500 out of
				// a voice webhook (Twilio reads it to the caller as "an application error
				// has occurred"): degrade to Twilio's native <Say> voice when the paid
				// fallback is enabled, otherwise skip the prompt and keep the call flowing.
				Logging.LogException(ex);

				if (!TtsConfig.TwilioSayFallbackEnabled)
				{
					LogSayFallbackSkipped(text);
					return new List<TwiML>();
				}

				return chunks.Select(chunk => (TwiML)new Say(chunk)).ToList();
			}
		}

		// Chunking lives in DispatchVoicePromptBuilder so the call broadcast worker
		// pre-warms exactly the chunks this service will request — the TTS cache key
		// is a hash of the exact chunk text.
		private static IEnumerable<string> ChunkText(string text)
		{
			return global::Resgrid.Services.DispatchVoicePromptBuilder.ChunkText(text);
		}

		private static Play CreatePlay(Uri url)
		{
			return new Play
			{
				Url = url
			};
		}

		public System.Threading.Tasks.Task PreWarmPromptAsync(string text, string voice = null)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(text);

			// Start generation for each chunk (or return the existing task) without
			// necessarily awaiting it. The TTS microservice's internal cache persists
			// across requests, so a subsequent call (GetPromptUrlAsync/AppendPromptAsync)
			// will find the URLs. Long dispatch text spans multiple chunks, so warm them all.
			foreach (var chunk in ChunkText(text))
			{
				GetOrCreatePromptUrlAsync(chunk, voice, CancellationToken.None)
					.ContinueWith(t =>
					{
						if (t.IsFaulted && t.Exception != null)
							Logging.LogException(t.Exception);
					}, TaskContinuationOptions.OnlyOnFaulted);
			}
			return System.Threading.Tasks.Task.CompletedTask;
		}

		public async System.Threading.Tasks.Task<Uri> GetPromptUrlAsync(string text, string voice, CancellationToken cancellationToken)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(text);

			var chunks = ChunkText(text).ToList();
			if (chunks.Count != 1)
				throw new ArgumentException($"GetPromptUrlAsync does not support multi-chunk input (got {chunks.Count} chunks). Use AppendPromptAsync for multi-chunk text.", nameof(text));

			return await GetOrCreatePromptUrlAsync(chunks[0], voice, cancellationToken);
		}

		private async Task<Uri> GetOrCreatePromptUrlAsync(string chunk, string voice, CancellationToken cancellationToken)
		{
			var cacheKey = string.IsNullOrWhiteSpace(voice)
				? chunk
				: $"{voice.Trim()}\u001F{chunk}";
			var lazyUrl = _promptUrlCache.GetOrAdd(
				cacheKey,
				_ => new Lazy<Task<Uri>>(
					() => _ttsAudioService.GenerateSpeechUrlAsync(chunk, voice, cancellationToken: CancellationToken.None),
					LazyThreadSafetyMode.ExecutionAndPublication));
			var generationTask = lazyUrl.Value;

			try
			{
				return cancellationToken.CanBeCanceled
					? await generationTask.WaitAsync(cancellationToken)
					: await generationTask;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch
			{
				_promptUrlCache.TryRemove(cacheKey, out _);
				throw;
			}
		}
	}
}
