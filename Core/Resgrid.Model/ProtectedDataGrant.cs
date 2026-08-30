using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Validated claims of a tenant-bound Protected Data Grant (ADP plan section 3.2). Produced ONLY
	/// by IProtectedDataGrantService.ValidateGrant after signature, lifetime, audience, department,
	/// policy-epoch and scope checks pass — never construct one from unvalidated input. Contains no
	/// key material and no protected values; it is safe to log its identifiers (GrantId, department)
	/// in value-free audit events.
	/// </summary>
	public class ProtectedDataGrant
	{
		/// <summary>Unique grant identifier (jti) — the replay/audit correlation id.</summary>
		public string GrantId { get; set; }

		/// <summary>Immutable user id (sub).</summary>
		public string UserId { get; set; }

		/// <summary>Exactly one department (dept) — never a list or wildcard.</summary>
		public int DepartmentId { get; set; }

		/// <summary>Login session identifier (sid) when the issuing session carried one.</summary>
		public string SessionId { get; set; }

		/// <summary>Numeric UserSessionClientApplication the session authenticated as (client_app).</summary>
		public int ClientApp { get; set; }

		/// <summary>Department policy epoch at issuance; a later epoch bump revokes this grant.</summary>
		public long PolicyEpoch { get; set; }

		/// <summary>Granted protected-operation scopes (see ProtectedDataGrantScopes).</summary>
		public IReadOnlyList<string> Scopes { get; set; }

		/// <summary>UTC instant the fresh MFA step-up completed (mfa_at). Absolute; never refreshed.</summary>
		public DateTime MfaAtUtc { get; set; }

		/// <summary>
		/// True when this grant was issued WITHOUT a second factor because the department exempted
		/// the calling client (<see cref="AdpStepUpExemptClients"/>). Carried explicitly rather than
		/// inferred from <see cref="MfaAtUtc"/>, which records when the grant was minted either way —
		/// an auditor asking "did somebody actually step up for this?" needs a straight answer.
		/// </summary>
		public bool StepUpExempt { get; set; }

		/// <summary>UTC issuance instant (iat).</summary>
		public DateTime IssuedAtUtc { get; set; }

		/// <summary>Absolute UTC expiry (exp) — the step-up window end; never sliding.</summary>
		public DateTime ExpiresOnUtc { get; set; }
	}
}
