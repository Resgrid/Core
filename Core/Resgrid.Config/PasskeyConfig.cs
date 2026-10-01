namespace Resgrid.Config
{
	/// <summary>
	/// Passkeys, Responder approval and provider step-up (passkey plan sections 10.2-10.3; Phase 0 workbook sections 5
	/// and 12). Every rollout gate starts OFF. A gate that is ON still does nothing unless the relying-party registry
	/// validates at startup, so a half-configured deployment fails closed. Turning a gate off stops new use only; it never
	/// removes durable revocations or makes an old grant valid.
	/// </summary>
	public static class PasskeyConfig
	{
		// ── Rollout gates (all OFF) ───────────────────────────────────────────────────

		/// <summary>Allow users to register passkeys.</summary>
		public static bool RegistrationEnabled = false;

		/// <summary>Accept passkeys as login MFA.</summary>
		public static bool LoginAcceptanceEnabled = false;

		/// <summary>Accept passkey evidence for Protected Data Grants.</summary>
		public static bool AdpAcceptanceEnabled = false;

		/// <summary>Issue version 2 Protected Data Grants. Every reader must support v2 before this is turned on.</summary>
		public static bool EmitGrantV2 = false;

		/// <summary>Allow shared-device (vehicle tablet / dispatch workstation) sessions.</summary>
		public static bool SharedDeviceModeEnabled = false;

		/// <summary>Allow "Approve with Responder" cross-app MFA.</summary>
		public static bool ResponderApprovalEnabled = false;

		/// <summary>Allow provider step-up (federated MFA) for departments that opt in.</summary>
		public static bool ProviderStepUpEnabled = false;

		// ── Relying parties (one per client; workbook section 5) ──────────────────────

		/// <summary>
		/// One relying party per client, separated by ";". Each entry is <c>client=rpId|origin,origin</c>, where client is
		/// web, responder, unit, dispatch or command (ic), the RP ID is that client's own host, and each origin is
		/// <c>https://host[:port]</c> under the RP ID or <c>android:apk-key-hash:&lt;base64url&gt;</c>. Example:
		/// <c>web=app.resgrid.com|https://app.resgrid.com;unit=unit.resgrid.com|https://unit.resgrid.com,android:apk-key-hash:abc</c>.
		/// Empty means passkeys are unavailable on this deployment.
		/// </summary>
		public static string RelyingParties = "";

		/// <summary>Name the platform shows in the passkey prompt.</summary>
		public static string RelyingPartyName = "Resgrid";

		// ── Ceremony limits ───────────────────────────────────────────────────────────

		public static int RegistrationChallengeLifetimeSeconds = 300;

		public static int AssertionChallengeLifetimeSeconds = 120;

		/// <summary>Failed verifications allowed against one challenge before it is spent.</summary>
		public static int ChallengeMaxAttempts = 5;

		/// <summary>Pending challenges one user may hold at once; more is refused rather than queued.</summary>
		public static int MaxOutstandingChallengesPerUser = 10;

		/// <summary>Active passkeys per user per client (at most 5 clients).</summary>
		public static int MaxActiveCredentialsPerClient = 10;

		public static int MaxDisplayNameLength = 100;

		// ── Responder approval (plan section 7.9 abuse controls) ──────────────────────

		/// <summary>An approval request lives this long and is never extended.</summary>
		public static int ApprovalRequestLifetimeSeconds = 120;

		/// <summary>Wrong numbers allowed before the request is denied.</summary>
		public static int ApprovalMaxNumberAttempts = 3;

		/// <summary>Approval requests one user may create per <see cref="ApprovalRateWindowMinutes"/>.</summary>
		public static int ApprovalMaxRequestsPerWindow = 5;

		public static int ApprovalRateWindowMinutes = 15;

		/// <summary>After two denials or expiries in a row (or one "not me"), new requests are refused this long.</summary>
		public static int ApprovalSuspensionMinutes = 15;

		/// <summary>How long after its expiry an approved request can still be used by the requester's final poll.</summary>
		public static int ApprovalConsumeGraceSeconds = 30;

		// ── Shared vehicle and workstation sessions (plan sections 10.5 and 12.5) ─────

		/// <summary>The longest idle lock a department may choose (at most 15 minutes).</summary>
		public static int SharedMaxIdleLockMinutes = 15;

		/// <summary>The longest shift a department may choose (at most 24 hours).</summary>
		public static int SharedMaxShiftHours = 24;

		/// <summary>Operator activity is written at most this often per session, so a busy screen is not a write per request.</summary>
		public static int SharedActivityWriteIntervalSeconds = 30;

		/// <summary>Unlock attempts one shared session may make per 5 minutes, on top of the account lockout.</summary>
		public static int SharedUnlockMaxAttempts = 5;
	}
}
