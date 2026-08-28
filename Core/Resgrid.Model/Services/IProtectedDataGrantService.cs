using System;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Issues and validates tenant-bound Protected Data Grants (ADP plan section 3): short-lived
	/// signed tokens binding user, single department, session, client application, policy epoch,
	/// scopes and fresh-MFA time. Issuance runs ONLY on the identity tier (the step-up endpoint,
	/// after fresh TOTP); validation runs on the Protected Data Broker and at API enforcement
	/// points. No DEK or protected value ever appears in a grant. The service is pure token
	/// cryptography — MFA verification, permission resolution and policy-epoch lookups belong to
	/// its callers, and validation is fail closed: any outcome except Valid denies the operation.
	/// </summary>
	public interface IProtectedDataGrantService
	{
		/// <summary>True when signing key material (private key) is configured on this host.</summary>
		bool CanIssueGrants { get; }

		/// <summary>True when validation key material (public key) is configured on this host.</summary>
		bool CanValidateGrants { get; }

		/// <summary>
		/// Signs a grant from already-verified facts. Throws InvalidOperationException when signing
		/// is not configured (check CanIssueGrants first) and ArgumentException on an unusable
		/// request. Lifetime is absolute and clamped to the operator ceiling.
		/// </summary>
		ProtectedDataGrantIssueResult IssueGrant(ProtectedDataGrantIssueRequest request);

		/// <summary>
		/// Validates a presented grant token: pinned algorithm, signature, issuer, audience,
		/// absolute lifetime with small bounded skew, exact department match, current policy epoch,
		/// and the required scope. Returns Valid and the parsed claims, or a value-free failure
		/// outcome with a null grant. Never throws on malformed input.
		/// </summary>
		ProtectedDataGrantValidationOutcome ValidateGrant(string token, int expectedDepartmentId,
			long currentPolicyEpoch, string requiredScope, out ProtectedDataGrant grant, DateTime? utcNow = null);
	}
}
