using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Messages;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

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
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IUserStateService _userStateService;

		public CommunicationService(ISmsService smsService, IEmailService emailService, IPushService pushService, IGeoLocationProvider geoLocationProvider,
			IOutboundVoiceProvider outboundVoiceProvider, IUserProfileService userProfileService, IDepartmentSettingsService departmentSettingsService,
			ISubscriptionsService subscriptionsService, IUserStateService userStateService)
		{
			_smsService = smsService;
			_emailService = emailService;
			_pushService = pushService;
			_geoLocationProvider = geoLocationProvider;
			_outboundVoiceProvider = outboundVoiceProvider;
			_userProfileService = userProfileService;
			_departmentSettingsService = departmentSettingsService;
			_subscriptionsService = subscriptionsService;
			_userStateService = userStateService;
		}

		public async Task<bool> SendMessageAsync(Message message, string sendersName, string departmentNumber, int departmentId, UserProfile profile = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(departmentId))
				return false;

			if (!await CanSendToUser(message.ReceivingUserId, departmentId))
				return false;

			if (profile == null && !String.IsNullOrWhiteSpace(message.ReceivingUserId))
				profile = await _userProfileService.GetProfileByUserIdAsync(message.ReceivingUserId);

			if (profile == null || profile.SendMessageSms)
			{
				try
				{
					var payment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync(departmentId);
					await _smsService.SendMessageAsync(message, departmentNumber, departmentId, profile, payment);
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
					await _emailService.SendMessageAsync(message, sendersName, departmentId, profile, message.ReceivingUser);
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


				if (!String.IsNullOrWhiteSpace(message.ReceivingUserId))
				{
					await _pushService.PushMessage(spm, message.ReceivingUserId, profile);
				}
				else
				{
					await _pushService.PushMessage(spm, String.Empty, profile);
				}

			}

			return true;
		}

		public async Task<bool> SendCallAsync(Call call, CallDispatch dispatch, string departmentNumber, int departmentId, UserProfile profile = null, string address = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(departmentId))
				return false;

			if (!await CanSendToUser(dispatch.UserId, departmentId))
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(dispatch.UserId);

			// Send a Push Notification
			if (profile == null || profile.SendPush)
			{
				try
				{
					var spc = new StandardPushCall();
					spc.CallId = call.CallId;
					spc.Title = string.Format("Call {0}", call.Name);
					spc.Priority = call.Priority;
					spc.ActiveCallCount = 1;
					spc.DepartmentId = departmentId;

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
					else if (!string.IsNullOrEmpty(call.GeoLocationData) && call.GeoLocationData.Length > 1)
					{
						try
						{
							string[] points = call.GeoLocationData.Split(char.Parse(","));

							if (points != null && points.Length == 2)
							{
								subTitle = await _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
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

					spc.SubTitle = Regex.Replace(spc.SubTitle, @"((http|https):\/\/[\w\-_]+(\.[\w\-_]+)+([\w\-\.,@?^=%&amp;:/~\+#]*[\w\-\@?^=%&amp;/~\+#])?)", "");

					spc.Title = StringHelpers.StripHtmlTagsCharArray(spc.Title);
					spc.Title = Regex.Replace(spc.Title, @"((http|https):\/\/[\w\-_]+(\.[\w\-_]+)+([\w\-\.,@?^=%&amp;:/~\+#]*[\w\-\@?^=%&amp;/~\+#])?)", "");

					spc.Title = spc.Title.Replace(char.Parse("/"), char.Parse(" "));
					spc.SubTitle = spc.SubTitle.Replace(char.Parse("/"), char.Parse(" "));

					await _pushService.PushCall(spc, dispatch.UserId, profile, call.CallPriority);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			// Send an SMS Message
			if (profile == null || profile.SendSms)
			{
				var payment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync(departmentId);
				await _smsService.SendCallAsync(call, dispatch, departmentNumber, departmentId, profile, call.Address, payment);
			}

			// Send an Email
			if (profile == null || profile.SendEmail)
			{
				await _emailService.SendCallAsync(call, dispatch, profile);
			}

			// Initiate a Telephone Call
			if (profile == null || profile.VoiceForCall)
			{
				try
				{

					if (!Config.SystemBehaviorConfig.DoNotBroadcast || Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(departmentId))
						_outboundVoiceProvider.CommunicateCall(departmentNumber, profile, call);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return true;
		}

		public async Task<bool> SendUnitCallAsync(Call call, CallDispatchUnit dispatch, string departmentNumber, string address = null)
		{
			var spc = new StandardPushCall();
			spc.CallId = call.CallId;
			spc.Title = string.Format("Call {0}", call.Name);
			spc.Priority = call.Priority;
			spc.ActiveCallCount = 1;
			spc.DepartmentId = call.DepartmentId;
			spc.DepartmentCode = call.Department?.Code;

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
			else if (!string.IsNullOrEmpty(call.GeoLocationData) && call.GeoLocationData.Length > 1)
			{
				try
				{
					string[] points = call.GeoLocationData.Split(char.Parse(","));

					if (points != null && points.Length == 2)
					{
						subTitle = await _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
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
				await _pushService.PushCallUnit(spc, dispatch.UnitId, call.CallPriority);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return true;
		}

		public async Task<bool> SendNotificationAsync(string userId, int departmentId, string message, string departmentNumber, string title = "Notification", UserProfile profile = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(departmentId))
				return false;

			if (!await CanSendToUser(userId, departmentId))
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId, false);

			if (profile == null || profile.SendNotificationEmail)
			{
				try
				{
					await _emailService.SendNotificationAsync(userId, $"{title} {message}", departmentId, profile);

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
					await _pushService.PushNotification(spm, userId, profile);

				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return true;
		}

		public async Task<bool> SendCalendarAsync(string userId, int departmentId, string message, string departmentNumber, string title = "Notification", UserProfile profile = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(departmentId))
				return false;

			if (!await CanSendToUser(userId, departmentId))
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId, false);

			if (profile == null || profile.SendNotificationEmail)
			{
				try
				{
					await _emailService.SendCalendarAsync(userId, $"{title} {message}", departmentId, profile);

				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			if (profile == null || profile.SendNotificationPush)
			{
				var spm = new StandardPushMessage();
				spm.Title = "Calendar";
				spm.SubTitle = $"{title} {message}";

				try
				{
					await _pushService.PushNotification(spm, userId, profile);

				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return true;
		}

		public async Task<bool> SendChat(string chatId, int departmentId, string sendingUserId, string group, string message, UserProfile sendingUser, List<UserProfile> recipients)
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


					if (!await CanSendToUser(sendingTo.UserId, departmentId))
						return false;

					if (sendingTo != null)
					{
						await _pushService.PushChat(spm, sendingTo.UserId, sendingTo);
					}
				}
				else
				{
					spm.Id = $"G{chatId}";
					//await recipients.ParallelForEachAsync(async person =>
					foreach (var person in recipients)
					{
						try
						{
							if (await CanSendToUser(person.UserId, departmentId))
							{
								await _pushService.PushChat(spm, person.UserId, person);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return true;
		}

		public async Task<bool> SendTroubleAlertAsync(TroubleAlertEvent troubleAlertEvent, Unit unit, Call call, string departmentNumber, int departmentId, string callAddress, string unitAddress, List<UserProfile> recipients)
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
					spc.DepartmentId = departmentId;

					string subTitle = String.Empty;
					if (!String.IsNullOrWhiteSpace(unitAddress))
					{
						spc.Title = string.Format("TROUBLE ALERT for {0} at {1}", unit.Name, unitAddress);
					}

					spc.Title = StringHelpers.StripHtmlTagsCharArray(spc.Title);

					try
					{
						await _pushService.PushCall(spc, recipient.UserId, recipient);
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
						_smsService.SendTroubleAlert(unit, call, unitAddress, departmentNumber, departmentId, recipient);
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
						await _emailService.SendTroubleAlert(troubleAlertEvent, unit, call, callAddress, unitAddress, personnelNames, recipient);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
			}

			return true;
		}


		public async Task<bool> SendTextMessageAsync(string userId, string title, string message, int departmentId, string departmentNumber, UserProfile profile = null)
		{
			return await _smsService.SendTextAsync(userId, title, message, departmentId, departmentNumber, profile);
		}

		private async Task<bool> CanSendToUser(string userId, int departmentId)
		{
			var supressStaffingInfo = await _departmentSettingsService.GetDepartmentStaffingSuppressInfoAsync(departmentId);
			var lastUserStaffing = await _userStateService.GetLastUserStateByUserIdAsync(userId);

			// Looks like this was a bug, lastUserStaffing was going through here == null. -SJ
			if (lastUserStaffing != null && supressStaffingInfo != null)
			{
				if (supressStaffingInfo.EnableSupressStaffing)
				{
					if (supressStaffingInfo.StaffingLevelsToSupress.Contains(lastUserStaffing.State))
						return false;
				}
			}

			return true;
		}
	}
}
