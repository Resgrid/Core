using System;
using System.Collections.Generic;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// What a registration ceremony verified (passkey plan section 6.1 step 5). Library-neutral: the provider adapter maps
	/// its own types onto this, so no WebAuthn library DTO reaches the domain model (plan section 4).
	/// </summary>
	public sealed class PasskeyRegistrationVerification
	{
		public bool Succeeded { get; init; }

		/// <summary>Value-free reason when verification failed (plan section 11): passkey_verification_failed.</summary>
		public string FailureCode { get; init; }

		public byte[] CredentialId { get; init; }
		public byte[] PublicKey { get; init; }
		public int? Algorithm { get; init; }
		public byte[] UserHandle { get; init; }
		public long SignCount { get; init; }
		public bool IsBackupEligible { get; init; }
		public bool IsBackedUp { get; init; }
		public IReadOnlyList<string> Transports { get; init; }
		public Guid Aaguid { get; init; }
		public string AttestationFormat { get; init; }
		public string Attachment { get; init; }

		public static PasskeyRegistrationVerification Failed(string code = "passkey_verification_failed") => new() { Succeeded = false, FailureCode = code };
	}

	/// <summary>The stored credential an assertion is verified against: this user's, bound to the asserting client.</summary>
	public sealed class PasskeyAssertionCredential
	{
		public byte[] CredentialId { get; init; }
		public byte[] PublicKey { get; init; }
		public byte[] UserHandle { get; init; }
		public long SignCount { get; init; }
	}

	/// <summary>What an assertion ceremony verified.</summary>
	public sealed class PasskeyAssertionVerification
	{
		public bool Succeeded { get; init; }
		public string FailureCode { get; init; }
		public long SignCount { get; init; }
		public bool IsBackedUp { get; init; }

		public static PasskeyAssertionVerification Failed(string code = "passkey_verification_failed") => new() { Succeeded = false, FailureCode = code };
	}
}
