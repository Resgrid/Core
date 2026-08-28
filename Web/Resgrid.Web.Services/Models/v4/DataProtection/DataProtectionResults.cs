namespace Resgrid.Web.Services.Models.v4.DataProtection
{
	/// <summary>
	/// ADP capability report for the calling department (ADP plan sections 7.1 and 12). Contains NO
	/// protected values. Flag/addon eligibility here is ADVISORY — every enrollment command re-checks
	/// server-side; the durable state and the admission gate are never collapsed into one boolean.
	/// </summary>
	public class DataProtectionCapabilitiesData
	{
		/// <summary>DepartmentDataProtectionState numeric value.</summary>
		public int State { get; set; }

		/// <summary>DepartmentDataProtectionState name for display/debugging.</summary>
		public string StateName { get; set; }

		/// <summary>True when protection is enforced for reads (Enabled/Rotating/OffboardingScheduled).</summary>
		public bool IsProtectionEnabled { get; set; }

		/// <summary>Advisory: global admission gate currently open AND the department could start enrollment.</summary>
		public bool IsEnrollmentAvailable { get; set; }

		/// <summary>Advisory: caller is the managing member and state permits an enroll command.</summary>
		public bool CanEnable { get; set; }

		/// <summary>Advisory: caller is the managing member and state permits cancel/offboarding control.</summary>
		public bool CanDisable { get; set; }

		/// <summary>Re-enabling after a completed opt-out always requires a fresh purchase and open gate.</summary>
		public bool ReenableRequiresFeatureFlag { get; set; }

		/// <summary>Catalog version this department's migration has verified (0 = none).</summary>
		public int CatalogVersion { get; set; }

		/// <summary>Current platform catalog version.</summary>
		public int CurrentCatalogVersion { get; set; }

		/// <summary>Department policy epoch; clients discard grants/state from older epochs.</summary>
		public long PolicyEpoch { get; set; }

		/// <summary>Effective Protected Data Grant lifetime in minutes.</summary>
		public int StepUpWindowMinutes { get; set; }

		/// <summary>Scheduled offboarding instant (ISO 8601), when state is OffboardingScheduled.</summary>
		public string OffboardingEffectiveOn { get; set; }

		/// <summary>Per-channel egress modes (ProtectedDataEgressMode numeric values).</summary>
		public int PushEgressMode { get; set; }
		public int EmailEgressMode { get; set; }
		public int SmsEgressMode { get; set; }
		public int VoiceEgressMode { get; set; }

		/// <summary>True while a department operation lock is active (mutations refused with 423).</summary>
		public bool IsDepartmentLocked { get; set; }

		/// <summary>Value-free lock banner reason, when locked.</summary>
		public string LockReason { get; set; }

		/// <summary>Projected lock end (ISO 8601), when locked and known.</summary>
		public string LockProjectedEndUtc { get; set; }
	}

	public class DataProtectionCapabilitiesResult : StandardApiResponseV4Base
	{
		public DataProtectionCapabilitiesData Data { get; set; } = new DataProtectionCapabilitiesData();
	}

	public class EnrollmentCommandResult : StandardApiResponseV4Base
	{
		/// <summary>DepartmentDataProtectionEnrollmentResult name.</summary>
		public string Outcome { get; set; }

		/// <summary>Resulting DepartmentDataProtectionState numeric value.</summary>
		public int State { get; set; }
	}

	/// <summary>
	/// Result of a successful step-up verification. The window is ABSOLUTE (never sliding): clients
	/// conceal protected values at StepUpExpiresOnUtc and prompt again on the next reveal/edit.
	/// When grant signing is configured on this deployment, GrantId/GrantToken carry a signed
	/// Protected Data Grant the client presents alongside its access token on protected operations;
	/// clients hold the token in MEMORY ONLY (never persisted) and discard it at expiry. On
	/// deployments without signing key material both stay null and the verification itself remains
	/// the capability (pre-broker behavior).
	/// </summary>
	public class StepUpResult : StandardApiResponseV4Base
	{
		/// <summary>Unique grant id (jti) for display/audit correlation; null when grants are not configured.</summary>
		public string GrantId { get; set; }

		/// <summary>Signed Protected Data Grant token; null when grants are not configured. MEMORY ONLY.</summary>
		public string GrantToken { get; set; }

		/// <summary>Absolute UTC expiry of this step-up window (ISO 8601).</summary>
		public string StepUpExpiresOnUtc { get; set; }

		/// <summary>The department's effective step-up window in minutes.</summary>
		public int StepUpWindowMinutes { get; set; }
	}
}
