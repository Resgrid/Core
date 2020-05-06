using System;
using System.Linq;
using System.Net.Mail;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Framework;

namespace Resgrid.Services
{
	public class SmsService : ISmsService
	{
		private readonly IUserProfileService _userProfileService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly ITextMessageProvider _textMessageProvider;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEmailSender _emailSender;

		public SmsService(IUserProfileService userProfileService, IGeoLocationProvider geoLocationProvider,
			ITextMessageProvider textMessageProvider, IDepartmentSettingsService departmentSettingsService,
			IEmailSender emailSender)
		{
			_userProfileService = userProfileService;
			_geoLocationProvider = geoLocationProvider;
			_textMessageProvider = textMessageProvider;
			_departmentSettingsService = departmentSettingsService;
			_emailSender = emailSender;
		}

		public void SendMessage(Message message, string departmentNumber, int departmentId, UserProfile profile = null)
		{
			if (profile == null && !String.IsNullOrWhiteSpace(message.ReceivingUserId))
				profile = _userProfileService.GetProfileByUserId(message.ReceivingUserId);

			MailMessage email = new MailMessage();

			if (profile != null && profile.SendMessageSms)
			{
				if (Config.SystemBehaviorConfig.DepartmentsToForceSmsGateway.Contains(departmentId))
				{
					string text = HtmlToTextHelper.ConvertHtml(message.Body);
					text = StringHelpers.StripHtmlTagsCharArray(text);
					_textMessageProvider.SendTextMessage(profile.GetPhoneNumber(), FormatTextForMessage(message.Subject, text),
						departmentNumber, (MobileCarriers)profile.MobileCarrier, departmentId, true);
				}
				else if (Carriers.DirectSendCarriers.Contains((MobileCarriers)profile.MobileCarrier))
				{
					string text = HtmlToTextHelper.ConvertHtml(message.Body);
					text = StringHelpers.StripHtmlTagsCharArray(text);
					_textMessageProvider.SendTextMessage(profile.GetPhoneNumber(), FormatTextForMessage(message.Subject, text),
						departmentNumber, (MobileCarriers)profile.MobileCarrier, departmentId);
				}
				else
				{
					email.To.Add(string.Format(Carriers.CarriersMap[(MobileCarriers)profile.MobileCarrier], profile.GetPhoneNumber()));

					email.From = new MailAddress(Config.OutboundEmailServerConfig.FromMail, "RGMsg");
					email.Subject = message.Subject;

					if (!string.IsNullOrEmpty(message.Body))
					{
						email.Body = HtmlToTextHelper.ConvertHtml(message.Body);
						email.Body = StringHelpers.StripHtmlTagsCharArray(email.Body);
					}
					email.IsBodyHtml = false;

					_emailSender.SendEmail(email);
				}
			}
		}

		public void SendCall(Call call, CallDispatch dispatch, string departmentNumber, int departmentId, UserProfile profile = null, string address = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast)
				return;

			if (profile == null)
				profile = _userProfileService.GetProfileByUserId(dispatch.UserId);

