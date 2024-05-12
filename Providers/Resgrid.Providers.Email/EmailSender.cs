using EmailModule;
using Resgrid.Model;
using Resgrid.Model.Providers;
using System;
using System.Net.Mail;
using System.Net.Mime;
using System.Threading.Tasks;

namespace Resgrid.Providers.EmailProvider
{
	public class EmailSender : IEmailSender
	{
		public EmailSender()
		{
			CreateClientFactory = CreateDefaultClientFactory();
			CreateMailMessageFactory = CreateDefaultMailMessageFactory();
		}

		public Func<ISmtpClient> CreateClientFactory { get; set; }

		public Func<Email, MailMessage> CreateMailMessageFactory { get; set; }

		public async Task<bool> Send(Email email)
		{
			Invariant.IsNotNull(email, "email");

			while (true)
			{
				try
				{
					using (var message = CreateDefaultMailMessageFactory()(email))
					{
						using (var client = CreateClientFactory())
						{
							await client.Send(message);
						}
					}

					return true;
				}
				catch (InvalidOperationException)
				{
				}
			}

			return false;
		}

		public async Task<bool> SendEmail(MailMessage email)
		{
			try
			{
				using (var client = CreateClientFactory())
				{
					await client.Send(email);
				}

				return true;
			}
			catch (InvalidOperationException)
			{
			}

			return false;
		}

		public MailMessage CreateMailMessageFromEmail(Email email)
		{
			// Do Nothing

			return null;
		}

		private static Func<ISmtpClient> CreateDefaultClientFactory()
		{
			return () => new SmtpClientWrapper(new SmtpClient());
		}

		private static Func<Email, MailMessage> CreateDefaultMailMessageFactory()
		{
			return email =>
			{
				var message = new MailMessage { From = new MailAddress(email.From), Subject = email.Subject };

				if (!string.IsNullOrEmpty(email.Sender))
				{
					message.Sender = new MailAddress(email.Sender);
				}

				email.To.Each(to => message.To.Add(to));
				email.ReplyTo.Each(to => message.ReplyToList.Add(to));
				email.CC.Each(cc => message.CC.Add(cc));
				email.Bcc.Each(bcc => message.Bcc.Add(bcc));
				email.Headers.Each(pair => message.Headers[pair.Key] = pair.Value);

				if (!string.IsNullOrEmpty(email.HtmlBody) && !string.IsNullOrEmpty(email.TextBody))
				{
					message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(email.HtmlBody, new ContentType(ContentTypes.Html)));
					//message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(email.TextBody, new ContentType(ContentTypes.Text)));
				}
				else if (!string.IsNullOrEmpty(email.HtmlBody))
				{
					message.Body = email.HtmlBody;
					message.IsBodyHtml = true;
				}
				else if (!string.IsNullOrEmpty(email.TextBody))
				{
					message.Body = email.TextBody;
					message.IsBodyHtml = false;
				}

				return message;
			};
		}
	}
}
