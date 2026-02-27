namespace Resgrid.Config
{
	/// <summary>
	/// Configuration settings for contact method verification (email, mobile, home number).
	/// All values can be overridden via the standard config file or environment variables
	/// (e.g. RESGRID:VerificationConfig:VerificationCodeExpiryMinutes).
	/// </summary>
	public static class VerificationConfig
	{
		// ── Code Lifecycle ────────────────────────────────────────────────────────────

		/// <summary>Number of minutes a verification code remains valid before expiring.</summary>
		public static int VerificationCodeExpiryMinutes = 30;

		/// <summary>Number of numeric digits in a generated verification code.</summary>
		public static int VerificationCodeLength = 6;

		// ── Attempt / Send Rate Limits ────────────────────────────────────────────────

		/// <summary>Maximum number of confirmation attempts allowed per contact method per calendar day (UTC).</summary>
		public static int MaxVerificationAttemptsPerDay = 5;

		/// <summary>Maximum number of verification code send requests allowed per contact method per hour.</summary>
		public static int MaxVerificationSendsPerHour = 3;

		// ── reCAPTCHA v3 ─────────────────────────────────────────────────────────────

		/// <summary>
		/// Minimum reCAPTCHA v3 score (0.0–1.0) required to pass bot-detection on the API
		/// department-registration endpoint. Requests scoring below this threshold are rejected.
		/// </summary>
		public static decimal RecaptchaMinimumScore = 0.5m;
	}
}

