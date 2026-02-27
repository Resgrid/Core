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
	}
}
