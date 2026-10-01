using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The revocation state of the credential behind a verification (passkey plan section 8.2). A passkey grant names the
	/// passkey and its state version; an approval grant, the approving Responder passkey and session; a provider step-up
	/// grant, the department's tested mapping and its version. Revoking or changing any of them voids the grant at its next
	/// use. TOTP needs none of this: advancing the account's authentication generation revokes it.
	/// </summary>
	public interface IMfaCredentialStateService
	{
		/// <summary>
		/// The credential a verification used, while it still counts for <paramref name="client"/>, <paramref name="userId"/>
		/// and <paramref name="departmentId"/>; null when it no longer does (or the method needs no credential).
		/// </summary>
		Task<MfaCredentialSnapshot> ResolveAsync(string userId, int departmentId, UserSessionClientApplication client, MfaEvidenceMethod method,
			string factorReference, long authenticationGeneration, CancellationToken cancellationToken = default);

		/// <summary>Whether the credential a version 2 grant names still counts, at the state version the grant carries.</summary>
		Task<bool> IsCurrentAsync(ProtectedDataGrant grant, CancellationToken cancellationToken = default);
	}
}
