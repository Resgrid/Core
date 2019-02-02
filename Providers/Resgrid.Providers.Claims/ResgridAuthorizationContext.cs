using System.Collections.ObjectModel;
using System.Security.Claims;
using Resgrid.Model;

namespace Resgrid.Providers.Claims
{
	public static class ResgridAuthorizationContext
	{
		public static AuthorizationContext BuildAdminRoleContext()
		{
			Collection<Claim> resourceClaims = new Collection<Claim>();
			resourceClaims.Add(new Claim(ClaimTypes.Role, SystemRoles.Admins));

			Collection<Claim> actionClaims = new Collection<Claim>();

			return new AuthorizationContext(ClaimsPrincipal.Current, resourceClaims, actionClaims);
		}
	}
}