			if (profile != null && profile.SendSms)
			{
				if (String.IsNullOrWhiteSpace(address))
				{
					if (!String.IsNullOrWhiteSpace(call.Address))
					{
						address = call.Address;
					}
					else if (!string.IsNullOrEmpty(call.GeoLocationData))
					{
						try
						{
							string[] points = call.GeoLocationData.Split(char.Parse(","));

							if (points != null && points.Length == 2)
							{
								address = _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
							}
						}
						catch
						{
						}
					}
				}

				if (Config.SystemBehaviorConfig.DepartmentsToForceSmsGateway.Contains(departmentId))
				{
					string text = HtmlToTextHelper.ConvertHtml(call.NatureOfCall);
					text = StringHelpers.StripHtmlTagsCharArray(text);
					text = text + " " + address;

					if (call.Protocols != null && call.Protocols.Any())
					{
						string protocols = String.Empty;
						foreach (var protocol in call.Protocols)
						{
							if (!String.IsNullOrWhiteSpace(protocol.Data))
							{
								if (String.IsNullOrWhiteSpace(protocols))
									protocols = protocol.Data;
								else
									protocols = protocol + "," + protocol.Data;
							}
						}

						if (!String.IsNullOrWhiteSpace(protocols))
							text = text + " (" + protocols + ")";
					}

					if (!String.IsNullOrWhiteSpace(call.ShortenedAudioUrl))
					{
						text = text + " " + call.ShortenedAudioUrl;
					}
					//else if (!String.IsNullOrWhiteSpace(call.ShortenedCallUrl))
					//{
					//	text = text + " " + call.ShortenedCallUrl;
					//}

					_textMessageProvider.SendTextMessage(profile.GetPhoneNumber(), FormatTextForMessage(call.Name, text), departmentNumber, (MobileCarriers)profile.MobileCarrier, departmentId, true);

					if (Config.SystemBehaviorConfig.SendCallsToSmsEmailGatewayAdditionally)
						SendCallViaEmailSmsGateway(call, address, profile);
				}
				else if (Carriers.DirectSendCarriers.Contains((MobileCarriers)profile.MobileCarrier))
				{
					string text = HtmlToTextHelper.ConvertHtml(call.NatureOfCall);
					text = StringHelpers.StripHtmlTagsCharArray(text);
					text = text + " " + address;

					if (call.Protocols != null && call.Protocols.Any())
					{
						string protocols = String.Empty;
						foreach (var protocol in call.Protocols)
						{
							if (!String.IsNullOrWhiteSpace(protocol.Data))
							{
								if (String.IsNullOrWhiteSpace(protocols))
									protocols = protocol.Data;
								else
									protocols = protocol + "," + protocol.Data;
							}
						}

						if (!String.IsNullOrWhiteSpace(protocols))
							text = text + " (" + protocols + ")";
					}

					if (!String.IsNullOrWhiteSpace(call.ShortenedAudioUrl))
					{
						text = text + " " + call.ShortenedAudioUrl;
					}
					//else if (!String.IsNullOrWhiteSpace(call.ShortenedCallUrl))
					//{
					//	text = text + " " + call.ShortenedCallUrl;
					//}

					_textMessageProvider.SendTextMessage(profile.GetPhoneNumber(), FormatTextForMessage(call.Name, text), departmentNumber, (MobileCarriers)profile.MobileCarrier, departmentId);

					if (Config.SystemBehaviorConfig.SendCallsToSmsEmailGatewayAdditionally)
						SendCallViaEmailSmsGateway(call, address, profile);
				}
				else
				{
					SendCallViaEmailSmsGateway(call, address, profile);
				}
			}
		}

		private void SendCallViaEmailSmsGateway(Call call, string address, UserProfile profile)
		{
			MailMessage email = new MailMessage();
			email.To.Add(string.Format(Carriers.CarriersMap[(MobileCarriers)profile.MobileCarrier], profile.GetPhoneNumber()));

			email.From = new MailAddress(Config.OutboundEmailServerConfig.FromMail, "RGCall");
			email.Subject = call.Name;

			if (!string.IsNullOrEmpty(call.NatureOfCall))
			{
				email.Body = HtmlToTextHelper.ConvertHtml(call.NatureOfCall);
				email.Body = StringHelpers.StripHtmlTagsCharArray(email.Body);
			}

			email.Body = email.Body + " " + address;

			if (!String.IsNullOrWhiteSpace(call.ShortenedAudioUrl))
			{
				email.Body = email.Body + " " + call.ShortenedAudioUrl;
			}
			//else if (!String.IsNullOrWhiteSpace(call.ShortenedCallUrl))
			//{
			//	email.Body = email.Body + " " + call.ShortenedCallUrl;
			//}

			email.IsBodyHtml = false;

			_emailSender.SendEmail(email);
		}

