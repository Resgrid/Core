using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Microsoft.Azure.NotificationHubs;
using Newtonsoft.Json.Linq;
using Resgrid.Providers.Bus.Models;
using Newtonsoft.Json;
using static Resgrid.Framework.Testing.TestData;

namespace Resgrid.Providers.Bus
{
	public class UnitNotificationProvider : IUnitNotificationProvider
	{
		public async Task RegisterPush(PushUri pushUri)
		{
			if (String.IsNullOrWhiteSpace(pushUri.DeviceId))
				return;

			if (pushUri.UnitId.HasValue)
			{
				var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureUnitNotificationHub_PushUrl);

				var tagsWithHashedDeviceId = new List<string>(new string[] {
														string.Format("unitId:{0}", pushUri.UnitId),
														string.Format("platform:{0}", pushUri.PlatformType),
														string.Format("uuid:{0}", pushUri.PushLocation),
														string.Format("deviceId:{0}", pushUri.DeviceId.GetHashCode())
				});

				if (pushUri.DepartmentId > 0)
				{
					tagsWithHashedDeviceId.Add(string.Format("departmentId:{0}", pushUri.DepartmentId));
				}

				ICollectionQueryResult<RegistrationDescription> registrations = null;
				try
				{
					registrations = await hubClient.GetRegistrationsByTagAsync(string.Format("deviceId:{0}", pushUri.DeviceId.GetHashCode()), 50);
				}
				catch
				{
					// So this just fails, like whenever it wants. I would rather people get 2 or 3 push messages for a fire then none. 
				}

				// Loop through all Azure registrations for this Hashed DeviceId and remove them
				if (registrations != null && registrations.Any())
				{
					foreach (var registration in registrations)
					{
						try
						{
							await hubClient.DeleteRegistrationAsync(registration);
						}
						catch (Microsoft.Azure.NotificationHubs.Messaging.MessagingEntityNotFoundException menfe)
						{
							// Eat me
						}
						catch (Exception ex)
						{
							Framework.Logging.LogException(ex);
						}
					}
				}

				if (pushUri.PlatformType == (int)Platforms.UnitWin)
				{
					try
					{
						var result = await hubClient.CreateMpnsNativeRegistrationAsync(pushUri.PushLocation, tagsWithHashedDeviceId.ToArray());
					}
					catch (ArgumentException ex)
					{
						Framework.Logging.LogException(ex,
						string.Format("Device Information: {0} {1} {2} {3}", tagsWithHashedDeviceId[0], tagsWithHashedDeviceId[1],
							tagsWithHashedDeviceId[2], tagsWithHashedDeviceId[3]));
					}
				}
				else if (pushUri.PlatformType == (int)Platforms.UnitAndroid)
				{
					try
					{
						//var result = await hubClient.CreateFcmNativeRegistrationAsync(pushUri.DeviceId, tagsWithHashedDeviceId.ToArray());
						var result = await hubClient.CreateFcmV1NativeRegistrationAsync(pushUri.DeviceId, tagsWithHashedDeviceId.ToArray());
					}
					catch (ArgumentException ex)
					{
						Framework.Logging.LogException(ex,
						string.Format("Device Information: {0} {1} {2} {3}", tagsWithHashedDeviceId[0], tagsWithHashedDeviceId[1],
							tagsWithHashedDeviceId[2], tagsWithHashedDeviceId[3]));
					}
				}
				else if (pushUri.PlatformType == (int)Platforms.UnitIOS)
				{
					try
					{
						var result = await hubClient.CreateAppleNativeRegistrationAsync(pushUri.DeviceId, tagsWithHashedDeviceId.ToArray());
					}
					catch (ArgumentException ex)
					{
						Framework.Logging.LogException(ex,
						string.Format("Device Information: {0} {1} {2} {3}", tagsWithHashedDeviceId[0], tagsWithHashedDeviceId[1],
							tagsWithHashedDeviceId[2], tagsWithHashedDeviceId[3]));
					}
				}
			}
		}

		public async Task UnRegisterPush(PushUri pushUri)
		{
			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureUnitNotificationHub_PushUrl);

			var registrations = await hubClient.GetRegistrationsByTagAsync(string.Format("deviceId:{0}", pushUri.DeviceId), 50);

