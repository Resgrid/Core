using System;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// Server-held proof that a factor was verified for one session (passkey plan section 5.3). Controllers never
	/// deserialize this from a request; only the server writes it when a verification actually succeeds. Readers compare
	/// <see cref="AuthenticationGeneration"/> with the user's current value, so any credential or MFA change that advances
	/// the generation retires older evidence without touching these rows.
	/// </summary>
	public class MfaEvidence
	{
		/// <summary>
		/// The evidence key of a tracked session, shared by Web and API so both hosts read the same session's evidence
		/// (a Web session's API bridge carries the Web session's id).
		/// </summary>
		public static string TrackedSessionKey(string userSessionId) =>
			string.IsNullOrWhiteSpace(userSessionId) ? null : "sid:" + userSessionId;

		/// <summary>The tracked session id a <see cref="TrackedSessionKey"/> names; null for any other key.</summary>
		public static string TrackedSessionId(string sessionKey) =>
			sessionKey != null && sessionKey.Length > 4 && sessionKey.StartsWith("sid:", System.StringComparison.Ordinal) ? sessionKey.Substring(4) : null;

		public string MfaEvidenceId { get; set; }

		public string UserId { get; set; }

		/// <summary>The tracked session id as <c>sid:{id}</c>, or <c>web:{id}</c> for an untracked Web session.</summary>
		public string SessionKey { get; set; }

		public int ClientApplication { get; set; }

		public int Kind { get; set; }

		public int Method { get; set; }

		public int Purpose { get; set; }

		/// <summary>Set only for department-scoped evidence (ADP step-up).</summary>
		public int? DepartmentId { get; set; }

		public DateTime VerifiedOnUtc { get; set; }

		public DateTime ExpiresOnUtc { get; set; }

		public long AuthenticationGeneration { get; set; }

		/// <summary>Opaque reference to the factor instance (passkey credential, SSO config), never the secret itself.</summary>
		public string FactorReference { get; set; }

		public DateTime? RevokedOnUtc { get; set; }

		public MfaEvidenceKind EvidenceKind => (MfaEvidenceKind)Kind;

		public MfaEvidenceMethod EvidenceMethod => (MfaEvidenceMethod)Method;
	}

	public enum MfaEvidenceKind
	{
		/// <summary>Password or SSO sign-in / reauthentication.</summary>
		FirstFactor = 1,

		/// <summary>A verified second factor. Only this kind can satisfy MFA.</summary>
		SecondFactor = 2,

		/// <summary>A recovery-code use. Recorded for accountability; never satisfies MFA or ADP (plan section 6.1).</summary>
		Recovery = 3
	}

	public enum MfaEvidenceMethod
	{
		Password = 1,
		Sso = 2,
		Totp = 10,
		Passkey = 11,
		PasskeyApproval = 12,
		Federated = 13,
		RecoveryCode = 20
	}

	public enum MfaEvidencePurpose
	{
		Login = 1,
		Reauthentication = 2,
		StepUp = 3,
		AdpStepUp = 4,
		SharedUnlock = 5
	}
}
