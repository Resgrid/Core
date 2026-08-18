using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Resgrid.Services
{
	public class PushService : IPushService
	{
		private readonly IPushLogsService _pushLogsService;
		private readonly INotificationProvider _notificationProvider;
		private readonly IUnitNotificationProvider _unitNotificationProvider;
		private readonly IUserProfileService _userProfileService;
		private readonly INovuProvider _novuProvider;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public PushService(IPushLogsService pushLogsService, INotificationProvider notificationProvider,
			IUserProfileService userProfileService, IUnitNotificationProvider unitNotificationProvider,
			INovuProvider novuProvider, IDepartmentSettingsService departmentSettingsService)
		{
			_pushLogsService = pushLogsService;
			_notificationProvider = notificationProvider;
			_userProfileService = userProfileService;
			_unitNotificationProvider = unitNotificationProvider;
			_novuProvider = novuProvider;
			_departmentSettingsService = departmentSettingsService;
		}

		public async Task<bool> Register(PushUri pushUri)
		{
			// A device that never lands its token on the Novu subscriber gets zero pushes forever, and every
			// exit below used to be a bare `false` nobody inspected. Log each one: a missing registration
			// is indistinguishable from a delivered-but-unseen push without it.
			if (pushUri == null || string.IsNullOrWhiteSpace(pushUri.DeviceId) || string.IsNullOrWhiteSpace(pushUri.PushLocation))
			{
				Framework.Logging.LogWarning($"PushService.Register: incomplete registration (userId {pushUri?.UserId}, platform {pushUri?.PlatformType}, hasToken {!string.IsNullOrWhiteSpace(pushUri?.DeviceId)}, prefix '{pushUri?.PushLocation}'), skipped.");
				return false;
			}

			var code = pushUri.PushLocation;
			// IC app registrations target the IC-specific Novu subscriber, keeping its inbox/push separate from the Responder app.
			var isICApp = string.Equals(pushUri.Source, "IC", StringComparison.OrdinalIgnoreCase);

			if (isICApp)
				await EnsureICUserSubscriber(pushUri, code);

			bool registered;

			// 1) iOS -> APNS
			if (pushUri.PlatformType == (int)Platforms.iOS)
			{
				registered = isICApp
					? await _novuProvider.UpdateICUserSubscriberApns(pushUri.UserId, code, pushUri.DeviceId)
					: await _novuProvider.UpdateUserSubscriberApns(pushUri.UserId, code, pushUri.DeviceId);
			}
			// 2) Android -> FCM
			else if (pushUri.PlatformType == (int)Platforms.Android)
			{
				registered = isICApp
					? await _novuProvider.UpdateICUserSubscriberFcm(pushUri.UserId, code, pushUri.DeviceId)
					: await _novuProvider.UpdateUserSubscriberFcm(pushUri.UserId, code, pushUri.DeviceId);
			}
			// 3) TODO: Web Push (other platforms)
			else
			{
				Framework.Logging.LogWarning($"PushService.Register: unsupported platform {pushUri.PlatformType} for user {pushUri.UserId} (prefix '{code}', IC {isICApp}), no push channel registered.");
				return false;
			}

			if (!registered)
				Framework.Logging.LogError($"PushService.Register: Novu rejected the credential write for user {pushUri.UserId} (platform {pushUri.PlatformType}, prefix '{code}', IC {isICApp}); subscriber will have no configured push channel.");

			return registered;
		}

		public async Task<bool> UnRegister(PushUri pushUri)
		{
			await _notificationProvider.UnRegisterPushByUserDeviceId(pushUri);

			return true;
		}

		private async Task EnsureICUserSubscriber(PushUri pushUri, string code)
		{
			try
			{
				var profile = await _userProfileService.GetProfileByUserIdAsync(pushUri.UserId);
				await _novuProvider.CreateICUserSubscriber(pushUri.UserId, code, pushUri.DepartmentId,
					profile?.MembershipEmail, profile?.FirstName, profile?.LastName);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}
		}

		public async Task<bool> RegisterUnit(PushUri pushUri)
		{
			if (pushUri == null || !pushUri.UnitId.HasValue || string.IsNullOrWhiteSpace(pushUri.DeviceId) || string.IsNullOrWhiteSpace(pushUri.PushLocation))
				return false;

			var unitId = pushUri.UnitId.Value;
			var code = pushUri.PushLocation;

			// 1) iOS -> APNS
			if (pushUri.PlatformType == (int)Platforms.iOS)
				return await _novuProvider.UpdateUnitSubscriberApns(unitId, code, pushUri.DeviceId);

			// 2) Android -> FCM
			if (pushUri.PlatformType == (int)Platforms.Android)
				return await _novuProvider.UpdateUnitSubscriberFcm(unitId, code, pushUri.DeviceId);

			// 3) TODO: Web Push (other platforms)
			return false;
		}

		public async Task<bool> UnRegisterUnit(PushUri pushUri)
		{
			await _unitNotificationProvider.UnRegisterPush(pushUri);

			return true;
		}

		public void UnRegisterNotificationOnly(PushUri pushUri)
		{
			_notificationProvider.UnRegisterPushByUserDeviceId(pushUri);
		}

		public async Task<bool> PushMessage(StandardPushMessage message, string userId, UserProfile profile = null)
		{
			if (message == null)
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			if (profile != null && profile.SendMessagePush)
			{
				string soundType = await GetSoundTypeAsync(message.DepartmentId, profile, PushSoundTypes.Message, PushSoundTypes.ModernMessage);

				try
				{
					await _notificationProvider.SendAllNotifications(message.Title, message.SubTitle, userId, string.Format("M{0}", message.MessageId), soundType, true, 1, "#000000");
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
				}

				try
				{
					if (!string.IsNullOrWhiteSpace(message.DepartmentCode))
						await _novuProvider.SendUserMessage(message.Title, message.SubTitle, userId, message.DepartmentCode, string.Format("M{0}", message.MessageId), soundType);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
				}
			}

			return true;
		}

		public async Task<bool> PushNotification(StandardPushMessage message, string userId, UserProfile profile = null)
		{
			if (message == null)
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			// Nothing is sent when the user has push off, so say so rather than reporting success —
			// a caller that reports delivery (the communication test) would otherwise claim a send
			// that never left this method.
			if (profile == null || !profile.SendNotificationPush)
				return false;

			string soundType = await GetSoundTypeAsync(message.DepartmentId, profile, PushSoundTypes.Notifiation, PushSoundTypes.ModernNotification);

			// An event code supplied on the message wins, so a caller can round-trip its own
			// identifier to the device (the communication test sends "CT:{responseToken}" and the
			// Responder app posts that token back to confirm receipt). MessageId stays the default
			// for ordinary notifications, preserving the existing "N{id}" codes.
			var eventCode = !string.IsNullOrWhiteSpace(message.Id)
				? message.Id
				: string.Format("N{0}", message.MessageId);

			bool delivered = false;

			try
			{
				await _notificationProvider.SendAllNotifications(message.Title, message.SubTitle, userId, eventCode, soundType, true, 1, "#000000");
				delivered = true;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			try
			{
				if (!string.IsNullOrWhiteSpace(message.DepartmentCode))
				{
					// Novu is the only transport that reports back whether it accepted the message;
					// the legacy provider returns void, so "didn't throw" is the best it can offer.
					delivered |= await _novuProvider.SendUserNotification(message.Title, message.SubTitle, userId, message.DepartmentCode, eventCode, soundType);
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			return delivered;
		}

		/// <summary>
		/// Pushes a notification to the user's IC app (distinct Novu subscriber) only — no legacy Azure path,
		/// so devices running just the Responder app aren't alerted for IC-only events.
		/// </summary>
		public async Task<bool> PushICNotification(StandardPushMessage message, string userId, UserProfile profile = null)
		{
			if (message == null)
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			if (profile != null && profile.SendNotificationPush)
			{
				string soundType = await GetSoundTypeAsync(message.DepartmentId, profile, PushSoundTypes.Notifiation, PushSoundTypes.ModernNotification);

				try
				{
					if (!string.IsNullOrWhiteSpace(message.DepartmentCode))
						await _novuProvider.SendICUserNotification(message.Title, message.SubTitle, userId, message.DepartmentCode, string.Format("N{0}", message.MessageId), soundType);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
				}
			}
			return true;
		}

		public async Task<bool> PushChat(StandardPushMessage message, string userId, UserProfile profile = null)
		{
			if (message == null)
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			if (profile != null && profile.SendMessagePush)
			{
				string soundType = await GetSoundTypeAsync(message.DepartmentId, profile, PushSoundTypes.Message, PushSoundTypes.ModernChat);
				await _notificationProvider.SendAllNotifications(message.Title, message.SubTitle, userId, message.Id, soundType, true, 1, "#000000");
			}

			return true;
		}

		public async Task<bool> PushChatMessage(StandardPushMessage message, string userId, string eventCode, int unreadCount,
			bool includeIncidentCommandApp, UserProfile profile = null)
		{
			if (message == null || string.IsNullOrWhiteSpace(userId))
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			// Both of these silently drop the push, and both are user/profile state rather than a fault —
			// log them so "chat pushes aren't arriving" can be told apart from "the user turned them off".
			if (profile == null)
			{
				Framework.Logging.LogWarning($"PushChatMessage: no user profile for {userId}, chat push dropped ({eventCode}).");
				return false;
			}

			if (!profile.SendMessagePush)
			{
				Framework.Logging.LogInfo($"PushChatMessage: SendMessagePush disabled for {userId}, chat push dropped ({eventCode}).");
				return false;
			}

			string soundType = await GetSoundTypeAsync(message.DepartmentId, profile, PushSoundTypes.Message, PushSoundTypes.ModernChat);

			if (string.IsNullOrWhiteSpace(message.DepartmentCode))
				Framework.Logging.LogWarning($"PushChatMessage: department {message.DepartmentId} has no Code, Novu chat push skipped for {userId} ({eventCode}).");

			try
			{
				await _notificationProvider.SendAllNotifications(message.Title, message.SubTitle, userId, eventCode, soundType, true, unreadCount, "#000000");
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			try
			{
				if (!string.IsNullOrWhiteSpace(message.DepartmentCode))
				{
					await _novuProvider.SendUserChatMessage(message.Title, message.SubTitle, userId, message.DepartmentCode, eventCode, soundType, unreadCount);

					// The IC app is an incident device: waking it for department, station, ad-hoc or peer
					// traffic both buries incident chatter and errors out for every user who never installed
					// it (that subscriber only exists after an IC-sourced registration). The caller gates it
					// on the channel, so this only fires for incident conversations.
					if (includeIncidentCommandApp)
						await _novuProvider.SendICUserChatMessage(message.Title, message.SubTitle, userId, message.DepartmentCode, eventCode, soundType, unreadCount);
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			return true;
		}

		public async Task<bool> PushChatMessageUnit(StandardPushMessage message, int unitId, string eventCode, int unreadCount)
		{
			if (message == null || unitId <= 0 || string.IsNullOrWhiteSpace(message.DepartmentCode))
				return false;

			try
			{
				await _novuProvider.SendUnitChatMessage(message.Title, message.SubTitle, unitId, message.DepartmentCode, eventCode, ((int)PushSoundTypes.ModernChat).ToString(), unreadCount);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			return true;
		}

		public async Task<bool> PushCall(StandardPushCall call, string userId, UserProfile profile = null, DepartmentCallPriority priority = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(call.DepartmentId.GetValueOrDefault()))
				return false;

			if (call == null)
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			if (profile != null && profile.SendPush)
			{
				string color = null;
				if (priority != null)
					color = priority.Color;

				string soundType = await GetCallSoundTypeAsync(call, priority, profile);

				// Legacy Push Notifications (Azure)
				try
				{
					await _notificationProvider.SendAllNotifications(call.SubTitle, call.Title, userId, string.Format("C{0}", call.CallId), soundType, true, call.ActiveCallCount, color);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
				}

				try
				{
					await _novuProvider.SendUserDispatch(call.Title, call.SubTitle, userId, call.DepartmentCode, string.Format("C{0}", call.CallId), soundType, true, call.ActiveCallCount, color);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
				}
			}

			return true;
		}

		public async Task<bool> PushCallUnit(StandardPushCall call, int unitId, DepartmentCallPriority priority = null)
		{
			if (Config.SystemBehaviorConfig.DoNotBroadcast && !Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(call.DepartmentId.GetValueOrDefault()))
				return false;

			if (call == null)
				return false;

			string color = null;
			if (priority != null)
				color = priority.Color;

			string soundType = await GetCallSoundTypeAsync(call, priority);

			try
			{
				await _novuProvider.SendUnitDispatch(call.Title, call.SubTitle, unitId, call.DepartmentCode, string.Format("C{0}", call.CallId), soundType, true, call.ActiveCallCount, color);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			return true;
		}

		private async Task<string> GetSoundTypeAsync(int? departmentId, UserProfile profile, PushSoundTypes legacyType, PushSoundTypes modernType)
		{
			bool useModern = await GetModernApplicationSoundsEnabledAsync(departmentId, profile);

			return ((int)(useModern ? modernType : legacyType)).ToString();
		}

		private async Task<string> GetCallSoundTypeAsync(StandardPushCall call, DepartmentCallPriority priority, UserProfile profile = null)
		{
			bool useModern = await GetModernApplicationSoundsEnabledAsync(call.DepartmentId, profile);
			return ConvertCallPriorityToSound((int)call.Priority, priority, useModern);
		}

		private async Task<bool> GetModernApplicationSoundsEnabledAsync(int? departmentId, UserProfile profile)
		{
			bool departmentEnabled = false;

			if (departmentId.HasValue)
			{
				try
				{
					departmentEnabled = await _departmentSettingsService.GetModernNotificationsEnabledAsync(departmentId.Value);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
				}
			}

			return ModernApplicationSoundSettings.IsEnabled(
				departmentEnabled,
				profile?.EnableModernApplicationSounds == true);
		}

		private string ConvertCallPriorityToSound(int priority, DepartmentCallPriority callPriority, bool useModern)
		{
			if (priority > 3 && callPriority != null)
			{
				if (callPriority.Tone > 0)
					return $"c{callPriority.Tone}";
				else
					return ((int)(useModern ? PushSoundTypes.ModernCallHigh : PushSoundTypes.CallHigh)).ToString();
			}

			switch (priority)
			{
				case (int)CallPriority.Low:
					return ((int)(useModern ? PushSoundTypes.ModernCallLow : PushSoundTypes.CallLow)).ToString();
				case (int)CallPriority.Medium:
					return ((int)(useModern ? PushSoundTypes.ModernCallMedium : PushSoundTypes.CallMedium)).ToString();
				case (int)CallPriority.High:
					return ((int)(useModern ? PushSoundTypes.ModernCallHigh : PushSoundTypes.CallHigh)).ToString();
				case (int)CallPriority.Emergency:
					return ((int)(useModern ? PushSoundTypes.ModernCallEmergency : PushSoundTypes.CallEmergency)).ToString();
				default:
					return ((int)(useModern ? PushSoundTypes.ModernCallHigh : PushSoundTypes.CallHigh)).ToString();
			}
		}

		private byte[] ReadResource(string fileName)
		{
			using (Stream resFilestream = Assembly.GetAssembly(this.GetType()).GetManifestResourceStream(fileName))
			{
				if (resFilestream == null) return null;
				byte[] ba = new byte[resFilestream.Length];
				resFilestream.Read(ba, 0, ba.Length);
				return ba;
			}
		}

		#region Private Events
		//private void Events_OnDeviceSubscriptionIdChanged(PushSharp.Common.PlatformType platform, string oldDeviceInfo, string newDeviceInfo)
		//{
		//	//Currently this event will only ever happen for Android GCM
		//	Console.WriteLine("Device Registration Changed:  Old-> " + oldDeviceInfo + "  New-> " + newDeviceInfo);
		//}

		//private void Events_OnNotificationSent(PushSharp.Common.Notification notification)
		//{

		//	Console.WriteLine("Sent: " + notification.Platform.ToString() + " -> " + notification.ToString());
		//}

		//private void Events_OnNotificationSendFailure(PushSharp.Common.Notification notification, Exception notificationFailureException)
		//{
		//	var exception = (PushSharp.WindowsPhone.WindowsPhoneNotificationSendFailureException) notificationFailureException;
		//	_pushLogsService.LogPushResult(exception.MessageStatus.DeviceConnectionStatus.ToString(),
		//																 exception.MessageStatus.HttpStatus.ToString(), exception.MessageStatus.MessageID.ToString(),
		//																 exception.MessageStatus.NotificationStatus.ToString(), exception.MessageStatus.SubscriptionStatus.ToString(),
		//																 exception.MessageStatus.Notification.EndPointUrl, exception);

		//	//Console.WriteLine("Failure: " + notification.Platform.ToString() + " -> " + notificationFailureException.Message + " -> " + notification.ToString());
		//}

		//private void Events_OnChannelException(Exception exception)
		//{
		//	Console.WriteLine("Channel Exception: " + exception.ToString());
		//}

		//private void Events_OnDeviceSubscriptionExpired(PushSharp.Common.PlatformType platform, string deviceInfo)
		//{
		//	Console.WriteLine("Device Subscription Expired: " + platform.ToString() + " -> " + deviceInfo);
		//}
		#endregion Private Events
	}
}
