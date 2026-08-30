using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Inputs for issuing a Protected Data Grant. The CALLER (the identity-tier step-up endpoint) is
	/// responsible for having verified fresh MFA and for supplying the department's CURRENT policy
	/// epoch and step-up window — the grant service performs no lookups and no MFA checks; it only
	/// binds already-verified facts into a signed token.
	/// </summary>
	public class ProtectedDataGrantIssueRequest
	{
		public string UserId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Login session id (sid claim) when the session carries one; null otherwise.</summary>
		public string SessionId { get; set; }

		/// <summary>Numeric UserSessionClientApplication of the authenticated session (default Api).</summary>
		public int ClientApp { get; set; }

		/// <summary>The department's current policy epoch (0 when the department has no policy row).</summary>
		public long PolicyEpoch { get; set; }

		/// <summary>Absolute grant lifetime in minutes (the department step-up window, ceiling-clamped).</summary>
		public int WindowMinutes { get; set; }

		/// <summary>Scopes to grant (ProtectedDataGrantScopes values). Empty/null grants nothing.</summary>
		public IReadOnlyList<string> Scopes { get; set; }

		/// <summary>UTC instant the MFA step-up completed. The mfa_at claim; never refreshed later.</summary>
		public DateTime MfaAtUtc { get; set; }

		/// <summary>Set when the department exempted the calling client from the step-up prompt.</summary>
		public bool StepUpExempt { get; set; }
	}

	/// <summary>Result of a successful grant issuance. The token is sensitive-in-transit but value-free.</summary>
	public class ProtectedDataGrantIssueResult
	{
		/// <summary>Unique grant id (jti) for client display, audit and replay correlation.</summary>
		public string GrantId { get; set; }

		/// <summary>Compact signed grant token the client presents alongside its ordinary access token.</summary>
		public string Token { get; set; }

		/// <summary>Absolute UTC expiry of the grant.</summary>
		public DateTime ExpiresOnUtc { get; set; }
	}

	/// <summary>
	/// Value-free validation outcomes for a presented Protected Data Grant. Anything but Valid MUST
	/// fail the protected operation closed; the distinct values exist for audit metrics, never for
	/// leaking token internals to callers.
	/// </summary>
	public enum ProtectedDataGrantValidationOutcome
	{
		Valid = 0,

		/// <summary>No validation key material is configured on this host.</summary>
		NotConfigured = 1,

		/// <summary>Missing, unparseable, wrong algorithm, wrong issuer/audience, or bad signature.</summary>
		Invalid = 2,

		/// <summary>Signature fine but the grant is outside its absolute lifetime (bounded skew).</summary>
		Expired = 3,

		/// <summary>The dept claim does not match the department the operation targets.</summary>
		WrongDepartment = 4,

		/// <summary>The department's policy epoch moved past the grant's — the grant is revoked.</summary>
		EpochRevoked = 5,

		/// <summary>The grant does not carry the scope the operation requires.</summary>
		MissingScope = 6
	}
}
