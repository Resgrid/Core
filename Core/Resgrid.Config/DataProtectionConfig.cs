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

		/// <summary>
		/// Grace in days after the paid-through date before an AUTOMATIC-billing lapse can schedule
		/// offboarding. Matches PlanAddon.GetEndDateFromNow()'s yearly + 14 days, which is the grace
		/// the rest of the platform already gives a failed renewal.
		/// </summary>
		public static int AddonAutomaticBillingGraceDays = 14;

		/// <summary>
		/// Grace in days for an INVOICED department. Sized for the worst common terms rather than the
		/// average: NET45 plus a fortnight of internal approval plus a fortnight of cheque handling
		/// still lands inside 75 days. Decrypting a customer's data because their finance team is
		/// slow is not a recoverable mistake, and the cost of being generous here is a few weeks of
		/// protection nobody has paid for yet.
		/// </summary>
		public static int AddonInvoicedBillingGraceDays = 75;

		/// <summary>
		/// Ceiling on any per-department grace override. Support can extend a genuine slow payer;
		/// nobody can accidentally grant a permanent free ride by typing an extra digit.
		/// </summary>
		public static int AddonMaxGraceDays = 180;

		/// <summary>Audience the application tier expects on broker mTLS/workload credentials.</summary>
		public static string BrokerAudience = "resgrid-protected-broker";

		/// <summary>Broker request timeout in milliseconds; protected operations fail closed on expiry.</summary>
		public static int BrokerTimeoutMs = 10000;

		/// <summary>
		/// Shared workload secret the application tier presents to the broker (X-Resgrid-Broker-Key).
		/// Supplied through the environment/secret store only; an empty value on the broker refuses
		/// every request (fail closed). This is defense-in-depth UNDER network isolation and mTLS —
		/// never the only control.
		/// </summary>
		public static string BrokerApiKey = "";

		/// <summary>Maximum field items one broker request may carry; larger requests are refused.</summary>
		public static int BrokerMaxItemsPerRequest = 200;

		/// <summary>
		/// Purposes the broker's workload decrypt lane (POST api/v1/broker/workload/decrypt?purpose=) accepts, comma
		/// separated (RMS plan section 5.9.4). Each purpose is an egress the department acknowledged in the
		/// application before the caller reaches the broker: neris-submission (worker 41), records-export
		/// (worker 45 / Workflow renders) and invoicing (invoice delivery, pay links and deployment finance: the
		/// Workforce &amp; Business Operations plan's document renders and the DTR void-reason append) and
		/// protected-workflow (an approved Protected Workflow release sending its allow-listed fields to its pinned
		/// destination). Empty disables the lane; callers fail closed with workload_purpose_denied.
		/// </summary>
		public static string BrokerWorkloadPurposes = "neris-submission,records-export,invoicing,workforce-costing,pay-data-reporting,protected-workflow";

		/// <summary>
		/// Days an approved Protected Workflow release stays Active before it expires and must be renewed with a
		/// fresh step-up and re-attestation. Expiry is checked at run time and by the daily sweep (worker 71).
		/// </summary>
		public static int ProtectedWorkflowReleaseLifetimeDays = 365;

		/// <summary>Hard HTTP timeout, in seconds, for a protected workflow step's request (and its OAuth2 token request).</summary>
		public static int ProtectedWorkflowHttpTimeoutSeconds = 30;

		/// <summary>Ceiling on the number of catalog fields one Protected Workflow release may allow-list.</summary>
		public static int ProtectedWorkflowMaxFieldsPerRelease = 16;

		/// <summary>
		/// How recent, in minutes, the approving administrator's step-up MFA must be for a release request, approval,
		/// renewal or the department toggle. Older proofs are refused with step_up_required.
		/// </summary>
		public static int ProtectedWorkflowStepUpFreshnessMinutes = 10;

		/// <summary>
		/// Whether a protected step may authenticate with an HttpBasic credential. Off by default: Bearer, API key and
		/// OAuth2 client credentials are allowed; Basic sends a long-lived password on every request.
		/// </summary>
		public static bool ProtectedWorkflowAllowHttpBasicCredentials = false;

		/// <summary>Days before ExpiresOn at which department administrators are emailed an expiry notice, comma separated.</summary>
		public static string ProtectedWorkflowExpiryNoticeDays = "30,7";

		/// <summary>Largest response body a protected step reads for its success rule or capture (larger is failed_response_too_large).</summary>
		public static int ProtectedWorkflowMaxResponseBytes = 1048576;

		/// <summary>Most ResponseCapture entries one protected step may declare.</summary>
		public static int ProtectedWorkflowMaxCaptureKeys = 5;

		/// <summary>Days a rotated private_key_jwt signing key stays published in the credential's JWKS.</summary>
		public static int WorkflowJwksOverlapDays = 7;

		/// <summary>True on the broker host to run the ADP migration coordinator sweep there (the only
		/// host with a real KMS adapter). Workers.Console keeps its sweep for liveness/offboarding
		/// flips but never runs nights — its engine reports unavailable.</summary>
		public static bool BrokerRunsMigrations = true;

		/// <summary>Broker-hosted migration sweep interval in seconds (matches worker command 27).</summary>
		public static int BrokerMigrationSweepSeconds = 300;

		/// <summary>Issuer (iss) on Protected Data Grants — the identity tier's logical name.</summary>
		public static string GrantIssuer = "resgrid-identity";

		/// <summary>Audience (aud) on Protected Data Grants, pinned by the broker and API validators.</summary>
		public static string GrantAudience = "resgrid-protected-data";

		/// <summary>
		/// Filesystem path to the grant SIGNING certificate (PFX with an ECDSA P-256 private key).
		/// Present ONLY on identity-tier hosts (the step-up endpoint); the broker gets the public
		/// validation certificate instead. The path is configuration; the file is a mounted secret.
		/// </summary>
		public static string GrantSigningCertificatePath = "";

		/// <summary>PFX password for the signing certificate, supplied through the environment only.</summary>
		public static string GrantSigningCertificatePassword = "";

		/// <summary>
		/// Filesystem path to the grant VALIDATION certificate (public key only, CER/PEM/PFX). Set on
		/// broker and API hosts. When empty, validation falls back to the signing certificate's
		/// public part where that is configured (single-host development).
		/// </summary>
		public static string GrantValidationCertificatePath = "";

		/// <summary>Bounded clock skew allowed when validating grant lifetimes, in seconds.</summary>
		public static int GrantClockSkewSeconds = 30;

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

		/// <summary>
		/// Response-boundary net (plan section 7.5): scan outgoing models of a PROTECTED department
		/// for values that still carry an envelope and redact them. Defence in depth behind the
		/// per-surface resolve calls, not a replacement for them. Costs one cached protection lookup
		/// for every other department. Operator kill switch if the walk ever proves too expensive on
		/// a hot path.
		/// </summary>
		public static bool EgressScanEnabled = true;

		/// <summary>
		/// Node ceiling for one response scan. A graph larger than this is reported as truncated
		/// rather than walked to the end — a silent cap would read as "nothing found".
		/// </summary>
		public static int EgressScanMaxNodes = 20000;

		/// <summary>Default Protected Data Grant lifetime in minutes when a department has no policy value.</summary>
		public static int StepUpWindowDefaultMinutes = 15;

		/// <summary>Department values above this trigger an administrator warning plus recorded reason.</summary>
		public static int StepUpWarningThresholdMinutes = 60;

		/// <summary>Operator ceiling on StepUpWindowMinutes; departments cannot exceed it.</summary>
		public static int StepUpMaximumMinutes = 480;

		/// <summary>
		/// Seconds before a Protected Data Grant expires at which the web reveal module warns the user and offers
		/// an in-place re-verification, so revealed fields and typed work on an edit page are not lost at expiry.
		/// </summary>
		public static int StepUpExpiryWarningSeconds = 120;

		/// <summary>
		/// ADP migration worker: maximum departments whose night runs in one sweep
		/// (BackOffice-adjustable). Executions are SEQUENTIAL within the sweep — this caps how many
		/// departments a sweep picks up, it does not parallelize them.
		/// </summary>
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
