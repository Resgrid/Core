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
