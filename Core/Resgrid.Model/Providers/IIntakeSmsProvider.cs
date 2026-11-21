using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>The carrier's answer to one intake conversation text.</summary>
	public sealed class IntakeSmsSendResult
	{
		public bool Success { get; set; }

		/// <summary>The carrier's message id (Twilio MessageSid) when it accepted the text.</summary>
		public string ProviderMessageId { get; set; }

		/// <summary>Why it was not accepted (no number or content).</summary>
		public string Error { get; set; }
	}

	/// <summary>
	/// Texts a member of the public on an intake SMS conversation (plan §8 D14/D15) from the region's master Resgrid number
	/// (NumberProviderConfig.TwilioResgridNumber; dedicated numbers are inbound-only, D5). Unlike <see cref="ITextMessageProvider"/> it
	/// returns the carrier's message id for the conversation's delivery log. Applies the same outbound net (ADP envelope scrub, URL
	/// allowlist) and never throws.
	/// </summary>
	public interface IIntakeSmsProvider
	{
		Task<IntakeSmsSendResult> SendAsync(string toNumber, string text, int departmentId, int maxLength, CancellationToken cancellationToken = default);
	}
}
