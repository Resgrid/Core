using System.Threading.Tasks;

namespace EmailModule
{
    using System;
    using System.Net.Mail;

    public interface ISmtpClient : IDisposable
    {
	    Task<bool> Send(MailMessage message);
    }
}
