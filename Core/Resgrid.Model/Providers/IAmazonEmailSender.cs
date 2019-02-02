using MimeKit;

namespace Resgrid.Model.Providers
{
	public interface IAmazonEmailSender
	{
		void SendDistributionListEmail(MimeMessage message);
	}
}