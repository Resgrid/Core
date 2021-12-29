using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IUnitNotificationProvider
	{
		Task RegisterPush(PushUri pushUri);
		Task UnRegisterPush(PushUri pushUri);
		Task SendAllNotifications(string title, string subTitle, int unitId, string eventCode, string type, bool enableCustomSounds, int count, string color);
		Task<List<PushRegistrationDescription>> GetRegistrationsByDeviceId(string deviceId);
		Task<List<PushRegistrationDescription>> GetRegistrationsByUnitId(int unitId);
		Task<List<PushRegistrationDescription>> GetRegistrationsByUUID(string uuid);
		Task UnRegisterPushByUserDeviceId(PushUri pushUri);
		Task UnRegisterPushByUUID(string uuid);
	}
}
