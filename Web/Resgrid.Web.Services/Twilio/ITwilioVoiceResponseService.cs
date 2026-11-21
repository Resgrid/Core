using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Resgrid.Web.Services.Twilio
{
	public interface ITwilioVoiceResponseService
	{
		System.Threading.Tasks.Task AppendPromptAsync(VoiceResponse response, string text, CancellationToken cancellationToken = default, string voice = null);

		System.Threading.Tasks.Task AppendPromptAsync(Gather gather, string text, CancellationToken cancellationToken = default, string voice = null);

		System.Threading.Tasks.Task AppendPromptsAsync(VoiceResponse response, IEnumerable<string> prompts, CancellationToken cancellationToken = default, string voice = null);

		System.Threading.Tasks.Task AppendPromptsAsync(Gather gather, IEnumerable<string> prompts, CancellationToken cancellationToken = default, string voice = null);

		/// <summary>
		/// Appends the spoken call dispatch (one &lt;Play&gt; per chunk) at the dispatch speaking
		/// rate (DispatchVoicePromptBuilder.DispatchSpeed), which is slower than the menus. Use this,
		/// not AppendPromptAsync, for dispatch text: the broadcast worker pre-warms the audio at that
		/// rate, and speed is part of the TTS cache key.
		/// </summary>
		System.Threading.Tasks.Task AppendDispatchPromptAsync(VoiceResponse response, string dispatchText, CancellationToken cancellationToken, string voice);

		/// <summary>
		/// PreWarmPromptAsync for dispatch text, at the dispatch speaking rate (see AppendDispatchPromptAsync).
		/// </summary>
		System.Threading.Tasks.Task PreWarmDispatchPromptAsync(string dispatchText, string voice);

		/// <summary>
		/// Appends the text as Twilio-native &lt;Say&gt; verbs to the given TwiML
		/// parent (VoiceResponse, Gather, ...), split into the same chunks the TTS
		/// pipeline would use so no single verb exceeds Twilio's per-&lt;Say&gt;
		/// text limit. No-op unless TtsConfig.TwilioSayFallbackEnabled. Used when
		/// the TTS time budget is exhausted or generation has already failed.
		/// </summary>
		void AppendSayFallback(TwiML parent, string text);

		/// <summary>
		/// Kicks off TTS generation for the given text in the background so that
		/// a subsequent call to GetPromptUrlAsync (or AppendPromptAsync) will find
		/// the URL already cached. Does not throw on failure — the cache entry is
		/// removed automatically if generation fails.
		/// </summary>
		System.Threading.Tasks.Task PreWarmPromptAsync(string text, string voice = null);

		/// <summary>
		/// Returns the TTS audio URL for the given text, generating it if necessary.
		/// Respects the provided cancellation token for timeout control.
		/// </summary>
		System.Threading.Tasks.Task<Uri> GetPromptUrlAsync(string text, string voice, CancellationToken cancellationToken);
	}
}
