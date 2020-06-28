using Resgrid.Model.Identity;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IClaimsRepository
	{
		Task<List<string>> GetRolesAsync(IdentityUser user);
		Task<IdentityUser> FindByIdAsync(string userId);
	}
}
