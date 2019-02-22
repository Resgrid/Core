using PostmarkDotNet;
using PostmarkDotNet.Exceptions;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Web;

namespace Resgrid.Providers.EmailProvider
{
	public class PostmarkTemplateProvider : IEmailProvider
	{
		private readonly IEmailSender _emailSender;

		private static string FROM_EMAIL = OutboundEmailServerConfig.ToMail;
		private static string DONOTREPLY_EMAIL = OutboundEmailServerConfig.FromMail;
		private static string LOGIN_URL = $"{SystemBehaviorConfig.ResgridBaseUrl}/Account/LogOn";
		private static string LIVECHAT_URL = $"{SystemBehaviorConfig.ResgridBaseUrl}/Home/Contact";
		private static string HELP_URL = "https://resgrid.uservoice.com";
		private static string UPDATEBILLINGINFO_URL = $"{SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription/UpdateBillingInfo";

		public PostmarkTemplateProvider(IEmailSender emailSender)
		{
			_emailSender = emailSender;
		}

		public void Configure(object sender, string fromAddress)
		{

		}

		public void SendAffiliateRegister(string email, string affiliateCode)
		{
			throw new NotImplementedException();
		}

		public void SendAffiliateRejection(string email, string rejectionReason)
		{
			throw new NotImplementedException();
		}

		public void SendAffiliateWelcomeMail(string name, string email)
		{
			throw new NotImplementedException();
		}

		public void SendCallMail(string email, string subject, string title, string priority, string natureOfCall, string mapPage, string address,
			string dispatchedOn, int callId, string userId, string coordinates, string shortenedAudioUrl)
		{

			string callQuery = String.Empty;

			try
			{
				callQuery = HttpUtility.UrlEncode(SymmetricEncryption.Encrypt(callId.ToString(), Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase));
			}
			catch { }

			var templateModel = new Dictionary<string, object>
			{
				{"subject", title},
				{"date", dispatchedOn},
				{"nature", HtmlToTextHelper.ConvertHtml(natureOfCall)},
				{"priority", priority},
				{"address", address},
				{"map_page", mapPage},
				{"action_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Dispatch/CallExportEx?query={callQuery}"},
				{"userId", userId},
				{"coordinates", coordinates}
			};

			if (!String.IsNullOrWhiteSpace(shortenedAudioUrl))
			{
				templateModel.Add("hasCallAudio", "true");
				templateModel.Add("callAudio_url", shortenedAudioUrl);
			}

			if (SystemBehaviorConfig.OutboundEmailType == OutboundEmailTypes.Postmark)
			{
				var message = new TemplatedPostmarkMessage
				{
					From = DONOTREPLY_EMAIL,
					To = email,
					TemplateId = Config.OutboundEmailServerConfig.PostmarkCallEmailTemplateId,
					TemplateModel = templateModel
				};

				var client = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);

				try
				{
					PostmarkResponse response = client.SendMessageAsync(message).Result;

					if (response.Status != PostmarkStatus.Success)
					{
						//Console.WriteLine("Response was: " + response.Message);
					}
				}
				catch (PostmarkValidationException) { }
			}
			else
			{
				var template = Mustachio.Parser.Parse(GetTempate("Call.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.To.Add(email);

				_emailSender.Send(newEmail);
			}
		}

		public void SendTroubleAlertMail(string email, string unitName, string gpsLocation, string personnel, string callAddress, string unitAddress, string dispatchedOn, string callName)
		{
			// Example request
			var message = new TemplatedPostmarkMessage
			{
				From = DONOTREPLY_EMAIL,
				To = email,
				TemplateId = Config.OutboundEmailServerConfig.PostmarkTroubleAlertTemplateId,
				TemplateModel = new Dictionary<string, object> {
					{ "unit_name", unitName },
					{ "date", dispatchedOn },
					{ "active_call", callName },
					{ "call_address", callAddress },
					{ "address", unitAddress },
					{ "gps_location", gpsLocation },
					{ "personnel_names", personnel }
				},
			};

			if (SystemBehaviorConfig.OutboundEmailType == OutboundEmailTypes.Postmark)
			{
				var client = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);

				try
				{
					PostmarkResponse response = client.SendMessageAsync(message).Result;

					if (response.Status != PostmarkStatus.Success)
					{
						//Console.WriteLine("Response was: " + response.Message);
					}
				}
				catch (PostmarkValidationException) { }
			}
			else
			{

			}
		}

		public void SendCancellationReciept(string name, string email, string endDate, string departmentName)
		{
			var message = new TemplatedPostmarkMessage
			{
				From = FROM_EMAIL,
				To = email,
				TemplateId = Config.OutboundEmailServerConfig.PostmarkCancelRecieptTemplateId,
				TemplateModel = new Dictionary<string, object> {
						{ "action_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription" },
						{ "subscriptions_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription" },
						{ "feedback_url", LIVECHAT_URL },
						{ "help_url", HELP_URL },
						{ "trial_extension_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription" },
						{ "export_url", "" },
						{ "plans_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/Home/Pricing" },
						{ "close_account_url", HELP_URL},
					},
			};

			var client = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);
			var response = client.SendMessageAsync(message).Result;
			if (response.Status != PostmarkStatus.Success)
			{
				//Console.WriteLine("Response was: " + response.Message);
			}
		}

