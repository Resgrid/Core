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
	}
}
