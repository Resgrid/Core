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
			foreach (var play in await CreatePlayVerbsAsync(text, voice, cancellationToken))
			{
				response.Append(play);
			}
		}

		public async System.Threading.Tasks.Task AppendPromptAsync(Gather gather, string text, CancellationToken cancellationToken = default, string voice = null)
		{
			foreach (var play in await CreatePlayVerbsAsync(text, voice, cancellationToken))
			{
				gather.Append(play);
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

		private async Task<IReadOnlyCollection<Play>> CreatePlayVerbsAsync(string text, string voice, CancellationToken cancellationToken)
		{
			var chunks = ChunkText(text).ToList();

			if (!chunks.Any())
			{
				return new List<Play>();
			}

			var urls = await System.Threading.Tasks.Task.WhenAll(chunks.Select(chunk => GetOrCreatePromptUrlAsync(chunk, voice, cancellationToken)));
			return urls.Select(CreatePlay).ToList();
		}

		private IEnumerable<string> ChunkText(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				yield break;

			var normalized = Regex.Replace(text, @"\s+", " ").Trim();
			var maxLength = TtsConfig.MaxTextLength > 0 ? TtsConfig.MaxTextLength : 1000;

			if (normalized.Length <= maxLength)
			{
				yield return normalized;
				yield break;
			}

			var sentences = Regex.Split(normalized, @"(?<=[\.\!\?])\s+")
				.Where(sentence => !string.IsNullOrWhiteSpace(sentence));
			var builder = new StringBuilder();

			foreach (var sentence in sentences)
			{
				var trimmed = sentence.Trim();

				if (trimmed.Length > maxLength)
				{
					foreach (var fragment in ChunkLongSentence(trimmed, maxLength))
					{
						if (builder.Length > 0)
						{
							yield return builder.ToString();
							builder.Clear();
						}

						yield return fragment;
					}

					continue;
				}

				if (builder.Length == 0)
				{
					builder.Append(trimmed);
					continue;
				}

				if (builder.Length + 1 + trimmed.Length <= maxLength)
				{
					builder.Append(' ').Append(trimmed);
					continue;
				}

				yield return builder.ToString();
				builder.Clear();
				builder.Append(trimmed);
			}

			if (builder.Length > 0)
			{
				yield return builder.ToString();
			}
		}

		private static IEnumerable<string> ChunkLongSentence(string sentence, int maxLength)
		{
			var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var builder = new StringBuilder();

			foreach (var word in words)
			{
				if (word.Length > maxLength)
				{
					if (builder.Length > 0)
					{
						yield return builder.ToString();
						builder.Clear();
					}

					for (var index = 0; index < word.Length; index += maxLength)
					{
						yield return word.Substring(index, Math.Min(maxLength, word.Length - index));
					}

					continue;
				}

				if (builder.Length == 0)
				{
					builder.Append(word);
					continue;
				}

				if (builder.Length + 1 + word.Length <= maxLength)
				{
					builder.Append(' ').Append(word);
					continue;
				}

				yield return builder.ToString();
				builder.Clear();
				builder.Append(word);
			}

			if (builder.Length > 0)
			{
				yield return builder.ToString();
			}
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

			// Start the generation task (or return the existing one) without
			// Start the generation task (or return the existing one) without
			// necessarily awaiting it. The TTS microservice's internal cache
			// persists across requests, so a subsequent call will find the URL.
			GetOrCreatePromptUrlAsync(text, voice, CancellationToken.None)
				.ContinueWith(t =>
				{
					if (t.IsFaulted && t.Exception != null)
						Logging.LogException(t.Exception);
				}, TaskContinuationOptions.OnlyOnFaulted);
			return System.Threading.Tasks.Task.CompletedTask;
		}

		public async System.Threading.Tasks.Task<Uri> GetPromptUrlAsync(string text, string voice, CancellationToken cancellationToken)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(text);

			return await GetOrCreatePromptUrlAsync(text, voice, cancellationToken);
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
