namespace Resgrid.Model.Providers
{
	public interface ITextMessageProvider
	{
		void SendTextMessage(string number, string message, string departmentNumber, MobileCarriers carrier, bool forceGateway = false);
	}
}
