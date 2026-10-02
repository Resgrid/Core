using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// The WebAuthn protocol adapter (passkey plan section 4): builds ceremony options for one client's relying party and
	/// verifies the client's response against the exact options that were issued. It is stateless: single use of a
	/// challenge, credential ownership and binding are the caller's job (the spike showed the library accepts a replayed
	/// assertion and verifies against whatever key it is given). Options and responses cross this boundary as JSON.
	/// </summary>
	public interface IPasskeyProvider
	{
		/// <summary>True when the client has a validated relying party.</summary>
		bool IsAvailableFor(UserSessionClientApplication client);

		/// <summary>
		/// Creation options requiring user verification and a discoverable credential, with attestation "none" and the
		/// user's existing credentials for this client excluded. <paramref name="preferRoaming"/> asks for a security key or
		/// phone, for shared installations (plan section 6.5).
		/// </summary>
		string CreateRegistrationOptions(UserSessionClientApplication client, byte[] userHandle, string userName, string displayName,
			IReadOnlyList<byte[]> excludeCredentialIds, bool preferRoaming);

		Task<PasskeyRegistrationVerification> VerifyRegistrationAsync(UserSessionClientApplication client, string optionsJson,
			string attestationResponseJson, CancellationToken cancellationToken = default);

		/// <summary>Request options requiring user verification, limited to <paramref name="allowCredentialIds"/>.</summary>
		string CreateAssertionOptions(UserSessionClientApplication client, IReadOnlyList<byte[]> allowCredentialIds);

		/// <summary>The raw credential id an assertion response names, so the caller can load the bound credential; null when unreadable.</summary>
		byte[] ReadCredentialId(string assertionResponseJson);

		Task<PasskeyAssertionVerification> VerifyAssertionAsync(UserSessionClientApplication client, string optionsJson,
			string assertionResponseJson, PasskeyAssertionCredential credential, CancellationToken cancellationToken = default);
	}
}
