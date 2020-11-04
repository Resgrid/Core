using System.Net.Mail;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IEmailSender
	{
		Task<bool> SendEmail(MailMessage email);
		Task<bool> Send(Email email);
		MailMessage CreateMailMessageFromEmail(Email email);
	}
}
