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

		// ── TOTP Settings ─────────────────────────────────────────────────────────────

		/// <summary>
		/// Issuer name embedded in the otpauth:// URI shown in QR codes.
		/// This is the label that appears in authenticator apps (e.g. Google Authenticator,
		/// Microsoft Authenticator, Authy).
		/// </summary>
		public static string TotpIssuerName = "Resgrid";
	}
}

