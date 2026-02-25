namespace Resgrid.Config
{
	public static class WorkflowConfig
	{
		/// <summary>AES-256 master key used by IEncryptionService for system-wide encryption.</summary>
		public static string EncryptionKey = "CHANGEME_32CHAR_MASTER_KEY_HERE!";

		/// <summary>Salt value used with PBKDF2 key derivation.</summary>
		public static string EncryptionSaltValue = "CHANGEME_SALT_VALUE_HERE";

		/// <summary>Default maximum number of retry attempts for a failed workflow action execution.</summary>
		public static int DefaultMaxRetryCount = 3;

		/// <summary>Base seconds for exponential back-off: delay = RetryBackoffBaseSeconds * 2^(attempt-1)</summary>
		public static int RetryBackoffBaseSeconds = 5;

		/// <summary>Maximum concurrent workflow executions processed by a single worker instance.</summary>
		public static int MaxConcurrentWorkflows = 5;

		/// <summary>Maximum workflow enqueue operations allowed per department per minute (rate limiting).</summary>
		public static int RateLimitPerDepartmentPerMinute = 60;
	}
}

