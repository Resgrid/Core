using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Messages;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using System;
using System.Collections.Async;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Resgrid.Services
{
	public class CommunicationService : ICommunicationService
	{
		private readonly ISmsService _smsService;
		private readonly IEmailService _emailService;
		private readonly IPushService _pushService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IOutboundVoiceProvider _outboundVoiceProvider;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public CommunicationService(ISmsService smsService, IEmailService emailService, IPushService pushService, IGeoLocationProvider geoLocationProvider,
			IOutboundVoiceProvider outboundVoiceProvider, IUserProfileService userProfileService, IDepartmentSettingsService departmentSettingsService)
		{
			_smsService = smsService;
			_emailService = emailService;
			_pushService = pushService;
			_geoLocationProvider = geoLocationProvider;
			_outboundVoiceProvider = outboundVoiceProvider;
			_userProfileService = userProfileService;
			_departmentSettingsService = departmentSettingsService;
		}

		public void SendMessage(Message message, string sendersName, string departmentNumber, int departmentId, UserProfile profile = null)
		{
			if (profile == null && !String.IsNullOrWhiteSpace(message.ReceivingUserId))
				profile = _userProfileService.GetProfileByUserId(message.ReceivingUserId);

			if (profile == null || profile.SendMessageSms)
			{
				try
				{
					_smsService.SendMessage(message, departmentNumber, departmentId, profile);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			if (profile == null || profile.SendMessageEmail)
			{
				try
				{
					_emailService.SendMessage(message, sendersName, profile, message.ReceivingUser);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			if (profile == null || profile.SendMessagePush)
			{
				var spm = new StandardPushMessage();
				spm.MessageId = message.MessageId;

				if (message.SystemGenerated)
					spm.SubTitle = "Msg from System";
				else
					spm.SubTitle = string.Format("Msg from {0}", sendersName);

				spm.Title = "Msg:" + message.Subject.Truncate(200);

				try
				{
					if (!String.IsNullOrWhiteSpace(message.ReceivingUserId))
#pragma warning disable 4014
						Task.Run(async () => { await _pushService.PushMessage(spm, message.ReceivingUserId, profile); }).ConfigureAwait(false);
#pragma warning restore 4014
					else
#pragma warning disable 4014
						Task.Run(async () => { await _pushService.PushMessage(spm, String.Empty, profile); }).ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}
		}

		public void SendCall(Call call, CallDispatch dispatch, string departmentNumber, int departmentId, UserProfile profile = null, string address = null)
		{
			if (profile == null)
				profile = _userProfileService.GetProfileByUserId(dispatch.UserId);

			// Send a Push Notification
			if (profile == null || profile.SendPush)
			{
				var spc = new StandardPushCall();
				spc.CallId = call.CallId;
				spc.Title = string.Format("Call {0}", call.Name);
				spc.Priority = call.Priority;
				spc.ActiveCallCount = 1;

				if (call.CallPriority != null && !String.IsNullOrWhiteSpace(call.CallPriority.Color))
				{
					spc.Color = call.CallPriority.Color;
				}
				else
				{
					spc.Color = "#000000";
				}

				string subTitle = String.Empty;

				if (String.IsNullOrWhiteSpace(address) && !String.IsNullOrWhiteSpace(call.Address))
				{
					subTitle = call.Address;
				}
				else if (!String.IsNullOrWhiteSpace(address))
				{
					subTitle = address;
				}
				else if (!string.IsNullOrEmpty(call.GeoLocationData))
				{
					try
					{
						string[] points = call.GeoLocationData.Split(char.Parse(","));

						if (points != null && points.Length == 2)
						{
							subTitle = _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
						}
					}
					catch
					{ }
				}

				if (!string.IsNullOrEmpty(subTitle))
				{
					spc.SubTitle = subTitle.Truncate(200);
				}
				else
				{
					if (!string.IsNullOrEmpty(call.NatureOfCall))
						spc.SubTitle = call.NatureOfCall.Truncate(200);
				}

				if (!String.IsNullOrWhiteSpace(spc.SubTitle))
					spc.SubTitle = StringHelpers.StripHtmlTagsCharArray(spc.SubTitle);

				spc.Title = StringHelpers.StripHtmlTagsCharArray(spc.Title);

				try
				{
#pragma warning disable 4014
					Task.Run(async () => { await _pushService.PushCall(spc, dispatch.UserId, profile, call.CallPriority); }).ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			// Send an SMS Message
			if (profile == null || profile.SendSms)
			{
				try
				{
#pragma warning disable 4014
					Task.Run(() => { _smsService.SendCall(call, dispatch, departmentNumber, departmentId, profile); }).ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			// Send an Email
			if (profile == null || profile.SendEmail)
			{
				try
				{
#pragma warning disable 4014
					Task.Run(() => { _emailService.SendCall(call, dispatch, profile); }).ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			// Initiate a Telephone Call
			if (profile == null || profile.VoiceForCall)
			{
				try
				{
#pragma warning disable 4014
					if (!Config.SystemBehaviorConfig.DoNotBroadcast)
						Task.Run(() => { _outboundVoiceProvider.CommunicateCall(departmentNumber, profile, call); }).ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}
		}

		public void SendUnitCall(Call call, CallDispatchUnit dispatch, string departmentNumber, string address = null)
		{
			var spc = new StandardPushCall();
			spc.CallId = call.CallId;
			spc.Title = string.Format("Call {0}", call.Name);
			spc.Priority = call.Priority;
			spc.ActiveCallCount = 1;

			if (call.CallPriority != null && !String.IsNullOrWhiteSpace(call.CallPriority.Color))
			{
				spc.Color = call.CallPriority.Color;
			}
			else
			{
				spc.Color = "#000000";
			}

			string subTitle = String.Empty;

			if (String.IsNullOrWhiteSpace(address) && !String.IsNullOrWhiteSpace(call.Address))
			{
				subTitle = call.Address;
			}
			else if (!String.IsNullOrWhiteSpace(address))
			{
				subTitle = address;
			}
			else if (!string.IsNullOrEmpty(call.GeoLocationData))
			{
				try
				{
					string[] points = call.GeoLocationData.Split(char.Parse(","));

					if (points != null && points.Length == 2)
					{
						subTitle = _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
					}
				}
				catch
				{ }
			}

			if (!string.IsNullOrEmpty(subTitle))
			{
				spc.SubTitle = subTitle.Truncate(200);
			}
			else
			{
				if (!string.IsNullOrEmpty(call.NatureOfCall))
					spc.SubTitle = call.NatureOfCall.Truncate(200);
			}

			if (!String.IsNullOrWhiteSpace(spc.SubTitle))
				spc.SubTitle = StringHelpers.StripHtmlTagsCharArray(spc.SubTitle);

			spc.Title = StringHelpers.StripHtmlTagsCharArray(spc.Title);

			try
			{
#pragma warning disable 4014
				Task.Run(async () => { await _pushService.PushCallUnit(spc, dispatch.UnitId, call.CallPriority); }).ConfigureAwait(false);
#pragma warning restore 4014
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public void SendNotification(string userId, int departmentId, string message, string departmentNumber, string title = "Notification", UserProfile profile = null)
		{
			if (profile == null)
				profile = _userProfileService.GetProfileByUserId(userId, false);

			if (profile == null || profile.SendNotificationSms)
			{
				try
				{
#pragma warning disable 4014
					Task.Run(() => { _smsService.SendNotification(userId, departmentId, $"{title} {message}", departmentNumber, profile); })
						.ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			if (profile == null || profile.SendNotificationEmail)
			{
				try
				{
#pragma warning disable 4014
					Task.Run(() => { _emailService.SendNotification(userId, $"{title} {message}", profile); }).ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			if (profile == null || profile.SendNotificationPush)
			{
				var spm = new StandardPushMessage();
				spm.Title = "Notification";
				spm.SubTitle = $"{title} {message}";

				try
				{
#pragma warning disable 4014
					Task.Run(async () => { await _pushService.PushNotification(spm, userId, profile); }).ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}
		}

		public async Task<bool> SendChat(string chatId, string sendingUserId, string group, string message, UserProfile sendingUser, List<UserProfile> recipients)
		{
			var spm = new StandardPushMessage();


			if (recipients.Count == 1)
			{
				spm.Title = $"New Chat Message from {sendingUser.FullName.AsFirstNameLastName}";
				spm.SubTitle = $"Chat: {sendingUser.LastName}: " + message;
			}
			else
			{
				spm.Title = $"New Chat Message in Group {group} from {sendingUser.FullName.AsFirstNameLastName}";
				spm.SubTitle = $"Chat {group}: {sendingUser.LastName}: " + message;
			}

			try
			{
				if (recipients.Count == 1)
				{
					var sendingTo = recipients.FirstOrDefault();
					spm.Id = $"T{sendingTo}";

					if (sendingTo != null)
					{
						await _pushService.PushChat(spm, sendingTo.UserId, sendingTo);
					}
				}
				else
				{
					spm.Id = $"G{chatId}";
					await recipients.ParallelForEachAsync(async person =>
						{
							try
							{
								await _pushService.PushChat(spm, person.UserId, person);
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
							}
						});
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return true;
		}

		public void SendTroubleAlert(TroubleAlertEvent troubleAlertEvent, Unit unit, Call call, string departmentNumber, int departmentId, string callAddress, string unitAddress, List<UserProfile> recipients)
		{
			string personnelNames = "No Unit Roles (Accountability) Set";
			if (troubleAlertEvent.Roles != null && troubleAlertEvent.Roles.Count() > 0)
			{
				personnelNames = "";
				foreach (var role in troubleAlertEvent.Roles)
				{
					if (String.IsNullOrWhiteSpace(personnelNames))
						personnelNames = role.UserFullName;
					else
						personnelNames = personnelNames + $",{role.UserFullName}";
				}
			}

			foreach (var recipient in recipients)
			{
				// Send a Push Notification
				if (recipient.SendPush)
				{
					var spc = new StandardPushCall();

					if (call != null)
						spc.CallId = call.CallId;

					spc.Title = string.Format("TROUBLE ALERT for {0}", unit.Name);
					spc.Priority = (int)CallPriority.Emergency;
					spc.ActiveCallCount = 1;

					string subTitle = String.Empty;
					if (!String.IsNullOrWhiteSpace(unitAddress))
					{
						spc.Title = string.Format("TROUBLE ALERT for {0} at {1}", unit.Name, unitAddress);
					}

					spc.Title = StringHelpers.StripHtmlTagsCharArray(spc.Title);

					try
					{
#pragma warning disable 4014
						Task.Run(async () => { await _pushService.PushCall(spc, recipient.UserId, recipient); }).ConfigureAwait(false);
#pragma warning restore 4014
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}

				// Send an SMS Message
				if (recipient.SendSms)
				{
					try
					{
#pragma warning disable 4014
						Task.Run(() => { _smsService.SendTroubleAlert(unit, call, unitAddress, departmentNumber, departmentId, recipient); }).ConfigureAwait(false);
#pragma warning restore 4014
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}

				// Send an Email
				if (recipient.SendEmail)
				{
					try
					{
#pragma warning disable 4014
						Task.Run(() => { _emailService.SendTroubleAlert(troubleAlertEvent, unit, call, callAddress, unitAddress, personnelNames, recipient); }).ConfigureAwait(false);
#pragma warning restore 4014
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
			}
		}


		public void SendTextMessage(string userId, string title, string message, int departmentId, string departmentNumber, UserProfile profile = null)
		{
			_smsService.SendText(userId, title, message, departmentId, departmentNumber, profile);
		}
	}
}
