using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface ITextMessageProvider
	{
		/// <param name="maxLengthOverride">When greater than zero, overrides the default outbound SMS
		/// length cap (SystemBehaviorConfig.SmsMaxLength) for this message — used by interactive chatbot
		/// replies that must not be cut short. Still bounded by the provider's hard limit.</param>
		Task<bool> SendTextMessage(string number, string message, string departmentNumber, MobileCarriers carrier, int departmentId, bool forceGateway = false, bool isCall = false, int maxLengthOverride = 0);
	}
}
