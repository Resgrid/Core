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

namespace Resgrid.Providers.Bus
{
	public class NotificationProvider : INotificationProvider
	{
		public async Task RegisterPush(PushUri pushUri)
		{
			if (String.IsNullOrWhiteSpace(pushUri.DeviceId))
				return;

			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureNotificationHub_PushUrl);
			var tagsWithHashedDeviceId = new List<string>(new string[] { string.Format("userId:{0}", pushUri.UserId),
														string.Format("platform:{0}", pushUri.PlatformType),
														string.Format("deviceId:{0}", pushUri.DeviceId.GetHashCode()) });

			if (pushUri.DepartmentId > 0)
			{
				tagsWithHashedDeviceId.Add(string.Format("departmentId:{0}", pushUri.DepartmentId));
			}

			CollectionQueryResult<RegistrationDescription> registrations = null;
			try
			{
				registrations = await hubClient.GetRegistrationsByTagAsync(string.Format("deviceId:{0}", pushUri.DeviceId.GetHashCode()), 50);
			}
			catch
			{
				// So this just fails, like whenever it wants. I would rather people get 2 or 3 push messages for a fire then none. 
			}

			// Loop through all Azure registrations for this Hashed DeviceId and remove them
			if (registrations != null)
			{
				foreach (var registration in registrations)
				{
					try
					{
						await hubClient.DeleteRegistrationAsync(registration);
					}
					catch (Exception ex)
					{
						Framework.Logging.LogException(ex);
					}
				}
			}

			if (pushUri.PlatformType == (int)Platforms.WindowsPhone7 || pushUri.PlatformType == (int)Platforms.WindowsPhone8)
			{
				if (!String.IsNullOrWhiteSpace(pushUri.PushLocation))
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
			}
			else if (pushUri.PlatformType == (int)Platforms.Windows8)
			{
				if (!String.IsNullOrWhiteSpace(pushUri.PushLocation))
				{
					try
					{
						var result = await hubClient.CreateWindowsNativeRegistrationAsync(pushUri.PushLocation, tagsWithHashedDeviceId.ToArray());
					}
					catch (ArgumentException ex)
					{
						Framework.Logging.LogException(ex,
							string.Format("Device Information: {0} {1} {2} {3}", tagsWithHashedDeviceId[0], tagsWithHashedDeviceId[1],
								tagsWithHashedDeviceId[2], tagsWithHashedDeviceId[3]));
					}
				}
			}
			else if (pushUri.PlatformType == (int)Platforms.Android)
			{
				try
				{
					var result = await hubClient.CreateFcmNativeRegistrationAsync(pushUri.DeviceId, tagsWithHashedDeviceId.ToArray());
				}
				catch (ArgumentException ex)
				{
					Framework.Logging.LogException(ex,
						string.Format("Device Information: {0} {1} {2} {3}", tagsWithHashedDeviceId[0], tagsWithHashedDeviceId[1],
							tagsWithHashedDeviceId[2], tagsWithHashedDeviceId[3]));
				}
			}
			else if (pushUri.PlatformType == (int)Platforms.iPad || pushUri.PlatformType == (int)Platforms.iPhone)
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

		public async Task UnRegisterPush(PushUri pushUri)
		{
			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureNotificationHub_PushUrl);

			var registrations = await hubClient.GetRegistrationsByTagAsync(string.Format("userId:{0}", pushUri.UserId), 50);

			foreach (var registration in registrations)
			{
				if (pushUri.PlatformType == (int)Platforms.Windows8 ||
					pushUri.PlatformType == (int)Platforms.WindowsPhone7 ||
					pushUri.PlatformType == (int)Platforms.WindowsPhone8)
				{
					var winReg = registration as WindowsRegistrationDescription;
					if (winReg != null && winReg.ChannelUri == pushUri.ChannelUri)
						await hubClient.DeleteRegistrationAsync(registration);
				}
				else if (pushUri.PlatformType == (int)Platforms.Android)
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
			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureNotificationHub_PushUrl);

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

