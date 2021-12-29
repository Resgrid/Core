using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IVoipProvider
	{
		Task<string> CreateUserIfNotExistsAsync(string voipSystemUserId, string emailAddress, UserProfile profile, int departmentId);

		Task<string> CreateDeviceForUserIfNotExistsAsync(string voipSystemUserId, string voipSystemDeviceId, UserProfile profile, int departmentId);

		Task<Tuple<string, string>> CreateConferenceIfNotExistsAsync(string voipSystemConferenceId, int departmentId, string name, string pin, int number);

		Task<string> CreateOpenViduSessionAndGetToken(string sessionId);
	}
}
