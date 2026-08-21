using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Resgrid.Providers.EmailProvider
{
	public class PostmarkTemplateProvider : IEmailProvider
	{
		private readonly IEmailSender _emailSender;

		private static string FROM_EMAIL = OutboundEmailServerConfig.FromMail;
		private static string DONOTREPLY_EMAIL = OutboundEmailServerConfig.FromMail;
		private static string LOGIN_URL = $"{SystemBehaviorConfig.ResgridBaseUrl}/Account/LogOn";
		private static string LIVECHAT_URL = $"https://resgrid.com/contact";
		private static string HELP_URL = "https://resgrid.zohodesk.com/portal/en/homem";
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


		public async Task<bool> SendDeleteDepartmentEmail(string requesterName, string departmentName, DateTime localCompletedOn, string sendingToPersonName, string email)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "requester_name", requesterName },
				{ "department_name", departmentName },
				{ "local_deletion_date", localCompletedOn.ToString("F") },
				{ "name", sendingToPersonName },
				{ "login_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/Account/LogOn" },
				{ "support_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/Home/Contact" },
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("DeleteDepartment.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.From = FROM_EMAIL;
				newEmail.Subject = "Resgrid Department Deletion Request";
				newEmail.To.Add(email);

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}


		public async Task<bool> SendCallMail(string email, string subject, string title, string priority, string natureOfCall, string mapPage, string address,
			string dispatchedOn, int callId, string userId, string coordinates, string shortenedAudioUrl)
		{
			string callQuery = String.Empty;

			try
			{
				callQuery = Convert.ToBase64String(
					Encoding.UTF8.GetBytes(SymmetricEncryption.Encrypt(callId.ToString(), Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase)));
			}
			catch
			{
			}

			var templateModel = new Dictionary<string, object>
			{
				{ "subject", title },
				{ "date", dispatchedOn },
				{ "nature", HtmlToTextHelper.ConvertHtml(natureOfCall) },
				{ "priority", priority },
				{ "address", address },
				{ "map_page", mapPage },
				{ "action_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Dispatch/CallExportEx?query={callQuery}" },
				{ "userId", userId },
				{ "coordinates", coordinates }
			};

			if (!String.IsNullOrWhiteSpace(shortenedAudioUrl))
			{
				templateModel.Add("hasCallAudio", "true");
				templateModel.Add("callAudio_url", shortenedAudioUrl);
			}


			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("Call.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.From = FROM_EMAIL;
				newEmail.Subject = "New Call: " + subject;
				newEmail.To.Add(email);

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}

		public async Task<bool> SendTroubleAlertMail(string email, string unitName, string gpsLocation, string personnel, string callAddress, string unitAddress,
			string dispatchedOn, string callName)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "unit_name", unitName },
					{ "date", dispatchedOn },
					{ "active_call", callName },
					{ "call_address", callAddress },
					{ "address", unitAddress },
					{ "gps_location", gpsLocation },
					{ "personnel_names", personnel }
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("TroubleAlert.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.From = FROM_EMAIL;
				newEmail.Subject = "Resgrid Trouble Alert";
				newEmail.To.Add(email);

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}

		public async Task<bool> SendCancellationReciept(string name, string email, string endDate, string departmentName)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "action_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription" },
				{ "subscriptions_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription" },
				{ "help_url", HELP_URL },
				{ "trial_extension_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription" },
				{ "export_url", "" },
				{ "plans_url", $"https://resgrid.com/pricing" },
				{ "close_account_url", HELP_URL },
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("Cancelled.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.From = FROM_EMAIL;
				newEmail.Subject = "Resgrid Subscription Canceled";
				newEmail.To.Add(email);

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}

		public async Task<bool> SendChargeFailed(string name, string email, string endDate, string departmentName, string planName)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "plan_name", planName },
				{ "action_url", UPDATEBILLINGINFO_URL },
				{ "subscriptions_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription" },
				{ "help_url", HELP_URL },
				{ "trial_extension_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription" },
				{ "export_url", "" },
				{ "close_account_url", HELP_URL },
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("ChargeFailed.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.From = FROM_EMAIL;
				newEmail.Subject = "Resgrid Subscription Payment Failed";
				newEmail.To.Add(email);

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}

		public async Task<bool> SendInviteMail(string code, string departmentName, string email, string senderName, string senderEmail)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "invite_sender_name", senderName },
				{ "department_name", departmentName },
				{ "action_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/Account/CompleteInvite?inviteCode={code}" },
				{ "support_email", FROM_EMAIL },
				{ "live_chat_url", LIVECHAT_URL },
				{ "help_url", HELP_URL },
				{ "sender_email", senderEmail },
				{ "invite_sender_organization_name", departmentName },
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("Invitation.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.From = FROM_EMAIL;
				newEmail.Subject = $"You're invited to Join {departmentName} in Resgrid";
				newEmail.To.Add(email);

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}

		public async Task<bool> SendMessageMail(string email, string subject, string messageSubject, string messageBody, string senderEmail, string senderName, string sentOn,
			int messageId)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "sender_name", senderName },
				{ "title", subject },
				{ "body", HtmlToTextHelper.ConvertHtml(messageBody) },
				{ "action_url", $"https://app.resgrid.com/User/Messages/ViewMessage?messageId={messageId}" },
				{ "timestamp", sentOn },
				{ "commenter_name", senderName }
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("Message.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.From = FROM_EMAIL;
				newEmail.Subject = $"Resgrid New Message: {subject}";
				newEmail.To.Add(email);

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}

		public async Task<bool> SendPasswordRecoveryMail(string name, string email,
			string departmentName, string resetUrl, string ipAddress, string userAgent, string requestedOn,
			bool isSsoManaged)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "name", System.Net.WebUtility.HtmlEncode(name) },
				{ "department_name", System.Net.WebUtility.HtmlEncode(departmentName) },
				{ "support_url", LIVECHAT_URL },
				{ "reset_url", System.Net.WebUtility.HtmlEncode(resetUrl) },
				{ "ip_address", System.Net.WebUtility.HtmlEncode(ipAddress) },
				{ "user_agent", System.Net.WebUtility.HtmlEncode(userAgent) },
				{ "requested_on", System.Net.WebUtility.HtmlEncode(requestedOn) },
				{ "has_reset_link", !isSsoManaged },
				{ "is_sso_managed", isSsoManaged },
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("PasswordRecovery.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.From = FROM_EMAIL;
				newEmail.Subject = "Resgrid password reset request";
				newEmail.To.Add(email);

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}

		public async Task<bool> SendPasswordChangedByAdministratorMail(string name, string userName,
			string email, string departmentName)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "name", System.Net.WebUtility.HtmlEncode(name) },
				{ "department_name", System.Net.WebUtility.HtmlEncode(departmentName) },
				{ "username", System.Net.WebUtility.HtmlEncode(userName) },
				{ "login_url", LOGIN_URL },
				{ "support_url", LIVECHAT_URL }
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("PasswordChangedByAdministrator.html"));
				var content = template(templateModel);
				var newEmail = new Email
				{
					HtmlBody = content,
					Sender = FROM_EMAIL,
					From = FROM_EMAIL,
					Subject = "Your Resgrid password was changed"
				};
				newEmail.To.Add(email);
				return await _emailSender.Send(newEmail);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		public async Task<bool> SendPaymentReciept(string departmentName, string name, string processDate, string amount, string email, string processor, string transactionId,
			string planName, string effectiveDates, string nextBillingDate, int paymentId)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "purchase_date", processDate },
				{ "name", name },
				{ "billing_url", UPDATEBILLINGINFO_URL },
				{ "uservoice_url", LIVECHAT_URL },
				{ "receipt_id", transactionId },
				{ "date", effectiveDates },
				{
					"receipt_details", new[]
					{
						new Dictionary<string, object>
						{
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
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("Receipt.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = FROM_EMAIL;
				newEmail.From = FROM_EMAIL;
				newEmail.Subject = $"Resgrid Password Reset";
				newEmail.To.Add(email);

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}

		public async Task<bool> SendRefundReciept(string name, string email, string departmentName, string processDate, string amount, string processor, string transactionId,
			string originalPaymentId)
		{
			throw new NotImplementedException();
		}

		public async Task<bool> SendSignupMail(string name, string departmentName, string email)
		{
			throw new NotImplementedException();
		}

		public async Task<bool> SendUpgradePaymentReciept(string departmentName, string processDate, string amount, string email, string processor, string transactionId,
			string planName, string newPlanName, string effectiveDates, string nextBillingDate)
		{
			throw new NotImplementedException();
		}

		public async Task<bool> SendWelcomeMail(string name, string departmentName, string userName, string email, int departmentId)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "name", name },
				{ "action_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}" },
				{ "login_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/Account/LogOn" },
				{ "department_id", departmentId },
				{ "department_name", departmentName },
				{ "username", userName },
				{ "support_email", FROM_EMAIL },
				{ "live_chat_url", LIVECHAT_URL },
				{ "help_url", HELP_URL },
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("Welcome.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = DONOTREPLY_EMAIL;
				newEmail.To.Add(email);
				newEmail.From = DONOTREPLY_EMAIL;
				newEmail.Subject = $"Welcome, {name} to Resgrid!";

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}

		public async Task<bool> TEAM_SendNofifySubCancelled(string name, string email, string departmentName, string departmentId, string reason, string processedOn,
			string planName, string refundIssued)
		{
			throw new NotImplementedException();
		}

		public async Task<bool> TEAM_SendNotifyRefundIssued(string departmentId, string departmentName, string processDate, string amount, string processor, string transactionId,
			string originalPaymentId)
		{
			throw new NotImplementedException();
		}

		public async Task<bool> SendNewDepartmentLinkMail(string name, string departmentName, string data, string email, int departmentId)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "name", name },
					{ "action_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}" },
					{ "login_url", $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/Account/LogOn" },
					{ "department_name", departmentName },
					{ "data", data },
					{ "support_email", FROM_EMAIL },
					{ "live_chat_url", LIVECHAT_URL },
					{ "help_url", HELP_URL },
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("DepartmentLinkCreated.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = DONOTREPLY_EMAIL;
				newEmail.To.Add(email);
				newEmail.From = DONOTREPLY_EMAIL;
				newEmail.Subject = $"Resgrid Department Link Created";

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}

		public async Task<bool> SendReportDeliveryMail(string email, string subject, string messageBody, string sentOn,
			string reportName, string attachmentFilename, byte[] attachmentData, string reportUrl)
		{
			var templateModel = new Dictionary<string, object>
			{
				{ "title", subject },
				{ "body", HtmlToTextHelper.ConvertHtml(messageBody) },
				{ "attachment_details", new []{
				new Dictionary<string,object> {
					{ "attachmnet_url",  reportUrl},
					{ "url_name", "View Live Report" },
					{ "attachment_name", attachmentFilename },
					{ "attachment_size", StringHelpers.GetSizeInMemory(attachmentData.LongLength) },
					{ "attachment_type", "PDF" },
				}
				}
				},

				{ "action_url", $"{SystemBehaviorConfig.ResgridBaseUrl}/User/Profile/Reporting" },
				{ "timestamp", sentOn }
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("ReportDelivery.html"));
				var content = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = content;
				newEmail.Sender = DONOTREPLY_EMAIL;
				newEmail.To.Add(email);
				newEmail.AttachmentName = attachmentFilename;
				newEmail.AttachmentData = attachmentData;
				newEmail.AttachmentContentType = "application/pdf";
				newEmail.From = DONOTREPLY_EMAIL;
				newEmail.Subject = subject;

				return await _emailSender.Send(newEmail);
			}
			catch (Exception)
			{
			}

			return false;
		}

		public async Task<bool> SendCommunicationTestMail(string email, CommunicationTestEmailContent content)
		{
			// The model is built before the try below, so without this a null content would throw past
			// the catch that turns every other failure here into a recorded false. A send that cannot be
			// composed is a failed send, same as one the provider rejects.
			if (content == null)
				return false;

			// Every string arrives already rendered in the recipient's language -- the template only
			// supplies the Resgrid chrome around them.
			var templateModel = new Dictionary<string, object>
			{
				{ "preheader", content.Preheader },
				{ "greeting", content.Greeting },
				{ "intro", content.Intro },
				{ "disclaimer", content.Disclaimer },
				{ "department_label", content.DepartmentLabel },
				{ "department_name", content.DepartmentName },
				{ "test_label", content.TestLabel },
				{ "test_name", content.TestName },
				{ "action", content.Action },
				{ "button_text", content.ButtonText },
				{ "confirm_url", content.ConfirmUrl },
				{ "trouble_text", content.TroubleText },
				{ "signoff", content.Signoff },
				{ "team_name", content.TeamName }
			};

			try
			{
				var template = Mustachio.Parser.Parse(GetTempate("CommunicationTest.html"));
				var body = template(templateModel);

				Email newEmail = new Email();
				newEmail.HtmlBody = body;
				newEmail.TextBody = content.TextBody;
				newEmail.Sender = DONOTREPLY_EMAIL;
				newEmail.To.Add(email);
				newEmail.From = DONOTREPLY_EMAIL;
				newEmail.Subject = content.Subject;

				return await _emailSender.Send(newEmail);
			}
			catch (Exception ex)
			{
				// A test that cannot reach someone is the answer the run is looking for, so this is
				// recorded as a failed send rather than thrown -- but it is still logged, because a
				// template or provider fault would otherwise read as "the member is unreachable".
				Logging.LogException(ex);
			}

			return false;
		}

		private string GetTempate(string templateName)
		{
			var assembly = typeof(PostmarkTemplateProvider).Assembly;
			using (var resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".Template." + templateName))
			{
				using (var reader = new StreamReader(resource))
				{
					return reader.ReadToEnd();
				}
			}
		}
	}
}
