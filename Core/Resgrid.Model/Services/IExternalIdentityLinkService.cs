using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	public interface IExternalIdentityLinkService
	{
		Task<UserExternalIdentityLink> GetBySubjectAsync(string departmentSsoConfigId, string externalSubject, CancellationToken cancellationToken = default);
		Task<UserExternalIdentityLink> SaveAsync(UserExternalIdentityLink link, CancellationToken cancellationToken = default);
		Task<SsoManagementState> GetSsoManagementStateAsync(string userId, CancellationToken cancellationToken = default);
		Task<bool> IsLocalLoginAllowedAsync(string userId, CancellationToken cancellationToken = default);
		Task<bool> IsLocalLoginAllowedAsync(string userId, int departmentId, CancellationToken cancellationToken = default);
	}
}
