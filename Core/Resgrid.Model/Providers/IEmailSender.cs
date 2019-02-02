using System.Net.Mail;

namespace Resgrid.Model.Providers
{
	public interface IEmailSender
	{
		void SendEmail(MailMessage email);
		void Send(Email email);
		MailMessage CreateMailMessageFromEmail(Email email);
	}
}