		public void SendChargeFailed(string name, string email, string endDate, string departmentName, string planName)
		{
			// Example request
			var message = new TemplatedPostmarkMessage
			{
				From = FROM_EMAIL,
				To = email,
				TemplateId = Config.OutboundEmailServerConfig.PostmarkChargeFailedTemplateId,
				TemplateModel = new Dictionary<string, object> {
					{ "plan_name", planName },
					{ "action_url", UPDATEBILLINGINFO_URL },
					{ "subscriptions_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription" },
					{ "feedback_url", LIVECHAT_URL },
					{ "help_url", HELP_URL },
					{ "trial_extension_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription" },
					{ "export_url", "" },
					{ "close_account_url", HELP_URL },
				},
			};

			var client = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);
			try
			{
				PostmarkResponse response = client.SendMessageAsync(message).Result;

				if (response.Status != PostmarkStatus.Success)
				{
					//Console.WriteLine("Response was: " + response.Message);
				}
			}
			catch (PostmarkValidationException) { }
		}

		public void SendInviteMail(string code, string departmentName, string email, string senderName, string senderEmail)
		{
			// Example request
			var message = new TemplatedPostmarkMessage
			{
				From = FROM_EMAIL,
				To = email,
				TemplateId = Config.OutboundEmailServerConfig.PostmarkInviteTemplateId,
				TemplateModel = new Dictionary<string, object> {
					{ "invite_sender_name", senderName },
					{ "department_name", departmentName },
					{ "action_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/Account/CompleteInvite?inviteCode={code}" },
					{ "support_email", FROM_EMAIL },
					{ "live_chat_url", LIVECHAT_URL },
					{ "help_url", HELP_URL },
					{ "sender_email", senderEmail },
					{ "invite_sender_organization_name", departmentName },
				},
			};

			var client = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);
			try
			{
				PostmarkResponse response = client.SendMessageAsync(message).Result;

				if (response.Status != PostmarkStatus.Success)
				{
					//Console.WriteLine("Response was: " + response.Message);
				}
			}
			catch (PostmarkValidationException) { }
		}

		public void SendMessageMail(string email, string subject, string messageSubject, string messageBody, string senderEmail, string senderName, string sentOn, int messageId)
		{
			var templateModel = new Dictionary<string, object> {
					{ "sender_name", senderName },
					{ "title", subject },
					{ "body", HtmlToTextHelper.ConvertHtml(messageBody) },
					//{ "attachment_details", new []{
							//new Dictionary<string,object> {
							//	{ "attachmnet_url", "attachmnet_url_Value" },
							//	{ "attachment_name", "attachment_name_Value" },
							//	{ "attachment_size", "attachment_size_Value" },
							//	{ "attachment_type", "attachment_type_Value" },
							//}
						//}
					//},

					{ "action_url", $"https://resgrid.com/User/Messages/ViewMessage?messageId={messageId}" },
					{ "timestamp", sentOn },
					{ "commenter_name", senderName }
				};

			if (SystemBehaviorConfig.OutboundEmailType == OutboundEmailTypes.Postmark)
			{
				var message = new TemplatedPostmarkMessage
				{
					From = DONOTREPLY_EMAIL,
					To = email,
					TemplateId = Config.OutboundEmailServerConfig.PostmarkMessageTemplateId,
					TemplateModel = templateModel,
				};

				var client = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);
				try
				{
					PostmarkResponse response = client.SendMessageAsync(message).Result;

					if (response.Status != PostmarkStatus.Success)
					{
						//Console.WriteLine("Response was: " + response.Message);
					}
				}
				catch (PostmarkValidationException) { }
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}
			else
			{
				var template = Mustachio.Parser.Parse(GetTempate("Message.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.To.Add(email);

				_emailSender.Send(newEmail);
			}
		}

		public void SendPasswordResetMail(string name, string password, string userName, string email, string departmentName)
		{
			var templateModel = new Dictionary<string, object> {
					{ "name", name },
					{ "department_Name", departmentName },
					{ "login_url", LOGIN_URL },
					{ "username", userName },
					{ "password", password },
					{ "support_url", LIVECHAT_URL },
					{ "action_url", LOGIN_URL },
					{ "operating_system", "" },
					{ "browser_name", "" },
				};

			if (SystemBehaviorConfig.OutboundEmailType == OutboundEmailTypes.Postmark)
			{
				var message = new TemplatedPostmarkMessage
				{
					From = FROM_EMAIL,
					To = email,
					TemplateId = Config.OutboundEmailServerConfig.PostmarkResetPasswordTemplateId,
					TemplateModel = templateModel,
				};

				var client = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);
				try
				{
					PostmarkResponse response = client.SendMessageAsync(message).Result;

					if (response.Status != PostmarkStatus.Success)
					{
						//Console.WriteLine("Response was: " + response.Message);
					}
				}
				catch (PostmarkValidationException) { }
			}
			else
			{
				var template = Mustachio.Parser.Parse(GetTempate("PasswordReset.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.To.Add(email);

				_emailSender.Send(newEmail);
			}
		}

		public void SendPaymentReciept(string departmentName, string name, string processDate, string amount, string email, string processor, string transactionId,
			string planName, string effectiveDates, string nextBillingDate, int paymentId)
		{
			var message = new TemplatedPostmarkMessage
			{
				From = FROM_EMAIL,
				To = email,
				TemplateId = Config.OutboundEmailServerConfig.PostmarkRecieptTemplateId,
				TemplateModel = new Dictionary<string, object> {
					{ "purchase_date", processDate },
					{ "name", name },
					{ "billing_url", UPDATEBILLINGINFO_URL },
					{ "uservoice_url", LIVECHAT_URL },
					{ "receipt_id", transactionId },
					{ "date", effectiveDates },
					{ "receipt_details", new []{
							new Dictionary<string,object> {
								{ "description", planName },
								{ "amount", amount }
							}
						}
					},
					{ "total", amount },
					{ "support_url", HELP_URL },
					{ "action_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}User/Subscription/ViewInvoice?paymentId={paymentId}" },
					{ "credit_card_brand", "" },
					{ "credit_card_last_four", "" },
					{ "expiration_date", "" },
				},
			};

			var client = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);
			try
			{
				PostmarkResponse response = client.SendMessageAsync(message).Result;

				if (response.Status != PostmarkStatus.Success)
				{
					//Console.WriteLine("Response was: " + response.Message);
				}
			}
			catch (PostmarkValidationException) { }
		}

		public void SendRefundReciept(string name, string email, string departmentName, string processDate, string amount, string processor, string transactionId, string originalPaymentId)
		{
			throw new NotImplementedException();
		}

		public void SendSignupMail(string name, string departmentName, string email)
		{
			throw new NotImplementedException();
		}

		public void SendUpgradePaymentReciept(string departmentName, string processDate, string amount, string email, string processor, string transactionId, string planName, string newPlanName, string effectiveDates, string nextBillingDate)
		{
			throw new NotImplementedException();
		}

		public void SendWelcomeMail(string name, string departmentName, string userName, string password, string email, int departmentId)
		{
			var templateModel = new Dictionary<string, object> {
					{ "name", name },
					{ "action_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}" },
					{ "login_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/Account/LogOn" },
					{ "department_id", departmentId },
					{ "department_name", departmentName },
					{ "username", userName },
					{ "password", password },
					{ "support_email", FROM_EMAIL },
					{ "live_chat_url", LIVECHAT_URL },
					{ "help_url", HELP_URL },
				};

			if (SystemBehaviorConfig.OutboundEmailType == OutboundEmailTypes.Postmark)
			{
				var message = new TemplatedPostmarkMessage
				{
					From = FROM_EMAIL,
					To = email,
					TemplateId = Config.OutboundEmailServerConfig.PostmarkWelcomeTemplateId,
					TemplateModel = templateModel,
				};

				var client = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);
				try
				{
					PostmarkResponse response = client.SendMessageAsync(message).Result;

					if (response.Status != PostmarkStatus.Success)
					{
						//Console.WriteLine("Response was: " + response.Message);
					}
				}
				catch (PostmarkValidationException) { }
			}
			else
			{
				var template = Mustachio.Parser.Parse(GetTempate("Welcome.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.To.Add(email);

				_emailSender.Send(newEmail);
			}
		}

		public void TEAM_SendNofifySubCancelled(string name, string email, string departmentName, string departmentId, string reason, string processedOn, string planName, string refundIssued)
		{
			throw new NotImplementedException();
		}

		public void TEAM_SendNotifyRefundIssued(string departmentId, string departmentName, string processDate, string amount, string processor, string transactionId, string originalPaymentId)
		{
			throw new NotImplementedException();
		}

		public void SendNewDepartmentLinkMail(string name, string departmentName, string data, string email, int departmentId)
		{
			// Example request
			var message = new TemplatedPostmarkMessage
			{
				From = FROM_EMAIL,
				To = email,
				TemplateId = Config.OutboundEmailServerConfig.PostmarkNewDepLinkTemplateId,
				TemplateModel = new Dictionary<string, object> {
					{ "name", name },
					{ "action_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}" },
					{ "login_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/Account/LogOn" },
					{ "department_name", departmentName },
					{ "data", data },
					{ "support_email", FROM_EMAIL },
					{ "live_chat_url", LIVECHAT_URL },
					{ "help_url", HELP_URL },
				},
			};

			var client = new PostmarkClient(Config.OutboundEmailServerConfig.PostmarkApiKey);
			try
			{
				PostmarkResponse response = client.SendMessageAsync(message).Result;

				if (response.Status != PostmarkStatus.Success)
				{
					//Console.WriteLine("Response was: " + response.Message);
				}
			}
			catch (PostmarkValidationException) { }
		}

		private string GetTempate(string templateName)
		{
			using (var resource = typeof(PostmarkTemplateProvider).Assembly.GetManifestResourceStream(templateName))
			{
				using (var reader = new StreamReader(resource))
				{
					return reader.ReadToEnd();
				}
			}
		}
	}
}
