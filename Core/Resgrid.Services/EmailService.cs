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
using Resgrid.Model.Helpers;
using Resgrid.Model.Identity;
using System.Text;
using MimeKit;
using Resgrid.Model.Events;
using System.Linq;
using System.Threading.Tasks;

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

		public async Task<bool> SendWelcomeEmail(string departmentName, string name, string emailAddress, string userName, string password, int departmentId)
		{
			try
			{
				await _emailProvider.SendWelcomeMail(name, departmentName, userName, password, emailAddress, departmentId);
				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return false;
		}

		public async Task<bool> SendPasswordResetEmail(string emailAddress, string name, string userName, string password, string departmentName)
		{
			try
			{
				await _emailProvider.SendPasswordResetMail(name, password, userName, emailAddress, departmentName);
				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return false;
		}

		public async Task<bool> Notify(EmailNotification email)
		{
			using (var mail = new MailMessage())
			{
				mail.To.Add("team@resgrid.com");
				mail.Subject = email.Subject;
				mail.From = new MailAddress("do-not-reply@resgrid.com");

				mail.Body = string.Format("Name: {0}<br/> Email: {1}<br/> {2}", email.Name, email.From, email.Body);
				mail.IsBodyHtml = true;

				await _emailSender.SendEmail(mail);
				return true;
			}
		}

		public async Task<bool> SystemTest(string emailAddress, EmailNotification email)
		{
			using (var mail = new MailMessage())
			{
				mail.To.Add(emailAddress);
				mail.Subject = email.Subject;

				if (!String.IsNullOrWhiteSpace(email.From))
					mail.From = new MailAddress(email.From);
				else
					mail.From = new MailAddress("do-not-reply@resgrid.com");

				mail.Body = string.Format("--SYSTEM TEST-- <br/>Email Id: {0} <br/>From: {1}, ", email.Body, email.Name);
				mail.IsBodyHtml = true;

				await _smtpClient.SendMailAsync(mail);
				return true;
			}
		}

		public async Task<bool> SendSignupEmail(string departmentName, string name, string emailAddress)
		{
			await _emailProvider.SendSignupMail(name, departmentName, emailAddress);
			return true;
		}

		public async Task<bool> SendMessageAsync(Message message, string senderName, int departmentId, UserProfile profile = null, IdentityUser user = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(departmentId))
				return false;

			if (profile == null && !String.IsNullOrWhiteSpace(message.ReceivingUserId))
				profile = await _userProfileService.GetProfileByUserIdAsync(message.ReceivingUserId);

			if (user == null && profile != null && profile.User != null)
				user = profile.User;

			if (user == null && !String.IsNullOrWhiteSpace(message.ReceivingUserId))
				user = _usersService.GetUserById(message.ReceivingUserId, false);

			var subject = string.Format("Resgrid Message from {0}", senderName);

			var senderEmail = String.Empty;

			if (message.SendingUser != null)
				senderEmail = message.SendingUser.Email;
			else
				senderEmail = "do-not-reply@resgrid.com";

			if (profile != null && profile.SendMessageEmail)
				await _emailProvider.SendMessageMail(user.Email, subject, message.Subject, message.Body, senderEmail, senderName, message.SentOn.ToString("G") + " UTC", message.MessageId);

			return true;
		}

		public async Task<bool> SendCallAsync(Call call, CallDispatch dispatch, UserProfile profile = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(call.DepartmentId))
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(dispatch.UserId);

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
			string address = "No Address Supplied";

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
			if (!string.IsNullOrEmpty(call.GeoLocationData) && call.GeoLocationData.Length > 1)
				coordinates = call.GeoLocationData;

			if (!string.IsNullOrEmpty(call.Address))
				address = call.Address;
			else if (!string.IsNullOrEmpty(call.GeoLocationData) && call.GeoLocationData.Length > 1)
			{
				string[] points = call.GeoLocationData.Split(char.Parse(","));

				if (points != null && points.Length == 2)
				{
					try
					{
						address = await _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
					}
					catch (Exception)
					{
					}
				}
			}

			string dispatchedOn = String.Empty;

			if (call.Department != null)
				dispatchedOn = call.LoggedOn.TimeConverterToString(call.Department);
			else
				dispatchedOn = call.LoggedOn.ToString("G") + " UTC";

			if (call.Protocols != null && call.Protocols.Any())
			{
				string protocols = String.Empty;
				foreach (var protocol in call.Protocols)
				{
					if (String.IsNullOrWhiteSpace(protocols))
						protocols = protocol.Data;
					else
						protocols = protocol + "," + protocol.Data;
				}

				if (!String.IsNullOrWhiteSpace(protocols))
					call.NatureOfCall = call.NatureOfCall + " (" + protocols + ")";
			}

			if (profile != null && profile.SendEmail && !String.IsNullOrWhiteSpace(emailAddress))
				await _emailProvider.SendCallMail(emailAddress, subject, call.Name, priority, call.NatureOfCall, call.MapPage,
																	address, dispatchedOn, call.CallId, dispatch.UserId, coordinates, call.ShortenedAudioUrl);

			return true;
		}

		public async Task<bool> SendTroubleAlert(TroubleAlertEvent troubleAlertEvent, Unit unit, Call call, string callAddress, string unitAddress, string personnelNames, UserProfile profile)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(unit.DepartmentId))
				return false;

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
			Department d = await _departmentsService.GetDepartmentByIdAsync(unit.DepartmentId);

			if (d != null)
				dispatchedOn = troubleAlertEvent.TimeStamp.Value.FormatForDepartment(d);
			else
				dispatchedOn = troubleAlertEvent.TimeStamp.Value.ToString("G") + " UTC";

			string gpsLocation = "No Unit GPS Location";
			string callName = "No Active Call";

			if (call != null)
				callName = call.Name;

			if (!String.IsNullOrWhiteSpace(troubleAlertEvent.Latitude) && !String.IsNullOrWhiteSpace(troubleAlertEvent.Longitude))
				gpsLocation = $"{troubleAlertEvent.Latitude},{troubleAlertEvent.Longitude}";

			if (profile != null && profile.SendEmail && !String.IsNullOrWhiteSpace(emailAddress))
			{
				await _emailProvider.SendTroubleAlertMail(emailAddress, unit.Name, gpsLocation, "", callAddress,
					unitAddress, "", callName);

				return true;
			}

			return false;
		}

		public async Task<bool> SendInviteAsync(Invite invite, string senderName, string senderEmail)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(invite.DepartmentId))
				return false;

			if (invite == null)
				return false;

			if (invite.Department == null)
				invite.Department = await _departmentsService.GetDepartmentByIdAsync(invite.DepartmentId);

			await _emailProvider.SendInviteMail(invite.Code.ToString(), invite.Department.Name, invite.EmailAddress, senderName, senderEmail);

			return true;
		}

		public async Task<bool> SendReportDeliveryAsync(EmailNotification email, int departmentId, string reportUrl, string reportName)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(departmentId))
				return false;

			var body = string.Format("Your scheduled Resgrid report is attached. It will be as an email attachment. You can view the live report by clicking the link in the email and logging in.");

			return await _emailProvider.SendReportDeliveryMail(email.To, email.Subject, body, DateTime.UtcNow.ToString("G") + " UTC", reportName, email.AttachmentName, email.AttachmentData, reportUrl);
		}







































		public async Task<bool> SendDistributionListEmail(MimeMessage message, string emailAddress, string name, string listUsername, string listEmail)
		{
			// VERP https://www.limilabs.com/blog/verp-variable-envelope-return-path-net

			message.From.Clear();
			message.From.Add(new MailboxAddress(Encoding.ASCII, $"({listUsername}) List", listEmail));

			message.To.Clear();
			message.To.Add(new MailboxAddress(name, emailAddress));

			message.Headers.Add(new MimeKit.Header(HeaderId.ReturnPath, $"{listUsername}+{emailAddress.Replace("@", "=")}@{Config.InboundEmailConfig.ListsDomain}"));
			message.Headers.Add(new MimeKit.Header("Return-Path", $"{listUsername}+{emailAddress.Replace("@", "=")}@{Config.InboundEmailConfig.ListsDomain}"));

			await _amazonEmailSender.SendDistributionListEmail(message);
			return true;
		}

		public async Task<bool> SendPaymentReceiptEmailAsync(Payment payment, Department department)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(department.DepartmentId))
				return false;

			IdentityUser user;

			if (department.ManagingUser != null)
				user = department.ManagingUser;
			else
				user = _usersService.GetUserById(department.ManagingUserId, false);

			var userProfile = await _userProfileService.GetProfileByUserIdAsync(department.ManagingUserId);

			await _emailProvider.SendPaymentReciept(department.Name, userProfile.FullName.AsFirstNameLastName, payment.PurchaseOn.ToShortDateString() + " (UTC)", payment.Amount.ToString("C", Cultures.UnitedStates), user.Email,
					((PaymentMethods)payment.Method).ToString(), payment.TransactionId, payment.Plan.Name, string.Format("{0} to {1}", payment.EffectiveOn.ToShortDateString(),
					payment.EndingOn.ToShortDateString()), payment.EndingOn.ToShortDateString() + " " + payment.EndingOn.ToShortTimeString() + " (UTC)", payment.PaymentId);

			return true;
		}

		public async Task<bool> SendCancellationEmailAsync(Payment payment, Department department)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(department.DepartmentId))
				return false;

			if (department == null && payment != null && payment.Department != null)
				department = payment.Department;

			if (department == null)
				return false;

			var user = _usersService.GetUserById(department.ManagingUserId, false);
			var profile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);

			if (user == null)
				return false;

			string name = "";
			if (profile != null)
				name = profile.FullName.AsFirstNameLastName;

			await _emailProvider.SendCancellationReciept(name, user.Email, payment.EndingOn.ToShortDateString(), department.Name);

			return true;
		}

		public async Task<bool> SendChargeFailedEmailAsync(Payment payment, Department department, Resgrid.Model.Plan plan)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(department.DepartmentId))
				return false;

			if (payment != null && department != null)
			{
				var user = _usersService.GetUserById(department.ManagingUserId, false);
				var profile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);
				string planName = "";

				if (plan != null)
					planName = plan.Name;

				if (profile != null)
					await _emailProvider.SendChargeFailed(profile.FullName.AsFirstNameLastName, user.Email, payment.EndingOn.ToShortDateString(), department.Name, planName);
				else
					await _emailProvider.SendChargeFailed("", user.Email, payment.EndingOn.ToShortDateString(), department.Name, planName);
			}

			return true;
		}

		public async Task<bool> SendCancellationWithRefundEmailAsync(Payment payment, Charge charge, Department department)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(department.DepartmentId))
				return false;

			var user = _usersService.GetUserById(department.ManagingUserId, false);
			var profile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);

			await _emailProvider.SendRefundReciept(profile.FirstName + " " + profile.LastName, user.Email, department.Name, DateTime.UtcNow.ToShortDateString(), (float.Parse(charge.AmountRefunded.ToString()) / 100f).ToString("C", Cultures.UnitedStates),
					((PaymentMethods)payment.Method).ToString(), charge.Id, payment.PaymentId.ToString());

			return true;
		}

		public async Task<bool> SendUserCancellationNotificationToTeamAsync(Department department, Payment payment, string userId, string reason)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(department.DepartmentId))
				return false;

			var user = _usersService.GetUserById(department.ManagingUserId, false);
			var profile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);

			bool refundIssued = false;

			if (DateTime.UtcNow <= payment.PurchaseOn.AddDays(30))
				refundIssued = true;

			await _emailProvider.TEAM_SendNofifySubCancelled(profile.FirstName + " " + profile.LastName, user.Email,
					department.Name, department.DepartmentId.ToString(), reason, DateTime.UtcNow.ToShortDateString() + " " + DateTime.UtcNow.ToShortTimeString(),
					payment.Plan.Name, refundIssued.ToString());

			return true;
		}

		public async Task<bool> SendRefundIssuedNotificationToTeam(Payment payment, Charge charge, Department department)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(department.DepartmentId))
				return false;

			await _emailProvider.TEAM_SendNotifyRefundIssued(department.DepartmentId.ToString(), department.Name, DateTime.UtcNow.ToShortDateString() + " " + DateTime.UtcNow.ToShortTimeString(),
													(float.Parse(charge.AmountRefunded.ToString()) / 100f).ToString("C", Cultures.UnitedStates), ((PaymentMethods)payment.Method).ToString(), charge.Id, payment.PaymentId.ToString());

			return true;
		}

		public async Task<bool> SendUpgradePaymentReceiptEmail(Payment newPayment, Payment oldPayment, Department department)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(department.DepartmentId))
				return false;

			var user = _usersService.GetUserById(department.ManagingUserId, false);

			await  _emailProvider.SendUpgradePaymentReciept(department.Name, newPayment.PurchaseOn.ToShortDateString() + " (UTC)", newPayment.Amount.ToString("C", Cultures.UnitedStates), user.Email,
					((PaymentMethods)newPayment.Method).ToString(), newPayment.TransactionId, oldPayment.Plan.Name, newPayment.Plan.Name, string.Format("{0} to {1}", newPayment.EffectiveOn.ToShortDateString(),
					newPayment.EndingOn.ToShortDateString()), newPayment.EndingOn.ToShortDateString() + " " + newPayment.EndingOn.ToShortTimeString() + " (UTC)");

			return true;

		}

		//public string TestEmailSettings(DepartmentCallEmail emailSettings)
		//{
		//	return _callEmailProvider.TestEmailSettings(emailSettings);
		//}

		public async Task<bool> SendReportDeliveryEmail(EmailNotification email)
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
					await _emailSender.SendEmail(mail);
					return true;
				}
				catch (SmtpException sex) { }
			}

			return false;
		}

		public async Task<bool> SendNotificationAsync(string userId, string message, int departmentId, UserProfile profile = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(departmentId))
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId);

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

						await _emailSender.SendEmail(mail);
					}
				}
			}

			return true;
		}

		public async Task<bool> SendCalendarAsync(string userId, string message, int departmentId, UserProfile profile = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(departmentId))
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId);

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
						mail.Subject = "Calendar";
						mail.From = new MailAddress(Config.OutboundEmailServerConfig.FromMail, "Resgrid");

						mail.Body = message;
						mail.IsBodyHtml = false;

						await _emailSender.SendEmail(mail);
					}
				}
			}

			return true;
		}

		public async Task<bool> SendNewDepartmentLinkMailAsync(DepartmentLink link)
		{
			var sourceDepartment = await _departmentsService.GetDepartmentByIdAsync(link.DepartmentId);
			var targetDepartment = await _departmentsService.GetDepartmentByIdAsync(link.DepartmentLinkId);
			var managingProfile = await _userProfileService.GetProfileByUserIdAsync(targetDepartment.ManagingUserId);

			await _emailProvider.SendNewDepartmentLinkMail(managingProfile.FullName.AsFirstNameLastName, sourceDepartment.Name, "", managingProfile.User.Email, targetDepartment.DepartmentId);

			return false;
		}

		public async Task<bool> SendDeleteDepartmentEmail(string sendingToEmail, string sendingToName, QueueItem queueItem)
		{
			if (queueItem == null)
				return false;

			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(int.Parse(queueItem.SourceId)))
				return false;

			var department = await _departmentsService.GetDepartmentByIdAsync(int.Parse(queueItem.SourceId));
			var userProfile = await _userProfileService.GetProfileByUserIdAsync(queueItem.QueuedByUserId);

			if (queueItem.ToBeCompletedOn.HasValue)
				return await _emailProvider.SendDeleteDepartmentEmail(userProfile.FullName.AsFirstNameLastName, department.Name, queueItem.ToBeCompletedOn.Value.TimeConverter(department), sendingToName, sendingToEmail);

			return false;
		}
	}
}
