using System.Threading.Tasks;
using MimeKit;

namespace Resgrid.Model.Providers
{
	public interface IAmazonEmailSender
	{
		Task<bool> SendDistributionListEmail(MimeMessage message);
	}
}