		public void SendTroubleAlert(Unit unit, Call call, string unitAddress, string departmentNumber, int departmentId, UserProfile profile)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast)
				return;

			if (profile != null && profile.SendSms)
			{
				string text = $"TROUBLE ALERT for {unit.Name} at {unitAddress}";

				if (Config.SystemBehaviorConfig.DepartmentsToForceSmsGateway.Contains(departmentId))
				{
					_textMessageProvider.SendTextMessage(profile.GetPhoneNumber(), FormatTextForMessage(call.Name, text), departmentNumber, (MobileCarriers)profile.MobileCarrier, departmentId, true);
				}
				else if (Carriers.DirectSendCarriers.Contains((MobileCarriers)profile.MobileCarrier))
				{
					_textMessageProvider.SendTextMessage(profile.GetPhoneNumber(), FormatTextForMessage(call.Name, text), departmentNumber, (MobileCarriers)profile.MobileCarrier, departmentId);
				}
				else
				{
					MailMessage email = new MailMessage();
					email.To.Add(string.Format(Carriers.CarriersMap[(MobileCarriers)profile.MobileCarrier], profile.GetPhoneNumber()));

					email.From = new MailAddress(Config.OutboundEmailServerConfig.FromMail, "RGCall");
					email.Subject = text;

					email.Body = text;
					email.IsBodyHtml = false;

					_emailSender.SendEmail(email);
				}
			}
		}

		public void SendText(string userId, string title, string message, int departmentId, string departmentNumber, UserProfile profile = null)
		{
			// TODO: This method should only be working with Twilio for inbound text message support

			if (profile == null)
				profile = _userProfileService.GetProfileByUserId(userId);

			var email = new MailMessage();

			if (profile != null && profile.SendMessageSms)
			{
				if (Carriers.DirectSendCarriers.Contains((MobileCarriers)profile.MobileCarrier))
				{
					//string departmentNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(departmentId);
					_textMessageProvider.SendTextMessage(profile.GetPhoneNumber(), FormatTextForMessage(title, message), departmentNumber, (MobileCarriers)profile.MobileCarrier, departmentId);
				}
				else
				{
					email.To.Add(string.Format(Carriers.CarriersMap[(MobileCarriers)profile.MobileCarrier], profile.GetPhoneNumber()));

					email.From = new MailAddress(Config.OutboundEmailServerConfig.FromMail, "RGNot");

					if (!string.IsNullOrEmpty(title))
						email.Subject = title;

					if (!string.IsNullOrEmpty(message))
					{
						email.Body = HtmlToTextHelper.ConvertHtml(message);
						email.Body = StringHelpers.StripHtmlTagsCharArray(email.Body);
					}
					email.IsBodyHtml = false;

					_emailSender.SendEmail(email);
				}
			}
		}

		public void SendNotification(string userId, int departmentId, string message, string departmentNumber, UserProfile profile = null)
		{
			if (profile == null)
				profile = _userProfileService.GetProfileByUserId(userId);

			var email = new MailMessage();

			if (profile != null && profile.SendNotificationSms)
			{
				if (Config.SystemBehaviorConfig.DepartmentsToForceSmsGateway.Contains(departmentId))
				{
					_textMessageProvider.SendTextMessage(profile.GetPhoneNumber(), message,
						departmentNumber, (MobileCarriers)profile.MobileCarrier, departmentId, true);
				}
				else if (Carriers.DirectSendCarriers.Contains((MobileCarriers)profile.MobileCarrier))
				{
					//string departmentNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(departmentId);
					_textMessageProvider.SendTextMessage(profile.GetPhoneNumber(), message,
						departmentNumber, (MobileCarriers)profile.MobileCarrier, departmentId);
				}
				else
				{
					email.To.Add(string.Format(Carriers.CarriersMap[(MobileCarriers)profile.MobileCarrier], profile.GetPhoneNumber()));

					email.From = new MailAddress(Config.OutboundEmailServerConfig.FromMail, "Resgrid");
					email.Subject = "Notification";
					email.Body = HtmlToTextHelper.ConvertHtml(message);
					email.IsBodyHtml = false;

					_emailSender.SendEmail(email);
				}
			}
		}

		private string FormatCallSubject(Call call)
		{
			if (call.IsCritical)
				return string.Format("New CRITICAL Call: P{0} {1}", call.Priority, call.Name);

			return string.Format("New Call: P{0} {1}", call.Priority, call.Name);
		}

		private string FormatTextForMessage(string title, string body)
		{
			string text = HtmlToTextHelper.ConvertHtml(body);
			text = StringHelpers.StripHtmlTagsCharArray(text);

			return String.Format("{0} : {1}", title, text);
		}
	}
}
