namespace Resgrid.Config
{
	/// <summary>
	/// Advanced Data Protection (ADP) platform configuration. Endpoint addresses, mounts and key
	/// names are configuration; SECRETS ARE NOT — the broker's client certificate and key, the
	/// OpenBao token, the YubiHSM PIN, and recovery shares must never appear here, in
	/// appsettings*.json, resgrid.env, container images, or the repository. Values load like every
	/// other Resgrid.Config class: "DataProtectionConfig.FieldName" JSON keys or
	/// RESGRID:DataProtectionConfig:FieldName environment variables.
	/// </summary>
	public static class DataProtectionConfig
	{
		/// <summary>Base URL of the Protected Data Broker service (empty = no broker deployed).</summary>
		public static string BrokerBaseUrl = "";

		/// <summary>Audience the application tier expects on broker mTLS/workload credentials.</summary>
		public static string BrokerAudience = "resgrid-protected-broker";

		/// <summary>Broker request timeout in milliseconds; protected operations fail closed on expiry.</summary>
		public static int BrokerTimeoutMs = 10000;

		/// <summary>
		/// Key-wrapping provider the broker uses: "OpenBaoTransit" (production default), or "LocalDev"
		/// for synthetic/non-PHI testing only — production startup must reject LocalDev.
		/// </summary>
		public static string KeyWrappingProviderType = "OpenBaoTransit";

		/// <summary>OpenBao base address, reachable ONLY from broker hosts (never Web/API/workers).</summary>
		public static string OpenBaoAddress = "";

		/// <summary>OpenBao Transit mount path.</summary>
		public static string OpenBaoTransitMount = "transit";

		/// <summary>Derived (per-department context) Transit KEK name.</summary>
		public static string OpenBaoTransitKeyName = "resgrid-dept-kek";

		/// <summary>
		/// Filesystem path to the broker's mTLS client certificate (PFX/PKCS#12) used for the OpenBao
		/// cert auth method. The path is configuration; the certificate FILE is a mounted secret and
		/// must never land in appsettings*.json, resgrid.env, container images, or the repository.
		/// </summary>
		public static string OpenBaoClientCertificatePath = "";

		/// <summary>PFX password, supplied through the environment/secret store only.</summary>
		public static string OpenBaoClientCertificatePassword = "";

		/// <summary>Optional named cert-auth role ("name" parameter on auth/cert/login); empty = any matching role.</summary>
		public static string OpenBaoCertAuthRoleName = "";

		/// <summary>OpenBao HTTP request timeout in milliseconds; unwrap/wrap fail closed on expiry.</summary>
		public static int OpenBaoTimeoutMs = 10000;

		/// <summary>Default Protected Data Grant lifetime in minutes when a department has no policy value.</summary>
		public static int StepUpWindowDefaultMinutes = 15;

		/// <summary>Department values above this trigger an administrator warning plus recorded reason.</summary>
		public static int StepUpWarningThresholdMinutes = 60;

		/// <summary>Operator ceiling on StepUpWindowMinutes; departments cannot exceed it.</summary>
		public static int StepUpMaximumMinutes = 480;

		/// <summary>ADP migration worker: departments migrated concurrently per night (BackOffice-adjustable).</summary>
		public static int MigrationNightlyConcurrency = 1;

		/// <summary>
		/// Operator kill switch: true stops the worker from opening NEW migration windows. It never
		/// interrupts an in-flight batch and never touches durable state, active protection, or
		/// queued departments (plan section 19.2).
		/// </summary>
		public static bool MigrationQueuePaused = false;

		/// <summary>Rows per transactional migration batch (cursor advances once per batch).</summary>
		public static int MigrationBatchSize = 500;

		/// <summary>
		/// Measured migration throughput in rows/second for the sizing estimate (plan section 18.2).
		/// Re-measured per deployment by the synthetic benchmark against production-equivalent
		/// hardware; the conservative default stands in until then.
		/// </summary>
		public static int MigrationBenchmarkRowsPerSecond = 200;

		/// <summary>Fixed per-table overhead added to the estimate, in seconds.</summary>
		public static int MigrationEstimatePerTableOverheadSeconds = 30;

		/// <summary>Verification-pass allowance as a fraction of the migration time (0.25 = +25%).</summary>
		public static double MigrationEstimateVerificationAllowance = 0.25;

		/// <summary>P90 multiplier over the P50 estimate — the range shown instead of false precision.</summary>
		public static double MigrationEstimateP90Multiplier = 2.0;

		/// <summary>Default department-local overnight migration window start ("HH:mm").</summary>
		public static string MigrationWindowDefaultStartLocal = "22:00";

		/// <summary>Default department-local overnight migration window end ("HH:mm").</summary>
		public static string MigrationWindowDefaultEndLocal = "06:00";

		/// <summary>Worker heartbeat interval for the department operation lock, in seconds.</summary>
		public static int LockHeartbeatIntervalSeconds = 60;

		/// <summary>Safety-valve lifetime added to each heartbeat; a stale lock stops enforcing after this.</summary>
		public static int LockExpirySeconds = 300;

		/// <summary>BackOffice protected-support grant lifetime in minutes (absolute, non-renewable).</summary>
		public static int BackofficeProtectedSupportWindowMinutes = 5;

		/// <summary>Hard maximum for BackofficeProtectedSupportWindowMinutes.</summary>
		public static int BackofficeProtectedSupportWindowMaximumMinutes = 15;
	}
}
