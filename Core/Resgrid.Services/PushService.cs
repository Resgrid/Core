using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using DnsClient;

namespace Resgrid.Services
{
	public class PushService : IPushService
	{
		private readonly IPushLogsService _pushLogsService;
		private readonly INotificationProvider _notificationProvider;
		private readonly IUnitNotificationProvider _unitNotificationProvider;
		private readonly IUserProfileService _userProfileService;
		private readonly INovuProvider _novuProvider;

		public PushService(IPushLogsService pushLogsService, INotificationProvider notificationProvider,
			IUserProfileService userProfileService, IUnitNotificationProvider unitNotificationProvider,
			INovuProvider novuProvider)
		{
			_pushLogsService = pushLogsService;
			_notificationProvider = notificationProvider;
			_userProfileService = userProfileService;
			_unitNotificationProvider = unitNotificationProvider;
			_novuProvider = novuProvider;
		}

		public async Task<bool> Register(PushUri pushUri)
		{
			if (pushUri == null || String.IsNullOrWhiteSpace(pushUri.DeviceId))
				return false;

			string deviceId = pushUri.DeviceId.GetHashCode().ToString();

			// We just store the full Device Id in the PushUri object, the hashed version is for Azure
			//var existingPushUri = _pushUriService.GetPushUriByPlatformDeviceId((Platforms)pushUri.PlatformType, pushUri.DeviceId);
			List<PushRegistrationDescription> usersDevices = null;

			try
			{
				usersDevices = await _notificationProvider.GetRegistrationsByUserId(pushUri.UserId);

				if (usersDevices == null || !usersDevices.Any(x => x.Tags.Contains(deviceId)))
					await _notificationProvider.RegisterPush(pushUri);
			}
			catch (TimeoutException)
			{ }
			catch (TaskCanceledException)
			{ }

			//if (existingPushUri == null)
			//	pushUri = _pushUriService.SavePushUri(pushUri);

			//if (usersDevices == null || !usersDevices.Any(x => x.Tags.Contains(deviceId)))
			//	await _notificationProvider.RegisterPush(pushUri);

			return true;
		}

		public async Task<bool> UnRegister(PushUri pushUri)
		{
			await _notificationProvider.UnRegisterPushByUserDeviceId(pushUri);

			return true;
		}

		public async Task<bool> RegisterUnit(PushUri pushUri)
		{
			//string deviceId = pushUri.DeviceId;
			List<PushRegistrationDescription> usersDevices = null;

			//try
			//{
			//	usersDevices = await _unitNotificationProvider.GetRegistrationsByUUID(pushUri.PushLocation);
			//}
			//catch (TimeoutException)
			//{ }

			if (pushUri.UnitId.HasValue && !string.IsNullOrWhiteSpace(pushUri.PushLocation))
				await _novuProvider.UpdateUnitSubscriberFcm(pushUri.UnitId.Value, pushUri.PushLocation, pushUri.DeviceId);


			//if (usersDevices == null || !usersDevices.Any(x => x.Tags.Contains(string.Format("unitId:{0}", pushUri.UnitId.ToString()))))
			//	await _unitNotificationProvider.RegisterPush(pushUri);
			//else
			//{
			//	await _unitNotificationProvider.UnRegisterPushByUUID(pushUri.PushLocation);
			//	await _unitNotificationProvider.RegisterPush(pushUri);
			//}

			return true;
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
				await _notificationProvider.SendAllNotifications(message.Title, message.SubTitle, userId, string.Format("M{0}", message.MessageId), ((int)PushSoundTypes.Message).ToString(), true, 1, "#000000");

			return true;
		}

		public async Task<bool> PushNotification(StandardPushMessage message, string userId, UserProfile profile = null)
		{
			if (message == null)
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			if (profile != null && profile.SendNotificationPush)
				await _notificationProvider.SendAllNotifications(message.Title, message.SubTitle, userId, string.Format("N{0}", message.MessageId), ((int)PushSoundTypes.Notifiation).ToString(), true, 1, "#000000");

			return true;
		}

		public async Task<bool> PushChat(StandardPushMessage message, string userId, UserProfile profile = null)
		{
			if (message == null)
				return false;

			if (profile == null)
				profile = await _userProfileService.GetProfileByUserIdAsync(userId);

			if (profile != null && profile.SendMessagePush)
				await _notificationProvider.SendAllNotifications(message.Title, message.SubTitle, userId, message.Id, ((int)PushSoundTypes.Message).ToString(), true, 1, "#000000");

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

			string color = null;
			if (priority != null)
				color = priority.Color;

			if (profile != null && profile.SendPush)
				await _notificationProvider.SendAllNotifications(call.SubTitle, call.Title, userId, string.Format("C{0}", call.CallId), ConvertCallPriorityToSound((int)call.Priority, priority), true, call.ActiveCallCount, color);

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

			try
			{
				await _unitNotificationProvider.SendAllNotifications(call.SubTitle, call.Title, unitId, string.Format("C{0}", call.CallId), ConvertCallPriorityToSound((int)call.Priority, priority), true, call.ActiveCallCount, color);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			try
			{
				await _novuProvider.SendUnitDispatch(call.Title, call.SubTitle, unitId, call.DepartmentCode, string.Format("C{0}", call.CallId), ConvertCallPriorityToSound((int)call.Priority, priority), true, call.ActiveCallCount, color);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			return true;
		}

		private string ConvertCallPriorityToSound(int priority, DepartmentCallPriority callPriority)
		{
			if (priority > 3 && callPriority != null)
			{
				if (callPriority.Tone > 0)
					return $"c{callPriority.Tone}";
				else
					return ((int)PushSoundTypes.CallHigh).ToString();
			}

			switch (priority)
			{
				case (int)CallPriority.Low:
					return ((int)PushSoundTypes.CallLow).ToString();
				case (int)CallPriority.Medium:
					return ((int)PushSoundTypes.CallMedium).ToString();
				case (int)CallPriority.High:
					return ((int)PushSoundTypes.CallHigh).ToString();
				case (int)CallPriority.Emergency:
					return ((int)PushSoundTypes.CallEmergency).ToString();
				default:
					return ((int)PushSoundTypes.CallHigh).ToString();
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
