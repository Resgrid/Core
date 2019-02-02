namespace Resgrid.Model.Providers
{
	public interface IOutboundVoiceProvider
	{
		void CommunicateCall(string phoneNumber, UserProfile profile, Call call);
	}
}