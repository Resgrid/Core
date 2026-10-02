namespace Resgrid.Config
{
	/// <summary>
	/// Configuration settings for TOTP-based two-factor authentication.
	/// All values can be overridden via the standard config file or environment variables
	/// (e.g. RESGRID:TwoFactorConfig:DefaultRecoveryCodeCount).
	/// </summary>
	public static class TwoFactorConfig
	{
		// ── Recovery Codes ────────────────────────────────────────────────────────────

		/// <summary>Number of recovery codes generated when a user enrolls in 2FA.</summary>
		public static int DefaultRecoveryCodeCount = 10;

		/// <summary>
		/// UI warning threshold: show a warning to the user when their remaining
		/// recovery code count falls to this value or below.
		/// </summary>
		public static int RecoveryCodeWarningThreshold = 3;

		// ── Step-Up Verification ──────────────────────────────────────────────────────

		/// <summary>
		/// Number of minutes a successful step-up 2FA verification remains valid before
		/// the user is re-prompted when accessing a sensitive admin operation.
		/// </summary>
		public static int StepUpVerificationWindowMinutes = 15;

		// ── First-Factor Reauthentication (passkey plan section 6.2) ─────────────────────

		/// <summary>
		/// A password (or SSO) sign-in or reauthentication this recent may START a credential change: set up, replace or
		/// turn off the authenticator. Older sessions are sent to reauthenticate first.
		/// </summary>
		public static int FirstFactorReauthWindowMinutes = 5;

		/// <summary>
		/// A credential change started inside the reauthentication window may be COMPLETED this long after the
		/// reauthentication, so scanning a QR code or finding the app does not force a second password prompt.
		/// </summary>
		public static int FirstFactorOperationWindowMinutes = 10;

		/// <summary>How long a staged (not yet verified) authenticator key stays usable.</summary>
		public static int StagedAuthenticatorLifetimeMinutes = 10;

		/// <summary>Server-side MFA evidence rows are purged this long after they were recorded.</summary>
		public static int MfaEvidenceRetentionHours = 24;

		// ── Department MFA policy (passkey plan section 7.6) ──────────────────────────────

		/// <summary>
		/// Rollout gate for enforcing <c>DepartmentSecurityPolicy.RequireMfa</c> on the paths that never enforced it:
		/// Web password login, the API password grant and Web department entry (section 7.6 rows 1, 3 and 5). The API
		/// SSO exchange already enforces it and is unaffected. Off until administrators have been notified and the
		/// affected members counted (section 7.6 rollout).
		/// </summary>
		public static bool RequireMfaEnforcementEnabled = false;

		/// <summary>
		/// How recent actual MFA must be for security, SSO and MFA policy changes and ADP management commands (section
		/// 7.6 rows 10 and 13, section 8.4). A department's longer ADP data window never stretches this.
		/// </summary>
		public static int SensitiveOperationWindowMinutes = 5;

		// ── Login MFA transaction (passkey plan sections 5.2 and 7.2; workbook section 7.1) ──

		/// <summary>
		/// Rollout gate for <c>mfa_flow=transaction</c> on the API password grant. Off: a client asking for the transaction
		/// gets today's legacy <c>mfa_required</c> response unchanged and falls back to <c>totp_code</c>.
		/// </summary>
		public static bool LoginMfaTransactionEnabled = false;

		/// <summary>
		/// Rollout gate for Web sign-in on the login transaction (passkey plan section 7.1): after the password, Core Web offers
		/// every second factor the account has and the department accepts (authenticator code, a passkey for the web, approval
		/// from Responder, a recovery code). It also needs <see cref="LoginMfaTransactionEnabled"/>. Off: Web keeps the
		/// authenticator-code page unchanged.
		/// </summary>
		public static bool WebLoginMfaTransactionEnabled = false;

		/// <summary>How long a partial login waits for its second factor. Non-sliding.</summary>
		public static int LoginMfaTransactionLifetimeSeconds = 300;

		/// <summary>Failed second-factor attempts, of any method, before the transaction is spent.</summary>
		public static int LoginMfaTransactionMaxAttempts = 5;

		/// <summary>How long the one-use completion code can be redeemed at the token endpoint.</summary>
		public static int LoginMfaCompletionCodeLifetimeSeconds = 60;

		// ── Factor recovery and security notices (passkey plan sections 5.4 and 6.4) ─────

		/// <summary>How long a restricted factor recovery lasts. Non-sliding; expired recovery starts with the first factor again.</summary>
		public static int FactorRecoveryLifetimeMinutes = 10;

		/// <summary>Wrong codes for the replacement authenticator before the recovery is spent.</summary>
		public static int FactorRecoveryMaxAttempts = 5;

		/// <summary>
		/// Send security notices (passkey plan section 6.4). Off: nothing is queued or sent, so deploying changes nothing for
		/// existing accounts until outbound email is confirmed and this is turned on.
		/// </summary>
		public static bool SecurityNoticesEnabled = false;

		/// <summary>Delivery attempts for one security notice before it is recorded as failed.</summary>
		public static int SecurityNoticeMaxAttempts = 8;

		/// <summary>How long sent and failed security notices are kept.</summary>
		public static int SecurityNoticeRetentionDays = 90;

		// ── TOTP Settings ─────────────────────────────────────────────────────────────

		/// <summary>
		/// Issuer name embedded in the otpauth:// URI shown in QR codes.
		/// This is the label that appears in authenticator apps (e.g. Google Authenticator,
		/// Microsoft Authenticator, Authy).
		/// </summary>
		public static string TotpIssuerName = "Resgrid";

		// ── Authenticator seeds at rest (passkey workbook section 12, slice 14) ────────

		/// <summary>
		/// Write authenticator seeds (the active key and a staged replacement) encrypted, and re-encrypt older ones as they
		/// are read. Every build that has this field reads both forms, so deploy it everywhere before turning this on. Turn it
		/// off and run <c>--AuthenticatorSeeds --Decrypt</c> before rolling back below this build.
		/// </summary>
		public static bool AuthenticatorSeedEncryptionEnabled = false;

		/// <summary>
		/// Seed encryption keys, separated by ";": <c>keyId=base64</c> of 32 random bytes; key ids are letters and digits.
		/// Empty means one key derived from <c>SecurityConfig.EncryptionKey</c>, id <c>m1</c>. To rotate, add a key, make it
		/// active, run <c>--AuthenticatorSeeds --Encrypt</c>, then remove the old key.
		/// </summary>
		public static string AuthenticatorSeedKeyRing = "";

		/// <summary>The key id new seeds are written with. Empty means <c>m1</c> when the ring is empty.</summary>
		public static string AuthenticatorSeedActiveKeyId = "";

		// ── Recent MFA activity (plan section 6.5) ─────────────────────────────────────

		/// <summary>How long each verification is kept for the account's recent-activity view.</summary>
		public static int MfaActivityRetentionDays = 30;

		/// <summary>Failed verifications recorded per account per hour; more are counted by the lockout but not stored.</summary>
		public static int MfaActivityDeniedPerHour = 20;
	}
}

