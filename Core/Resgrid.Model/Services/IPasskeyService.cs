using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Passkey enrollment, inventory, revocation and step-up assertions (passkey plan sections 6.1 and 6.5). Every command
	/// rechecks the rollout gate, the client's relying party and the caller's server-side evidence; a passkey is bound to
	/// the client that registered it and is only ever verified for that client.
	/// </summary>
	public interface IPasskeyService
	{
		/// <summary>Whether a passkey could be registered from <paramref name="client"/> on this deployment now.</summary>
		bool IsRegistrationAvailable(UserSessionClientApplication client);

		/// <summary>
		/// Creation options for a new passkey bound to the caller's client. Requires a password or SSO verification and an
		/// accepted second factor for this session within five minutes, TOTP enrolled and recovery codes remaining.
		/// </summary>
		Task<PasskeyCeremonyStart> BeginRegistrationAsync(PasskeyCaller caller, bool totpEnrolled, int recoveryCodesRemaining,
			CancellationToken cancellationToken = default);

		/// <summary>Verifies the attestation, spends the request, stores the public credential and audits it.</summary>
		Task<PasskeyRegistrationResult> CompleteRegistrationAsync(PasskeyCaller caller, string requestId, string credentialJson,
			string displayName, CancellationToken cancellationToken = default);

		/// <summary>The user's own active passkeys in every client (plan section 6.1 item 6).</summary>
		Task<IReadOnlyList<UserPasskey>> GetActiveForUserAsync(string userId, CancellationToken cancellationToken = default);

		Task<bool> HasActiveForClientAsync(string userId, UserSessionClientApplication client, CancellationToken cancellationToken = default);

		Task<int> CountActiveForUserAsync(string userId, CancellationToken cancellationToken = default);

		Task<PasskeyOutcome> RenameAsync(PasskeyCaller caller, string userPasskeyId, string displayName, CancellationToken cancellationToken = default);

		/// <summary>
		/// Revokes one of the caller's passkeys, in any client, after an accepted second factor within five minutes. Its
		/// evidence and the user's pending challenges are retired with it, and sessions that signed in with it end.
		/// </summary>
		Task<PasskeyRevocationResult> RevokeAsync(PasskeyCaller caller, string userPasskeyId, CancellationToken cancellationToken = default);

		Task<PasskeyRevocationResult> RevokeAllForClientAsync(PasskeyCaller caller, UserSessionClientApplication client,
			CancellationToken cancellationToken = default);

		/// <summary>Responder passkeys only: whether the credential may approve other apps' requests (plan section 7.9).</summary>
		Task<PasskeyOutcome> SetApprovalEnabledAsync(PasskeyCaller caller, string userPasskeyId, bool enabled,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Assertion options limited to the caller's passkeys for the calling client, bound to the caller's session or, for
		/// <see cref="AuthenticationChallengePurpose.LoginSecondFactor"/>, to its login transaction.
		/// </summary>
		Task<PasskeyCeremonyStart> BeginAssertionAsync(PasskeyCaller caller, AuthenticationChallengePurpose purpose,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Verifies an assertion against the passkey it names, which must be the caller's and bound to the calling client,
		/// then spends the request and records the use. Recording evidence is the caller's job.
		/// </summary>
		Task<PasskeyAssertionResult> CompleteAssertionAsync(PasskeyCaller caller, AuthenticationChallengePurpose purpose, string requestId,
			string credentialJson, CancellationToken cancellationToken = default);
	}
}
