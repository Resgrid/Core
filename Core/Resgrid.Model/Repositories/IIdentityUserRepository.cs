using Resgrid.Model.Identity;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IIdentityUserRepository
	{
		Task<string> InsertAsync(IdentityUser user, CancellationToken cancellationToken);
		Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);
		Task<bool> UpdateAsync(IdentityUser user, CancellationToken cancellationToken);
		Task<IdentityUser> GetByIdAsync(string id);
		Task<IdentityUser> GetByUserNameAsync(string userName);
		Task<IdentityUser> GetByEmailAsync(string email);
		Task<IEnumerable<IdentityUser>> GetAllAsync();
		Task<IdentityUser> GetByUserLoginAsync(string loginProvider, string providerKey);

		Task<bool> InsertClaimsAsync(string id, IEnumerable<Claim> claims, CancellationToken cancellationToken);
		Task<bool> InsertLoginInfoAsync(string id, Microsoft.AspNetCore.Identity.UserLoginInfo loginInfo, CancellationToken cancellationToken);
		Task<bool> AddToRoleAsync(string id, string roleName, CancellationToken cancellationToken);

		Task<IList<Claim>> GetClaimsByUserIdAsync(string id);
		Task<IList<string>> GetRolesByUserIdAsync(string id);
		Task<IList<Microsoft.AspNetCore.Identity.UserLoginInfo>> GetUserLoginInfoByIdAsync(string id);
		Task<IList<IdentityUser>> GetUsersByClaimAsync(Claim claim);
		Task<IList<IdentityUser>> GetUsersInRoleAsync(string roleName);
		Task<bool> IsInRoleAsync(string id, string roleName);

		Task<bool> RemoveClaimsAsync(string id, IEnumerable<Claim> claims, CancellationToken cancellationToken);
		Task<bool> RemoveFromRoleAsync(string id, string roleName, CancellationToken cancellationToken);
		Task<bool> RemoveLoginAsync(string id, string loginProvider, string providerKey, CancellationToken cancellationToken);
		Task<bool> UpdateClaimAsync(string id, Claim oldClaim, Claim newClaim, CancellationToken cancellationToken);
	}
}
