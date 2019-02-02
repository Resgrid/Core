using System;
using System.IO;
using System.Net.Mail;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.EmailProvider;
using Stripe;
using MailMessage = System.Net.Mail.MailMessage;
using Microsoft.AspNet.Identity.EntityFramework6;
using Resgrid.Model.Helpers;
using PostmarkDotNet;
using System.Reflection;
using System.Text;
using MimeKit;
using Resgrid.Model.Events;

namespace Resgrid.Services
{
	public class EmailService : IEmailService
	{
		#region Private Members and Constructors
		private readonly IUserProfileService _userProfileService;
		private readonly IUsersService _usersService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IEmailProvider _emailProvider;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICallEmailProvider _callEmailProvider;
		private readonly IEmailSender _emailSender;
		private readonly IAmazonEmailSender _amazonEmailSender;

		private SmtpClient _smtpClient;

		public EmailService(IUserProfileService userProfileService, IUsersService usersService, IGeoLocationProvider geoLocationProvider, IEmailProvider emailProvider, 
			IDepartmentsService departmentsService, ICallEmailProvider callEmailProvider, IEmailSender emailSender, IAmazonEmailSender amazonEmailSender)
		{
			_userProfileService = userProfileService;
			_usersService = usersService;
			_geoLocationProvider = geoLocationProvider;
			_emailProvider = emailProvider;
			_departmentsService = departmentsService;
			_callEmailProvider = callEmailProvider;
			_emailSender = emailSender;
			_amazonEmailSender = amazonEmailSender;

			_smtpClient = new SmtpClient
			{
				DeliveryMethod = SmtpDeliveryMethod.Network,
				Host = Config.OutboundEmailServerConfig.Host
			};
			_smtpClient.Credentials = new System.Net.NetworkCredential(Config.OutboundEmailServerConfig.UserName, Config.OutboundEmailServerConfig.Password);

			IEmailSender sender = new EmailSender
			{
				CreateClientFactory = () => new SmtpClientWrapper(_smtpClient)
			};

			_emailProvider.Configure(emailSender, "DO-NOT-REPLY@resgrid.com");
		}
		#endregion Private Members and Constructors

