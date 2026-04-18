using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IOutboundVoiceProvider
	{
		Task<bool> CommunicateCallAsync(string phoneNumber, UserProfile profile, Call call);

		/// <summary>
		/// Places a Twilio voice call that speaks the verification code digits to the user.
		/// </summary>
		Task<bool> SendVoiceVerificationCallAsync(string phoneNumber, string userId, int contactType);
	}
}
