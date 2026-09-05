using System.Security.Claims;
using Resgrid.Model;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// Request-side resolution of the Record grant a system principal is acting under (Identifier Allocation
	/// Registry section 4.4). The claim issued at sign-in only says "some grant exists"; this re-reads the
	/// configuration against the department the request actually resolved to, so a token minted while a grant
	/// existed stops working the moment the grant is removed or points at another department.
	/// <para>
	/// Nothing here can widen access. Mutating and restricted Record policies are unreachable for a system
	/// principal because the claim is never issued, not because this class declines to hand one out.
	/// </para>
	/// </summary>
	public static class RecordsSystemPrincipal
	{
		/// <summary>
		/// Whether the caller is a non-user principal — the SMTP relay key or a client_credentials service
		/// account. A real member never carries either marker.
		/// </summary>
		public static bool IsSystemPrincipal(ClaimsPrincipal principal)
		{
			if (principal == null)
				return false;

			return principal.HasClaim(ResgridClaimTypes.Data.ServiceAccount, "true") ||
				principal.HasClaim(c => c.Type == ResgridClaimTypes.Data.RecordGrantPurpose);
		}

		/// <summary>
		/// The grant covering this request, or null when the caller is a user principal or has no grant for
		/// the department. Callers treat null-with-a-system-principal as a denial, not as "unrestricted".
		/// </summary>
		public static SystemPrincipalRecordGrant ResolveGrant(ClaimsPrincipal principal, int departmentId)
		{
			if (!IsSystemPrincipal(principal))
				return null;

			return SystemPrincipalRecordGrant.For(departmentId);
		}
	}
}
