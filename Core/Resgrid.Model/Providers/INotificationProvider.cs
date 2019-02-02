using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface INotificationProvider
	{
		Task RegisterPush(PushUri pushUri);
		Task UnRegisterPush(PushUri pushUri);
		Task SendAllNotifications(string title, string subTitle, string userId, string eventCode, string type, bool enableCustomSounds, int count, string color);
		Task<List<PushRegistrationDescription>> GetRegistrationsByDeviceId(string deviceId);
		Task<List<PushRegistrationDescription>> GetRegistrationsByUserId(string userId);
		Task UnRegisterPushByUserDeviceId(PushUri pushUri);
	}
}