		public void SendWelcomeEmail(string departmentName, string name, string emailAddress, string userName, string password, int departmentId)
		{
			try
			{
				_emailProvider.SendWelcomeMail(name, departmentName, userName, password, emailAddress, departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public void SendPasswordResetEmail(string emailAddress, string name, string userName, string password, string departmentName)
		{
			try
			{
				_emailProvider.SendPasswordResetMail(name, password, userName, emailAddress, departmentName);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public void Notify(EmailNotification email)
		{
			using (var mail = new MailMessage())
			{
				mail.To.Add("team@resgrid.com");
				mail.Subject = email.Subject;

				if (!String.IsNullOrWhiteSpace(email.From))
					mail.From = new MailAddress(email.From);
				else
					mail.From = new MailAddress("do-not-reply@resgrid.com");

				mail.Body = string.Format("Name: {0}<br/> Email: {1}<br/> {2}", email.Name, email.From, email.Body);
				mail.IsBodyHtml = true;

				_emailSender.SendEmail(mail);
			}
		}

		public void SystemTest(string emailAddresss, EmailNotification email)
		{
			using (var mail = new MailMessage())
			{
				mail.To.Add(emailAddresss);
				mail.Subject = email.Subject;

				if (!String.IsNullOrWhiteSpace(email.From))
					mail.From = new MailAddress(email.From);
				else
					mail.From = new MailAddress("do-not-reply@resgrid.com");

				mail.Body = string.Format("--SYSTEM TEST-- <br/>Email Id: {0} <br/>From: {1}, ", email.Body, email.Name);
				mail.IsBodyHtml = true;

				_smtpClient.Send(mail);
			}
		}

		public void SendSignupEmail(string departmentName, string name, string emailAddress)
		{
			_emailProvider.SendSignupMail(name, departmentName, emailAddress);
		}

		public void SendAffiliateSignupEmail(string name, string emailAddress)
		{
			_emailProvider.SendAffiliateWelcomeMail(name, emailAddress);
		}

		public void SendMessage(Message message, string senderName, UserProfile profile = null, IdentityUser user = null)
		{
			if (profile == null && !String.IsNullOrWhiteSpace(message.ReceivingUserId))
				profile = _userProfileService.GetProfileByUserId(message.ReceivingUserId);

			if (user == null && profile != null && profile.User != null)
				user = profile.User;

			if (user == null && !String.IsNullOrWhiteSpace(message.ReceivingUserId))
				user = _usersService.GetUserById(message.ReceivingUserId);

			var subject = string.Format("Resgrid Message from {0}", senderName);

			var senderEmail = String.Empty;

			if (message.SendingUser != null)
				senderEmail = message.SendingUser.Email;
			else
				senderEmail = "do-not-reply@resgrid.com";

			if (profile != null && profile.SendMessageEmail)
				_emailProvider.SendMessageMail(user.Email, subject, message.Subject, message.Body, senderEmail, senderName, message.SentOn.ToString("G") + " UTC", message.MessageId);
		}

		public void SendCall(Call call, CallDispatch dispatch, UserProfile profile = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast)
				return;

			if (profile == null)
				profile = _userProfileService.GetProfileByUserId(dispatch.UserId);

			string emailAddress = String.Empty;

			if (dispatch.User != null && dispatch.User != null)
				emailAddress = dispatch.User.Email;
			else
			{
				//Logging.LogError(string.Format("Send Call Email (Missing User Membership): {0} User: {1}", call.CallId, dispatch.UserId));
				var user = _usersService.GetUserById(dispatch.UserId, false);

				if (user != null && user != null)
					emailAddress = user.Email;
			}

			string subject = string.Empty;
			string priority = string.Empty;
			string address = string.Empty;

			if (call.IsCritical)
			{
				subject = string.Format("Resgrid CRITICAL Call: P{0} {1}", call.Priority, call.Name);
				priority = string.Format("{0} CRITICAL", ((CallPriority)call.Priority).ToString());
			}
			else
			{
				subject = string.Format("Resgrid Call: P{0} {1}", call.Priority, call.Name);
				priority = string.Format("{0}", ((CallPriority)call.Priority).ToString());
			}

			string coordinates = "No Coordinates Supplied";
			if (!string.IsNullOrEmpty(call.GeoLocationData))
			{
				coordinates = call.GeoLocationData;

				string[] points = call.GeoLocationData.Split(char.Parse(","));

				if (points != null && points.Length == 2)
				{
					try
					{
						address = _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
					}
					catch (Exception)
					{
					}
				}
			}

			if (!string.IsNullOrEmpty(call.Address) && string.IsNullOrWhiteSpace(address))
				address = call.Address;
			else
				address = "No Address Supplied";

			string dispatchedOn = String.Empty;

			if (call.Department != null)
				dispatchedOn = call.LoggedOn.FormatForDepartment(call.Department);
			else
				dispatchedOn = call.LoggedOn.ToString("G") + " UTC";



			if (profile != null && profile.SendEmail && !String.IsNullOrWhiteSpace(emailAddress))
				_emailProvider.SendCallMail(emailAddress, subject, call.Name, priority, call.NatureOfCall, call.MapPage,
																	address, dispatchedOn, call.CallId, dispatch.UserId, coordinates, call.ShortenedAudioUrl);
		}

		public void SendTroubleAlert(TroubleAlertEvent troubleAlertEvent, Unit unit, Call call, string callAddress, string unitAddress, string personnelNames, UserProfile profile)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast)
				return;

			string emailAddress = string.Empty;

			if (profile.User != null && profile.User != null)
				emailAddress = profile.User.Email;
			else
			{
				//Logging.LogError(string.Format("Send Call Email (Missing User Membership): {0} User: {1}", call.CallId, dispatch.UserId));
				var user = _usersService.GetUserById(profile.UserId, false);

				if (user != null && user != null)
					emailAddress = user.Email;
			}

			string subject = $"TROUBLE ALERT for {unit.Name} located at {unitAddress}";

			string dispatchedOn = String.Empty;

			if (call.Department != null)
				dispatchedOn = troubleAlertEvent.TimeStamp.Value.FormatForDepartment(call.Department);
			else
				dispatchedOn = troubleAlertEvent.TimeStamp.Value.ToString("G") + " UTC";

			string gpsLocation = "No Unit GPS Location";

			if (!String.IsNullOrWhiteSpace(troubleAlertEvent.Latitude) && !String.IsNullOrWhiteSpace(troubleAlertEvent.Longitude))
				gpsLocation = $"{troubleAlertEvent.Latitude},{troubleAlertEvent.Longitude}";

			if (profile != null && profile.SendEmail && !String.IsNullOrWhiteSpace(emailAddress))
				_emailProvider.SendTroubleAlertMail(emailAddress, unit.Name, gpsLocation, "", callAddress, unitAddress, "", call.Name);
		}

		public void SendInvite(Invite invite, string senderName, string senderEmail)
		{
			if (invite == null)
				return;

			if (invite.Department == null)
				invite.Department = _departmentsService.GetDepartmentById(invite.DepartmentId);

			_emailProvider.SendInviteMail(invite.Code.ToString(), invite.Department.Name, invite.EmailAddress, senderName, senderEmail);
		}

		public void SendDistributionListEmail(MimeMessage message, string emailAddress, string name, string listUsername, string listEmail)
		{
			// VERP https://www.limilabs.com/blog/verp-variable-envelope-return-path-net

			message.From.Clear();
			message.From.Add(new MailboxAddress(Encoding.ASCII, $"({listUsername}) List", listEmail));

			message.To.Clear();
			message.To.Add(new MailboxAddress(emailAddress));

			message.Headers.Add(new MimeKit.Header(HeaderId.ReturnPath, $"{listUsername}+{emailAddress.Replace("@", "=")}@{Config.InboundEmailConfig.ListsDomain}"));
			message.Headers.Add(new MimeKit.Header("Return-Path", $"{listUsername}+{emailAddress.Replace("@", "=")}@{Config.InboundEmailConfig.ListsDomain}"));

			_amazonEmailSender.SendDistributionListEmail(message);
		}

		public void SendPaymentRecieptEmail(Payment payment, Department department)
		{
			IdentityUser user;

			if (department.ManagingUser != null)
				user = department.ManagingUser;
			else
				user = _usersService.GetUserById(department.ManagingUserId);

			var userProfile = _userProfileService.GetProfileByUserId(department.ManagingUserId);

			_emailProvider.SendPaymentReciept(department.Name, userProfile.FullName.AsFirstNameLastName, payment.PurchaseOn.ToShortDateString() + " (UTC)", payment.Amount.ToString("C"), user.Email,
					((PaymentMethods)payment.Method).ToString(), payment.TransactionId, payment.Plan.Name, string.Format("{0} to {1}", payment.EffectiveOn.ToShortDateString(),
					payment.EndingOn.ToShortDateString()), payment.EndingOn.ToShortDateString() + " " + payment.EndingOn.ToShortTimeString() + " (UTC)", payment.PaymentId);

		}

		public void SendCancellationEmail(Payment payment, Department department)
		{
			if (department == null && payment != null && payment.Department != null)
				department = payment.Department;

			if (department == null)
				return;

			var user = _usersService.GetUserById(department.ManagingUserId);
			var profile = _userProfileService.GetProfileByUserId(user.UserId);

			if (user == null)
				return;

			string name = "";
			if (profile != null)
				name = profile.FullName.AsFirstNameLastName;

			_emailProvider.SendCancellationReciept(name, user.Email, payment.EndingOn.ToShortDateString(), department.Name);
		}

		public void SendChargeFailedEmail(Payment payment, Department department, Plan plan)
		{
			var user = _usersService.GetUserById(department.ManagingUserId);
			var profile = _userProfileService.GetProfileByUserId(user.UserId);

			if (profile != null)
				_emailProvider.SendChargeFailed(profile.FullName.AsFirstNameLastName, user.Email, payment.EndingOn.ToShortDateString(), department.Name, plan.Name);
			else
				_emailProvider.SendChargeFailed("", user.Email, payment.EndingOn.ToShortDateString(), department.Name, plan.Name);
		}

		public void SendCancellationWithRefundEmail(Payment payment, StripeCharge charge, Department department)
		{
			var user = _usersService.GetUserById(department.ManagingUserId);
			var profile = _userProfileService.GetProfileByUserId(user.UserId);

			_emailProvider.SendRefundReciept(profile.FirstName + " " + profile.LastName, user.Email, department.Name, DateTime.UtcNow.ToShortDateString(), (float.Parse(charge.AmountRefunded.ToString()) / 100f).ToString("C"),
					((PaymentMethods)payment.Method).ToString(), charge.Id, payment.PaymentId.ToString());
		}

		public void SendUserCancellationNotificationToTeam(Department department, Payment payment, string userId, string reason)
		{
			var user = _usersService.GetUserById(department.ManagingUserId);
			var profile = _userProfileService.GetProfileByUserId(user.UserId);

			bool refundIssued = false;

			if (DateTime.UtcNow <= payment.PurchaseOn.AddDays(30))
				refundIssued = true;

			_emailProvider.TEAM_SendNofifySubCancelled(profile.FirstName + " " + profile.LastName, user.Email,
					department.Name, department.DepartmentId.ToString(), reason, DateTime.UtcNow.ToShortDateString() + " " + DateTime.UtcNow.ToShortTimeString(),
					payment.Plan.Name, refundIssued.ToString());
		}

		public void SendRefundIssuedNotificationToTeam(Payment payment, StripeCharge charge, Department department)
		{
			_emailProvider.TEAM_SendNotifyRefundIssued(department.DepartmentId.ToString(), department.Name, DateTime.UtcNow.ToShortDateString() + " " + DateTime.UtcNow.ToShortTimeString(),
													(float.Parse(charge.AmountRefunded.ToString()) / 100f).ToString("C"), ((PaymentMethods)payment.Method).ToString(), charge.Id, payment.PaymentId.ToString());
		}

		public void SendUpgradePaymentRecieptEmail(Payment newPayment, Payment oldPayment, Department department)
		{
			var user = _usersService.GetUserById(department.ManagingUserId);

			_emailProvider.SendUpgradePaymentReciept(department.Name, newPayment.PurchaseOn.ToShortDateString() + " (UTC)", newPayment.Amount.ToString("C"), user.Email,
					((PaymentMethods)newPayment.Method).ToString(), newPayment.TransactionId, oldPayment.Plan.Name, newPayment.Plan.Name, string.Format("{0} to {1}", newPayment.EffectiveOn.ToShortDateString(),
					newPayment.EndingOn.ToShortDateString()), newPayment.EndingOn.ToShortDateString() + " " + newPayment.EndingOn.ToShortTimeString() + " (UTC)");

		}

		public void SendAffiliateApprovalEmail(Affiliate affiliate)
		{
			_emailProvider.SendAffiliateRegister(affiliate.EmailAddress, affiliate.AffiliateCode);
		}

		public void SendAffiliateRejectionEmail(Affiliate affiliate)
		{
			_emailProvider.SendAffiliateRejection(affiliate.EmailAddress, affiliate.RejectReason);
		}

		//public string TestEmailSettings(DepartmentCallEmail emailSettings)
		//{
		//	return _callEmailProvider.TestEmailSettings(emailSettings);
		//}

		public void SendReportDeliveryEmail(EmailNotification email)
		{
			using (var mail = new MailMessage())
			{
				mail.To.Add(email.To);
				mail.Subject = email.Subject;
				mail.From = new MailAddress("DO-NOT-REPLY@resgrid.com", "Resgrid Report Delivery");

				mail.Body = string.Format("Your scheduled Resgrid report is attached.");
				mail.IsBodyHtml = false;

				var ms = new MemoryStream(email.AttachmentData);
				var a = new System.Net.Mail.Attachment(ms, email.AttachmentName);
				mail.Attachments.Add(a);

				//_smtpClient.Send(mail);

				try
				{
					_emailSender.SendEmail(mail);
				}
				catch (SmtpException sex) { }
			}
		}

		public void SendNotification(string userId, string message, UserProfile profile = null)
		{
			if (profile == null)
				profile = _userProfileService.GetProfileByUserId(userId);

			if (profile != null && profile.SendNotificationEmail)
			{
				string email = null;

				if (!String.IsNullOrWhiteSpace(profile.MembershipEmail))
					email = profile.MembershipEmail;
				else if (profile.User != null && profile.User != null)
					email = profile.User.Email;
				else
					email = _usersService.GetMembershipByUserId(userId).Email;

				if (email != null)
				{
					using (var mail = new MailMessage())
					{
						mail.To.Add(email);
						mail.Subject = "Notification";
						mail.From = new MailAddress(Config.OutboundEmailServerConfig.FromMail, "Resgrid");

						mail.Body = message;
						mail.IsBodyHtml = false;

						_emailSender.SendEmail(mail);
					}
				}
			}
		}

		public void SendNewDepartmentLinkMail(DepartmentLink link)
		{
			var sourceDepartment = _departmentsService.GetDepartmentById(link.DepartmentId);
			var targetDepartment = _departmentsService.GetDepartmentById(link.DepartmentLinkId);
			var managingProfile = _userProfileService.GetProfileByUserId(targetDepartment.ManagingUserId);

			_emailProvider.SendNewDepartmentLinkMail(managingProfile.FullName.AsFirstNameLastName, sourceDepartment.Name, "", managingProfile.User.Email, targetDepartment.DepartmentId);
		}
	}
}
