using System;
using System.Linq;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using EmailModule;
using PostmarkDotNet;
using PostmarkDotNet.Exceptions;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.EmailProvider
{
	public class PostmarkEmailSender : IEmailSender
	{
		public async Task<bool> SendEmail(MailMessage email)
		{
			try
			{
				var to = new StringBuilder();
				foreach (var t in email.To)
				{
					if (to.Length == 0)
						to.Append(t.Address);
					else
						to.Append("," + t.Address);
				}

				var message = new PostmarkMessage(email.From.Address, to.ToString(), email.Subject, StringHelpers.StripHtmlTagsCharArray(email.Body), email.Body);

				var newClient = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);

				var response = await newClient.SendMessageAsync(message);

				if (response.ErrorCode != 200 && response.ErrorCode != 406 && response.Message != "OK" &&
				    !response.Message.Contains("You tried to send to a recipient that has been marked as inactive"))
				{
					Logging.LogError(string.Format(
						"Error from PostmarkEmailSender->SendEmail: {3} {0} FromEmail:{1} ToEmail:{2}",
						response.Message, email.From.Address, email.To.First().Address, response.ErrorCode));

					return false;
				}

				return true;
			}
			catch (PostmarkValidationException) { }
			catch// (Exception ex)
			{
				//if (!ex.ToString().Contains("You tried to send to a recipient that has been marked as inactive."))
				//	Logging.LogException(ex);
			}

			return false;
		}

		public async Task<bool> Send(Email email)
		{
			var mail = CreateMailMessageFromEmail(email);

			try
			{
				if (SystemBehaviorConfig.OutboundEmailType == OutboundEmailTypes.Postmark)
				{
					if (mail.From != null && !String.IsNullOrWhiteSpace(mail.From.Address))
					{
						var to = new StringBuilder();
						foreach (var t in email.To)
						{
							if (to.Length == 0)
								to.Append(t);
							else
								to.Append("," + t);
						}

						var message = new PostmarkMessage(email.From, to.ToString(), email.Subject, StringHelpers.StripHtmlTagsCharArray(email.HtmlBody), email.HtmlBody);
						var newClient = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);

						if (!String.IsNullOrWhiteSpace(email.AttachmentName) && email.AttachmentData.Length > 0)
						{
							message.AddAttachment(email.AttachmentData, email.AttachmentName, email.AttachmentContentType);
						}

						var response = await newClient.SendMessageAsync(message);

						if (response.ErrorCode != 200 && response.ErrorCode != 406 && response.Message != "OK" &&
						    !response.Message.Contains(
							    "You tried to send to a recipient that has been marked as inactive"))
						{
							Logging.LogError(string.Format(
								"Error from PostmarkEmailSender->Send: {3} {0} FromEmail:{1} ToEmail:{2}",
								response.Message, mail.From.Address, mail.To.First().Address, response.ErrorCode));

							return false;
						}

						return true;
					}
					else
					{
						try
						{
							using (var smtpClient = new SmtpClient
							{
								DeliveryMethod = SmtpDeliveryMethod.Network,
								Host = Config.OutboundEmailServerConfig.Host
							})
							{
								if (!String.IsNullOrWhiteSpace(OutboundEmailServerConfig.UserName) && !String.IsNullOrWhiteSpace(OutboundEmailServerConfig.Password))
								{
									smtpClient.Credentials = new System.Net.NetworkCredential(OutboundEmailServerConfig.UserName, OutboundEmailServerConfig.Password);
								}

								smtpClient.Send(mail);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
				}
				else
				{
					try
					{
						using (var smtpClient = new SmtpClient
						{
							DeliveryMethod = SmtpDeliveryMethod.Network,
							Host = OutboundEmailServerConfig.Host
						})
						{
							if (!String.IsNullOrWhiteSpace(OutboundEmailServerConfig.UserName) && !String.IsNullOrWhiteSpace(OutboundEmailServerConfig.Password))
							{
								smtpClient.Credentials = new System.Net.NetworkCredential(OutboundEmailServerConfig.UserName, OutboundEmailServerConfig.Password);
							}

							smtpClient.Send(mail);
						}
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
			}
			catch (PostmarkValidationException) { }
			catch (Exception)
			{
				try
				{
					using (var smtpClient = new SmtpClient
					{
						DeliveryMethod = SmtpDeliveryMethod.Network,
						Host = Config.OutboundEmailServerConfig.Host
					})
					{
						smtpClient.Credentials = new System.Net.NetworkCredential(Config.OutboundEmailServerConfig.UserName, Config.OutboundEmailServerConfig.Password);
						smtpClient.Send(mail);
					}
				}
				catch (Exception ex)
				{
					if (!ex.ToString().Contains("You tried to send to a recipient that has been marked as inactive."))
						Logging.LogException(ex);
				}
			}

			return false;
		}

		public MailMessage CreateMailMessageFromEmail(Email email)
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
		}
	}
}
