using System.Threading.Tasks;

namespace Resgrid.Model.Providers;

public interface INovuProvider
{
	Task<bool> CreateUserSubscriber(string userId, string code, int departmentId, string email, string firstName, string lastName);
	Task<bool> CreateUnitSubscriber(int unitId, string code, int departmentId, string unitName, string deviceId);
	Task<bool> UpdateUserSubscriberFcm(string userId, string code, string token);
	Task<bool> UpdateUnitSubscriberFcm(int unitId, string code, string token);

	Task<bool> SendUnitDispatch(string title, string body, int unitId, string depCode, string eventCode, string type,
		bool enableCustomSounds, int count, string color);

	Task<bool> DeleteMessage(string messageId);
}
