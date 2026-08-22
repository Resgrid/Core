using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IUserExternalIdentityLinksRepository : IRepository<UserExternalIdentityLink>
	{
		Task<UserExternalIdentityLink> GetActiveBySubjectAsync(string departmentSsoConfigId, string externalSubject);
		Task<UserExternalIdentityLink> GetActiveByUserAndConfigAsync(string userId, string departmentSsoConfigId);
		Task<IReadOnlyList<UserExternalIdentityLink>> GetActiveByUserAsync(string userId);
	}
}