		public async Task<List<PushRegistrationDescription>> GetRegistrationsByDeviceId(string deviceId)
		{
			List<PushRegistrationDescription> registrations = new List<PushRegistrationDescription>();

			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureNotificationHub_PushUrl);

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

		public async Task<List<PushRegistrationDescription>> GetRegistrationsByUserId(string userId)
		{
			var registrations = new List<PushRegistrationDescription>();

			try
			{
				var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureNotificationHub_PushUrl);

				var registraions = await hubClient.GetRegistrationsByTagAsync(string.Format("userId:{0}", userId), 50);

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

		public async Task SendAllNotifications(string title, string subTitle, string userId, string eventCode, string type, bool enableCustomSounds, int count, string color)
		{
			try
			{
				var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureNotificationHub_PushUrl);

				await SendAndroidNotification(title, subTitle, userId, eventCode, type, enableCustomSounds, count, color, hubClient);
				await SendAppleNotification(title, subTitle, userId, eventCode, type, enableCustomSounds, count, color, hubClient);
				//SendWindowsNotification(title, subTitle, userId).Wait();
				//SendWindowsPhoneNotification(title, subTitle, userId, enableCustomSounds);
			}
			catch (Exception ex)
			{
				string exception = ex.ToString();
			}
		}

		public async Task<NotificationOutcomeState> SendAndroidNotification(string title, string subTitle, string userId, string eventCode, string type, bool enableCustomSounds, int count, string color, NotificationHubClient hubClient = null)
		{
			try
			{
				if (hubClient == null)
					hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureNotificationHub_PushUrl);

				string androidNotification = null;

				//if (type == ((int)PushSoundTypes.CallEmergency).ToString() ||
				//	type == ((int)PushSoundTypes.CallHigh).ToString() ||
				//	type == ((int)PushSoundTypes.CallMedium).ToString() ||
				//	type == ((int)PushSoundTypes.CallLow).ToString())
				if (eventCode.ToLower().StartsWith("c"))
				{
					androidNotification = CreateAndroidNotification(title, subTitle, eventCode, type, count, color, "calls");

					//if (enableCustomSounds && type.Contains("CallPAudio"))
					//{
						/*
						androidNotification = "{ \"data\" : {\"title\":\"" + title + "\", \"message\":\"" + subTitle +
											  "\", \"content-available\": \"1\", \"eventCode\":\"" + eventCode + "\", \"sound\":\"" +
											  FormatForAndroidNativePush(GetSoundFileNameFromType(Platforms.Android, type,
												  enableCustomSounds)) + "\", \"soundname\":\"" +
											  FormatForAndroidNativePush(GetSoundFileNameFromType(Platforms.Android, type,
												  enableCustomSounds)) + "\"," + "}}";
						*/

					//	androidNotification = CreateAndroidDataNotification(title, subTitle, eventCode, type, count, color);
						//androidNotification = CreateAndroidNotification(title, subTitle, eventCode, type, count, color);
					//}
					//else
					//{
					//	androidNotification = "{ \"data\" : {\"title\":\"" + title + "\", \"message\":\"" + subTitle +
					//						  "\", \"content-available\": \"1\", \"eventCode\":\"" + eventCode + "\", \"sound\":\"" +
					//						  FormatForAndroidNativePush(GetSoundFileNameFromType(Platforms.Android, type,
					//							  enableCustomSounds)) + "\", \"soundname\":\"" +
					//						  FormatForAndroidNativePush(GetSoundFileNameFromType(Platforms.Android, type,
					//							  enableCustomSounds)) + "\"," +
					//						  //"\"actions\": [{ title: \"RESPONDING\", callback: \"window.pushActionResponding\"},{ title: \"NOT RESPONDING\", callback: \"window.pushActionNotResponding\"},]" +
					//						  "\"actions\": [{ icon: \"emailGuests\", title: \"RESPONDING\", callback: \"pushActionResponding\"},{ icon: \"emailGuests\", title: \"NOT RESPONDING\", callback: \"pushActionNotResponding\"}]" +
					//						  "}}";
					//}
				}
				else
				{
					androidNotification = "{ \"data\" : {\"title\":\"" + title + "\", \"message\":\"" + subTitle +
																			 "\", \"eventCode\":\"" + eventCode + "\", \"sound\":\"" +
																			 FormatForAndroidNativePush(GetSoundFileNameFromType(Platforms.Android, type,
																				 enableCustomSounds)) + "\", \"soundname\":\"" +
																			 FormatForAndroidNativePush(GetSoundFileNameFromType(Platforms.Android, type,
																				 enableCustomSounds)) + "\"}}";
				}

				var androidOutcome = await hubClient.SendFcmNativeNotificationAsync(androidNotification, string.Format("userId:{0}", userId));

				return androidOutcome.State;
			}
			catch (Exception ex)
			{

				string exception = ex.ToString();
			}

