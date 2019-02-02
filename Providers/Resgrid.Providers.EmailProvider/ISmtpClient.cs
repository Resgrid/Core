namespace EmailModule
{
    using System;
    using System.Net.Mail;

    public interface ISmtpClient : IDisposable
    {
        void Send(MailMessage message);
    }
}