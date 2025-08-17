using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IOutboundVoiceProvider
	{
		Task<bool> CommunicateCallAsync(string phoneNumber, UserProfile profile, Call call);
	}
}