			foreach (var registration in registrations)
			{
				if (pushUri.PlatformType == (int)Platforms.Android)
				{
					var androidReg = registration as GcmRegistrationDescription;
					if (androidReg != null && androidReg.GcmRegistrationId == pushUri.DeviceId)
						await hubClient.DeleteRegistrationAsync(registration);
				}
				else if (pushUri.PlatformType == (int)Platforms.iPad || pushUri.PlatformType == (int)Platforms.iPhone)
				{
					var iosReg = registration as AppleRegistrationDescription;
					if (iosReg != null && iosReg.DeviceToken == pushUri.DeviceId)
						await hubClient.DeleteRegistrationAsync(registration);
				}
			}
		}

		public async Task UnRegisterPushByUserDeviceId(PushUri pushUri)
		{
			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureUnitNotificationHub_PushUrl);

			var registrations = await hubClient.GetRegistrationsByTagAsync(string.Format("userId:{0}", pushUri.UserId), 50);

			// Step 1: Remove the devices that have a hashed deviceId
			try
			{
				List<Task> tasksWithHashedDeviceId = registrations.Where(x => x.Tags.Contains(pushUri.DeviceId.GetHashCode().ToString())).Select(registration => hubClient.DeleteRegistrationAsync(registration)).ToList();
				await Task.WhenAll(tasksWithHashedDeviceId.ToArray());
			}
			catch { }

			// Step 2: Remove the devices that have a non-hashsed device id (the old style)
			try
			{
				List<Task> tasksWithNormalDeviceId = registrations.Where(x => x.Tags.Contains(pushUri.DeviceId)).Select(registration => hubClient.DeleteRegistrationAsync(registration)).ToList();
				await Task.WhenAll(tasksWithNormalDeviceId.ToArray());
			}
			catch { }

			// Step 3: Remove the devices that have that PushUriId
			try
			{
				List<Task> tasksWithHashedDeviceId = registrations.Where(x => x.Tags.Contains(string.Format("pushUriId:{0}", pushUri.PushUriId))).Select(registration => hubClient.DeleteRegistrationAsync(registration)).ToList();
				await Task.WhenAll(tasksWithHashedDeviceId.ToArray());
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, string.Format("PushUriId: {0}", pushUri.PushUriId));
			}
		}

		public async Task UnRegisterPushByUUID(string uuid)
		{
			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureUnitNotificationHub_PushUrl);

			var registrations = await hubClient.GetRegistrationsByTagAsync(string.Format("uuid:{0}", uuid), 50);

			// Step 1: Remove the devices that have a hashed deviceId
			try
			{
				List<Task> tasksWithHashedDeviceId = registrations.Select(registration => hubClient.DeleteRegistrationAsync(registration)).ToList();
				await Task.WhenAll(tasksWithHashedDeviceId.ToArray());
			}
			catch { }
		}

		public async Task<List<PushRegistrationDescription>> GetRegistrationsByDeviceId(string deviceId)
		{
			List<PushRegistrationDescription> registrations = new List<PushRegistrationDescription>();

			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureUnitNotificationHub_PushUrl);

			try
			{
				// Step 1: Get the push registrations by the deviceId (passed via the Cordova UniqueDeviceId plugin
				var registraions = await hubClient.GetRegistrationsByTagAsync(string.Format("deviceId:{0}", deviceId), 50);

				if (registraions != null && registraions.Any())
				{
					foreach (var registraion in registraions)
					{
						var pushReg = new PushRegistrationDescription();
						pushReg.ETag = registraion.ETag;
						pushReg.ExpirationTime = registraion.ExpirationTime;
						pushReg.RegistrationId = registraion.RegistrationId;
						pushReg.Tags = registraion.Tags;

						registrations.Add(pushReg);
					}
				}

				// Step 2: Get the push registrations by the hashed version of the deviceId (passed via the Cordova UniqueDeviceId plugin (We hash is now because some deviceId's are longer/contain bar chars.
				var registraions2 = await hubClient.GetRegistrationsByTagAsync(string.Format("deviceId:{0}", deviceId.GetHashCode()), 50);

				if (registraions2 != null && registraions2.Any())
				{
					foreach (var registraion in registraions2)
					{
						var pushReg = new PushRegistrationDescription();
						pushReg.ETag = registraion.ETag;
						pushReg.ExpirationTime = registraion.ExpirationTime;
						pushReg.RegistrationId = registraion.RegistrationId;
						pushReg.Tags = registraion.Tags;

						registrations.Add(pushReg);
					}
				}
			}
			catch
			{ /* We can get some timeout errors, aggregate errors, etc from here. For right now, lets just return an empty list on any error. -SJ (12-13-2015) */ }

			return registrations;
		}

		public async Task<List<PushRegistrationDescription>> GetRegistrationsByUnitId(int unitId)
		{
			var registrations = new List<PushRegistrationDescription>();

			try
			{
				var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureUnitNotificationHub_PushUrl);

				var registraions = await hubClient.GetRegistrationsByTagAsync(string.Format("unitId:{0}", unitId), 50);

				if (registraions != null && registraions.Any())
					foreach (var registraion in registraions)
					{
						var pushReg = new PushRegistrationDescription();
						pushReg.ETag = registraion.ETag;
						pushReg.ExpirationTime = registraion.ExpirationTime;
						pushReg.RegistrationId = registraion.RegistrationId;
						pushReg.Tags = registraion.Tags;

						registrations.Add(pushReg);
					}
			}
			catch
			{ /* We can get some timeout errors, aggregate errors, etc from here. For right now, lets just return an empty list on any error. -SJ (12-13-2015) */}

			return registrations;
		}

		public async Task<List<PushRegistrationDescription>> GetRegistrationsByUUID(string uuid)
		{
			var registrations = new List<PushRegistrationDescription>();

			try
			{
				var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureUnitNotificationHub_PushUrl);

				var registraions = await hubClient.GetRegistrationsByTagAsync(string.Format("uuid:{0}", uuid), 50);

				if (registraions != null && registraions.Any())
					foreach (var registraion in registraions)
					{
						var pushReg = new PushRegistrationDescription();
						pushReg.ETag = registraion.ETag;
						pushReg.ExpirationTime = registraion.ExpirationTime;
						pushReg.RegistrationId = registraion.RegistrationId;
						pushReg.Tags = registraion.Tags;

						registrations.Add(pushReg);
					}
			}
			catch (Exception ex)
			{
				string test = ex.ToString();
				/* We can get some timeout errors, aggregate errors, etc from here. For right now, lets just return an empty list on any error. -SJ (12-13-2015) */
			}

			return registrations;
		}

		public async Task SendAllNotifications(string title, string subTitle, int unitId, string eventCode, string type, bool enableCustomSounds, int count, string color)
		{
			try
			{
				await SendAndroidNotification(title, subTitle, unitId, eventCode, type, enableCustomSounds, count, color);
				await SendAppleNotification(title, subTitle, unitId, eventCode, type, enableCustomSounds, count, color);
				//SendWindowsNotification(title, subTitle, userId).Wait();
				//SendWindowsPhoneNotification(title, subTitle, userId, enableCustomSounds);
			}
			catch (Exception ex)
			{
				string exception = ex.ToString();
			}
		}

		public async Task<NotificationOutcomeState> SendAndroidNotification(string title, string subTitle, int unitId, string eventCode, string type, bool enableCustomSounds, int count, string color)
		{
			try
			{
				var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureUnitNotificationHub_PushUrl);
				string androidNotification = CreateAndroidNotification(subTitle, title, eventCode, type, count, color, "calls");

				//var androidOutcome = await hubClient.SendFcmNativeNotificationAsync(androidNotification, string.Format("unitId:{0}", unitId));
				var androidOutcome = await hubClient.SendFcmV1NativeNotificationAsync(androidNotification, string.Format("unitId:{0}", unitId));

				return androidOutcome.State;
			}
			catch (Exception ex)
			{

				string exception = ex.ToString();
			}

			return NotificationOutcomeState.Unknown;
		}

		public async Task<NotificationOutcomeState> SendAppleNotification(string title, string subTitle, int unitId, string eventCode, string type, bool enableCustomSounds, int count, string color)
		{
			try
			{
				var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureUnitNotificationHub_PushUrl);
				string appleNotification = null;
				string category = null;

				if (eventCode.ToLower().StartsWith("m")) // message
					category = "messages";
				else if (eventCode.ToLower().StartsWith("c")) //call
					category = "calls";
				else if (eventCode.ToLower().StartsWith("n")) // notification
					category = "notifications";
				else if (eventCode.ToLower().StartsWith("t")) // 1 on 1 chat
					category = "chats";
				else if (eventCode.ToLower().StartsWith("g")) // group chat
					category = "chats";
				else
					category = "notifications";

				var apnsPayload = new ApnsPayload
				{
					aps = new ApnsHeader
					{
						alert = new ApnsAlert
						{
							title = title,
							body = subTitle
						},
						badge = count,
						category = category,
						sound = new ApnsSound
						{
							name = GetSoundFileNameFromType(Platforms.iPhone, type, enableCustomSounds),
							critical = category == "calls" ? 1 : 0,
							volume = 1.0f
						}
					},
					eventCode = eventCode,
					type = type
				};

				appleNotification = JsonConvert.SerializeObject(apnsPayload);

				var appleOutcome = await hubClient.SendAppleNativeNotificationAsync(appleNotification, string.Format("unitId:{0}", unitId));

				return appleOutcome.State;
			}
			catch (Exception ex)
			{
				string exception = ex.ToString();
			}

			return NotificationOutcomeState.Unknown;
		}

		public async Task<NotificationOutcomeState> SendWindowsNotification(string title, string subTitle, string userId, bool enableCustomSounds)
		{
			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureUnitNotificationHub_PushUrl);

			var windowsMessage = string.Format(@"<toast><visual><binding template=""ToastText01""><text id=""1"">{0}</text></binding></visual></toast>", title);

			var messageOutcome = await hubClient.SendWindowsNativeNotificationAsync(windowsMessage, string.Format("userId:{0}", userId));

			return messageOutcome.State;
		}

		public async Task<NotificationOutcomeState> SendWindowsPhoneNotification(string title, string subTitle, string userId, bool enableCustomSounds)
		{
			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureUnitNotificationHub_PushUrl);

			string winPhoneMessage = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
				"<wp:Notification xmlns:wp=\"WPNotification\">" +
					 "<wp:Toast>" +
						string.Format("<wp:Text1>{0}</wp:Text1>", title) +
						string.Format("<wp:Text2>{0}</wp:Text2>", subTitle) +
						"<wp:Param></wp:Param>" +
					 "</wp:Toast> " +
				"</wp:Notification>";

			var messageOutcome = await hubClient.SendMpnsNativeNotificationAsync(winPhoneMessage, string.Format("userId:{0}", userId));

			return messageOutcome.State;
		}

		private string GetSoundFileNameFromType(Platforms platform, string type, bool enableCustomSounds)
		{
			if (!enableCustomSounds)
			{
				if (platform == Platforms.iPhone)
					return "beep.caf";

				return "beep.mp3";
			}

			if (type == ((int)PushSoundTypes.CallEmergency).ToString())
			{
				if (platform == Platforms.iPhone)
					return "callemergency.caf";

				return "callemergency.wav";
			}
			else if (type == ((int)PushSoundTypes.CallHigh).ToString())

			{
				if (platform == Platforms.iPhone)
					return "callhigh.caf";

				return "callhigh.mp3";
			}
			else if (type == ((int)PushSoundTypes.CallMedium).ToString())
			{
				if (platform == Platforms.iPhone)
					return "callmedium.caf";

				return "callmedium.mp3";
			}
			else if (type == ((int)PushSoundTypes.CallLow).ToString())
			{
				if (platform == Platforms.iPhone)
					return "calllow.caf";

				return "calllow.mp3";
			}
			else if (type == ((int)PushSoundTypes.Notifiation).ToString())
			{
				if (platform == Platforms.iPhone)
					return "notification.caf";

				return "notification.mp3";
			}
			else if (type == ((int)PushSoundTypes.Message).ToString())
			{
				if (platform == Platforms.iPhone)
					return "message.caf";

				return "message.mp3";
			}
			else
			{
				if (platform == Platforms.iPhone)
					return $"{type}.caf";

				return $"{type}.mp3";
			}
		}

		private string FormatForAndroidNativePush(string fileName)
		{
			if (String.IsNullOrWhiteSpace(fileName))
				return string.Empty;

			return Path.GetFileNameWithoutExtension(fileName).Replace("_", "").ToLower();
		}

		private string CreateAndroidNotification(string title, string subTitle, string eventCode, string type, int count, string color, string channel)
		{
			if (color == null)
				color = "#ff0000";

			if (count == 0)
				count = 1;

			dynamic pushNotification = new JObject();

			pushNotification.message = new JObject();
			pushNotification.message.notification = new JObject();
			pushNotification.message.notification.title = title;
			pushNotification.message.notification.body = subTitle;
			//pushNotification.notification.android_channel_id = type;

			pushNotification.message.android = new JObject();

			if (channel != null && channel == "calls")
				pushNotification.message.android.priority = 1;

			//pushNotification.message.android.ttl = "86400";
			pushNotification.message.android.notification = new JObject();
			//pushNotification.android.notification.color = color;
			pushNotification.message.android.notification.channel_id = type;
			//pushNotification.android.notification.sound = soundFilename;
			pushNotification.message.android.notification.default_sound = true;

			if (channel != null && channel == "calls")
			{
				pushNotification.message.android.notification.sticky = true;
				pushNotification.message.android.notification.notification_priority = 5;
			}

			pushNotification.message.data = new JObject();
			pushNotification.message.data.title = title;
			pushNotification.message.data.message = subTitle;
			pushNotification.message.data.notId = eventCode;
			pushNotification.message.data.eventCode = eventCode;
			pushNotification.message.data.type = type;

			return pushNotification.ToString();
		}
	}
}