			return NotificationOutcomeState.Unknown;
		}

		public async Task<NotificationOutcomeState> SendAppleNotification(string title, string subTitle, string userId, string eventCode, string type, bool enableCustomSounds, int count, string color, NotificationHubClient hubClient = null)
		{
			try
			{
				if (hubClient == null)
					hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureNotificationHub_PushUrl);

				string appleNotification = null;
				string category = null;

				//if (type == ((int)PushSoundTypes.CallEmergency).ToString() ||
				//	type == ((int)PushSoundTypes.CallHigh).ToString() ||
				//	type == ((int)PushSoundTypes.CallMedium).ToString() ||
				//	type == ((int)PushSoundTypes.CallLow).ToString())
				//{
				//	appleNotification = "{\"aps\" : { \"alert\" : \"" + subTitle + "\", \"content-available\": 1, \"category\" : \"CALLS\", \"badge\" : " + count + ", \"sound\" : \"" + GetSoundFileNameFromType(Platforms.iPhone, type, enableCustomSounds) + "\" }, \"eventCode\": \"" + eventCode + "\" }";
				//}
				//else
				//{
				//	appleNotification = "{\"aps\" : { \"alert\" : \"" + subTitle + "\", \"badge\" : " + count + ", \"sound\" : \"" + GetSoundFileNameFromType(Platforms.iPhone, type, enableCustomSounds) + "\" }, \"eventCode\": \"" + eventCode + "\" }";
				//}


				if (type == ((int)PushSoundTypes.CallEmergency).ToString() ||
					type == ((int)PushSoundTypes.CallHigh).ToString() ||
					type == ((int)PushSoundTypes.CallMedium).ToString() ||
					type == ((int)PushSoundTypes.CallLow).ToString())
				{
					category = "calls";
				}
				else if (type == ((int)PushSoundTypes.Message).ToString())
				{
					category = "message";
				}
				else if (type == ((int)PushSoundTypes.Notifiation).ToString())
				{
					category = "notif";
				}


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
						sound = GetSoundFileNameFromType(Platforms.iPhone, type, enableCustomSounds)
					},
					eventCode = eventCode

				};

				appleNotification = JsonConvert.SerializeObject(apnsPayload);

				var appleOutcome = await hubClient.SendAppleNativeNotificationAsync(appleNotification, string.Format("userId:{0}", userId));

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
			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureNotificationHub_PushUrl);

			var windowsMessage = string.Format(@"<toast><visual><binding template=""ToastText01""><text id=""1"">{0}</text></binding></visual></toast>", title);

			var messageOutcome = await hubClient.SendWindowsNativeNotificationAsync(windowsMessage, string.Format("userId:{0}", userId));

			return messageOutcome.State;
		}

		public async Task<NotificationOutcomeState> SendWindowsPhoneNotification(string title, string subTitle, string userId, bool enableCustomSounds)
		{
			var hubClient = NotificationHubClient.CreateClientFromConnectionString(Config.ServiceBusConfig.AzureNotificationHub_FullConnectionString, Config.ServiceBusConfig.AzureNotificationHub_PushUrl);

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

				return "beep.wav";
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

				return "callhigh.wav";
			}
			else if (type == ((int)PushSoundTypes.CallMedium).ToString())
			{
				if (platform == Platforms.iPhone)
					return "callmedium.caf";

				return "callmedium.wav";
			}
			else if (type == ((int)PushSoundTypes.CallLow).ToString())
			{
				if (platform == Platforms.iPhone)
					return "calllow.caf";

				return "calllow.wav";
			}
			else if (type == ((int)PushSoundTypes.Notifiation).ToString())
			{
				if (platform == Platforms.iPhone)
					return "notification.caf";

				return "notification.wav";
			}
			else if (type == ((int)PushSoundTypes.Message).ToString())
			{
				if (platform == Platforms.iPhone)
					return "message.caf";

				return "message.wav";
			}
			else
			{
				if (platform == Platforms.iPhone)
					return $"{type}.mp3";

				return $"{type}.mp3";
			}

		}

		private string FormatForAndroidNativePush(string fileName)
		{
			if (String.IsNullOrWhiteSpace(fileName))
				return string.Empty;

			return Path.GetFileNameWithoutExtension(fileName).Replace("_", "").ToLower();
		}

		private string CreateAndroidDataNotification(string title, string subTitle, string eventCode, string type, int count, string color)
		{
			dynamic pushNotification = new JObject();
			pushNotification.data = new JObject();
			pushNotification.data.title = title;
			pushNotification.data.body = subTitle;
			pushNotification.data["content-available"] = 1;
			pushNotification.data["content_available"] = 1;
			pushNotification.data["force-start"] = 1;
			pushNotification.data.notId = eventCode;
			pushNotification.data.eventCode = eventCode;
			pushNotification.data.priority = "high";
			pushNotification.data.dcpid = type.Replace("CallPAudio_", "");
			pushNotification.data.mtitle = title;
			pushNotification.data.mbody = subTitle;
			//pushNotification.data.sound = FormatForAndroidNativePush(GetSoundFileNameFromType(Platforms.Android, type, true));
			//pushNotification.data.soundname = FormatForAndroidNativePush(GetSoundFileNameFromType(Platforms.Android, type, true));
			pushNotification.data.color = color;
			pushNotification.data.count = count;
			pushNotification.data.ledColor = new JArray(0, int.Parse(color.Substring(0, 2), System.Globalization.NumberStyles.HexNumber), int.Parse(color.Substring(2, 2), System.Globalization.NumberStyles.HexNumber), int.Parse(color.Substring(4, 2), System.Globalization.NumberStyles.HexNumber));
			//pushNotification.data.vibrationPattern = new JArray(2000, 500, 1000, 500);
			//pushNotification.data.android_channel_id = "calls";
			pushNotification.data.android_channel_id = type;

			return pushNotification.ToString();
		}

		private string CreateAndroidNotification(string title, string subTitle, string eventCode, string type, int count, string color, string channel)
		{
			if (color == null)
				color = "#ff0000";

			if (count == 0)
				count = 1;

			dynamic pushNotification = new JObject();
			pushNotification.data = new JObject();
			pushNotification.data.title = title;
			pushNotification.data.message = subTitle;
			pushNotification.data["content-available"] = 1;
			pushNotification.data["force-start"] = 1;
			pushNotification.data.notId = eventCode;
			pushNotification.data.eventCode = eventCode;

			if (channel != null && channel == "calls")
				pushNotification.data.priority = "high";

			string soundFilename = FormatForAndroidNativePush(GetSoundFileNameFromType(Platforms.Android, type, true));

			pushNotification.data.sound = soundFilename;
			pushNotification.data.soundname = soundFilename;
			pushNotification.data.color = color;
			pushNotification.data.count = count;
			pushNotification.data.ledColor = new JArray(0, int.Parse(color.Substring(1, 2), System.Globalization.NumberStyles.HexNumber),
				int.Parse(color.Substring(3, 2), System.Globalization.NumberStyles.HexNumber), int.Parse(color.Substring(5, 2), System.Globalization.NumberStyles.HexNumber));
			pushNotification.data.vibrationPattern = new JArray(500, 1000, 500);
			pushNotification.data.android_channel_id = type;

			return pushNotification.ToString();
		}
	}
}
