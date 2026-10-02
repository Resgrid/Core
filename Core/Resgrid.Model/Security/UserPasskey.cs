using System;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// A registered passkey's public credential (passkey plan section 5.1). It belongs to one user and is bound to the one
	/// client application whose relying party registered it; an assertion from any other client is refused. Resgrid holds
	/// only the public key: never a private key or biometric data. Revocation is durable and advances the state version.
	/// </summary>
	public class UserPasskey
	{
		/// <summary>Server-generated row id, independent of the credential id.</summary>
		public string UserPasskeyId { get; set; }

		public string UserId { get; set; }

		/// <summary>The <see cref="UserSessionClientApplication"/> the credential is bound to. Immutable.</summary>
		public int ClientApplication { get; set; }

		/// <summary>The relying party the credential was registered against.</summary>
		public string RpId { get; set; }

		/// <summary>The complete credential id, compared byte for byte after the hash lookup.</summary>
		public byte[] CredentialId { get; set; }

		/// <summary>SHA-256 of the credential id: the unique, indexed lookup key within the RP.</summary>
		public byte[] CredentialIdHash { get; set; }

		/// <summary>The verified COSE public key.</summary>
		public byte[] PublicKey { get; set; }

		/// <summary>COSE algorithm identifier of the public key (for example -7 for ES256), when known.</summary>
		public int? Algorithm { get; set; }

		/// <summary>The opaque per-account, per-RP user handle given to the authenticator.</summary>
		public byte[] UserHandle { get; set; }

		public long SignCount { get; set; }

		public bool IsBackupEligible { get; set; }

		public bool IsBackedUp { get; set; }

		/// <summary>Comma-separated transports the authenticator reported; a hint only.</summary>
		public string Transports { get; set; }

		public string Aaguid { get; set; }

		public string AttestationFormat { get; set; }

		/// <summary>User-managed name, bounded and escaped on display.</summary>
		public string DisplayName { get; set; }

		public DateTime CreatedOnUtc { get; set; }

		// Registration context: what the server observed about the registering session. Not proof of a device.
		public string RegistrationPlatform { get; set; }
		public string RegistrationInstallation { get; set; }
		public string RegistrationUserAgentFamily { get; set; }

		/// <summary>Client-reported authenticator attachment ("platform" or "cross-platform"); a hint only.</summary>
		public string RegistrationAttachment { get; set; }

		public bool RegisteredInSharedMode { get; set; }

		public DateTime? LastUsedOnUtc { get; set; }
		public int? LastUsedClientApplication { get; set; }
		public string LastUsedInstallation { get; set; }
		public bool LastUsedInSharedMode { get; set; }

		/// <summary>Responder credentials only: whether it may approve other apps' requests (plan section 7.9).</summary>
		public bool ApprovalEnabled { get; set; }

		public DateTime? RevokedOnUtc { get; set; }
		public int? RevocationReason { get; set; }
		public string RevokedByUserId { get; set; }

		/// <summary>Advanced on revocation and on every change a grant or evidence could depend on.</summary>
		public long StateVersion { get; set; }

		public bool IsActive => RevokedOnUtc == null;

		/// <summary>The MFA evidence factor reference for a passkey, so its removal can retire what it verified.</summary>
		public static string FactorReferenceFor(string userPasskeyId) =>
			string.IsNullOrWhiteSpace(userPasskeyId) ? null : "passkey:" + userPasskeyId;
	}

	public enum PasskeyRevocationReason
	{
		RemovedByUser = 1,
		RemovedAllForClient = 2,
		FactorRecovery = 3,
		AccountDeactivated = 4
	}
}
