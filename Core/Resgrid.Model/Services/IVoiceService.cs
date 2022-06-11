

using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IVoiceService
	{
		Task<bool> CanDepartmentUseVoiceAsync(int departmentId);

		Task<DepartmentVoice> GetVoiceSettingsForDepartmentAsync(int departmentId);

		Task<bool> InitializeDepartmentUsersWithVoipProviderAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken));

		Task<DepartmentVoice> GetOrCreateDepartmentVoiceRecordAsync(Department department, CancellationToken cancellationToken = default(CancellationToken));

		Task<DepartmentVoiceUser> SaveUserToVoipProviderAsync(DepartmentVoice voice, UserProfile profile, string emailAddress, CancellationToken cancellationToken = default(CancellationToken));

		Task<DepartmentVoiceChannel> SaveChannelToVoipProviderAsync(Department department, string name, CancellationToken cancellationToken = default(CancellationToken));

		Task<string> GetOpenViduSessionToken(string sessionId);

		Task<DepartmentVoiceChannel> GetDepartmentVoiceChannelByIdAsync(string departmentVoiceChannelId);

		Task<bool> DeleteDepartmentVoiceChannelAsync(DepartmentVoiceChannel channel,
			CancellationToken cancellationToken = default(CancellationToken));
	}
}
