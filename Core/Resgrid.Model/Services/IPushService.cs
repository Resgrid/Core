using System;
using System.Threading.Tasks;
using Resgrid.Model.Messages;

namespace Resgrid.Model.Services
{
	public interface IPushService
	{
		Task<bool> PushMessage(StandardPushMessage message, string userId, UserProfile profile = null);
		Task<bool> PushCall(StandardPushCall call, string userId, UserProfile profile = null, DepartmentCallPriority priority = null);
		Task<bool> Register(PushUri pushUri);
		Task<bool> UnRegister(PushUri pushUri);
		void UnRegisterNotificationOnly(PushUri pushUri);
		Task<bool> PushNotification(StandardPushMessage message, string userId, UserProfile profile = null);
		Task<bool> RegisterUnit(PushUri pushUri);
		Task<bool> UnRegisterUnit(PushUri pushUri);
		Task<bool> PushChat(StandardPushMessage message, string userId, UserProfile profile = null);
		Task<bool> PushCallUnit(StandardPushCall call, int unitId, DepartmentCallPriority priority = null);
	}
}
