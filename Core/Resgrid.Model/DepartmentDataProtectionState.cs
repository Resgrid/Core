namespace Resgrid.Model
{
	/// <summary>
	/// Durable data-safety state for a department's Advanced Data Protection (ADP) lifecycle, stored on
	/// DepartmentDataProtectionPolicies.State. This is the ONLY data-safety truth: billing state and the
	/// Security.DepartmentProtectedDataEnrollment feature flag are admission/commercial controls and must
	/// never drive runtime encrypt/decrypt behavior. All transitions from EnrollmentQueued onward are made
	/// by the ADP migration worker inside scheduled windows, never inline in Web/API requests.
	///
	/// Enrollment:  Disabled -> EnrollmentQueued -> ProvisioningKey -> Encrypting -> Verifying -> Enabled
	/// Rotation:    Enabled -> Rotating -> Verifying -> Enabled
	/// Offboarding: Enabled -> OffboardingScheduled -> DisableRequested -> Decrypting -> Verifying -> Disabled
	/// Verifying is shared by enrollment, rotation and offboarding; the direction is carried by
	/// DepartmentDataProtectionPolicy.ActiveMigrationKind (a DepartmentDataProtectionMigrationKind value).
	/// Failures land in the resumable Failed state with the migration cursor intact.
	/// </summary>
	public enum DepartmentDataProtectionState
	{
		Disabled = 0,
		EnrollmentQueued = 1,
		ProvisioningKey = 2,
		Encrypting = 3,
		Verifying = 4,
		Enabled = 5,
		Rotating = 6,
		OffboardingScheduled = 7,
		DisableRequested = 8,
		Decrypting = 9,
		Failed = 10
	}
}
