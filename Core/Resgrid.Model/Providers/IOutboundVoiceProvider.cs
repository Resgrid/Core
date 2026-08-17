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

		/// <summary>
		/// Places a Twilio voice call for a communication test. The call plays the test prompt and
		/// gathers a keypress, which is recorded against <paramref name="responseToken"/>.
		/// </summary>
		Task<bool> SendCommunicationTestCallAsync(string phoneNumber, string responseToken);
	}
}
