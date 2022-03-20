using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface ITextMessageProvider
	{
		Task<bool> SendTextMessage(string number, string message, string departmentNumber, MobileCarriers carrier, int departmentId, bool forceGateway = false, bool isCall = false);
	}
}
