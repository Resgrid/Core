using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Resgrid.Providers.Claims
{
	/// <summary>Shared identity and permission requirements for every chat transport.</summary>
	public static class ChatAuthorizationPolicyExtensions
	{
		public static AuthorizationPolicyBuilder RequireChatAccessClaims(this AuthorizationPolicyBuilder policy)
		{
			return policy
				.RequireAuthenticatedUser()
				.RequireClaim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View)
				.RequireAssertion(context =>
				{
					var userId = context.User.FindFirst(ClaimTypes.PrimarySid)?.Value;
					var departmentIdClaim = context.User.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value;

					return !string.IsNullOrWhiteSpace(userId) &&
						int.TryParse(departmentIdClaim, out var departmentId) && departmentId > 0;
				});
		}
	}
}
