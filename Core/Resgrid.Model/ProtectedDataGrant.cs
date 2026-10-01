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

		/// <summary>
		/// Grant contract version (grant_ver): 1 for grants without the claim, 2 for passkey-plan grants (plan section
		/// 8.2). A version 2 grant is bound to the caller's session, client and authentication generation, and is usable
		/// only after <c>ProtectedGrantBinding</c> confirms that binding against the validated session.
		/// </summary>
		public int Version { get; set; } = 1;

		/// <summary>Version 2 only: how the second factor was verified (<see cref="ProtectedDataGrantMfaMethods"/>).</summary>
		public string MfaMethod { get; set; }

		/// <summary>Version 2 only: opaque reference to the credential or evidence behind the grant, for revocation checks.</summary>
		public string MfaCredentialId { get; set; }

		/// <summary>Version 2 only: the credential or evidence state version at issuance.</summary>
		public long? MfaStateVersion { get; set; }

		/// <summary>Version 2 only: the account authentication generation at issuance (auth_gen).</summary>
		public long? AuthenticationGeneration { get; set; }

		/// <summary>Version 2 only: the shared-session lock version at issuance, for shared sessions.</summary>
		public long? SessionLockVersion { get; set; }

		/// <summary>Authentication methods references (amr) as issued.</summary>
		public IReadOnlyList<string> Amr { get; set; }
	}

	/// <summary>The mfa_method values of a version 2 grant (plan section 8.2). Nothing else is accepted.</summary>
	public static class ProtectedDataGrantMfaMethods
	{
		public const string Totp = "totp";

		/// <summary>A passkey registered to the same client as the grant.</summary>
		public const string Passkey = "passkey";

		/// <summary>A Responder approval (plan section 7.9).</summary>
		public const string PasskeyApproval = "passkey_approval";

		/// <summary>A mapped identity-provider step-up (plan section 7.8).</summary>
		public const string Federated = "federated";

		/// <summary>No second factor: only with step_up_exempt.</summary>
		public const string None = "none";

		public static bool IsKnown(string method) =>
			method == Totp || method == Passkey || method == PasskeyApproval || method == Federated || method == None;
	}
}
