using System.Collections.Generic;

namespace Resgrid.Config
{
	/// <summary>
	/// Security Configuration and Settings
	/// </summary>
	public static class SecurityConfig
	{
		/// <summary>
		/// System level credentials user-name as key, password as value, for system level logins, not department api level
		/// which are configured in the application itself
		/// </summary>
		public static Dictionary<string, string> SystemLoginCredentials = new Dictionary<string, string>()
		{

		};

		/// <summary>
		/// System-level API key used by the SMTP Relay in hosted multi-department mode.
		/// When the X-Resgrid-SystemApiKey header matches this value, the request bypasses
		/// OAuth 2.0 authentication and is granted full cross-department access.
		/// The department for each operation is determined by the DepartmentId field in the
		/// request body/query parameters, not by the auth token.
		/// </summary>
		public static string SystemApiKey = "";

		/// <summary>
		/// Pepper for the keyed hash stored for each department API key (X-Resgrid-ApiKey). Leave blank to derive one
		/// from <see cref="EncryptionKey"/>. Changing it (or the encryption key when this is blank) invalidates every
		/// department API key.
		/// </summary>
		public static string DepartmentApiKeyPepper = "";

		/// <summary>Longest a department API key can be issued for, in days.</summary>
		public static int DepartmentApiKeyMaxLifetimeDays = 730;

		/// <summary>Most unrevoked, unexpired department API keys a department can hold at once.</summary>
		public static int DepartmentApiKeyMaxActivePerDepartment = 25;

		/// <summary>How long a department API key lookup is cached, in seconds (capped at 60). Revoking a key clears it at once.</summary>
		public static int DepartmentApiKeyCacheSeconds = 60;

		/// <summary>Invalid department API keys accepted from one address in <see cref="DepartmentApiKeyFailureWindowMinutes"/> before it is refused outright.</summary>
		public static int DepartmentApiKeyMaxFailuresPerAddress = 20;

		public static int DepartmentApiKeyFailureWindowMinutes = 10;

		/// <summary>
		/// Shared secret required on the internal scheduled-report endpoint (User/Reports/InternalRunReport).
		/// The report-delivery worker supplies this value as the "key" query parameter; requests without a
		/// matching key are rejected. Must be set (non-empty) for scheduled report delivery to function.
		/// </summary>
		public static string InternalReportsToken = "";

		// ── Encryption ───────────────────────────────────────────────────────────────

		/// <summary>AES-256 master key used by IEncryptionService for system-wide encryption.</summary>
		public static string EncryptionKey = "CHANGEME_32CHAR_MASTER_KEY_HERE!";

		/// <summary>Salt value used with PBKDF2 key derivation.</summary>
		public static string EncryptionSaltValue = "CHANGEME_SALT_VALUE_HERE";

		/// <summary>
		/// Number of PBKDF2-HMAC-SHA256 iterations used when deriving AES-256 encryption keys.
		/// OWASP recommends a minimum of 600,000 iterations for PBKDF2-HMAC-SHA256 (as of 2023).
		/// Increase this value over time as hardware capabilities improve.
		/// </summary>
		public static int Pbkdf2Iterations = 600000;

		/// <summary>
		/// Lifetime, in minutes, of signed anonymous file links (CallFiles/GetFile). Links are
		/// regenerated on every authenticated GetFilesForCall response, so a short lifetime only
		/// bounds how long a leaked/forwarded URL keeps working. Default 24 hours.
		/// </summary>
		public static int SignedFileLinkTtlMinutes = 1440;

		/// <summary>
		/// Accept legacy signed file links that carry no expiry (issued before expiring links
		/// shipped). Leave true through a deployment transition, then flip false so every
		/// anonymous file link has a bounded lifetime.
		/// </summary>
		public static bool AllowLegacySignedFileLinks = true;
	}
}
