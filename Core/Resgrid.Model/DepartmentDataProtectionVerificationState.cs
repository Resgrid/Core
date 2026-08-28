namespace Resgrid.Model
{
	/// <summary>
	/// Verification progress for one DepartmentDataProtectionMigrations row. Only a Passed verification
	/// (counts, AEAD/AAD spot checks, catalog coverage, plaintext-residue scan for enrollment or
	/// envelope-residue scan for offboarding) lets the worker transition the department out of
	/// Verifying.
	/// </summary>
	public enum DepartmentDataProtectionVerificationState
	{
		NotStarted = 0,
		InProgress = 1,
		Passed = 2,
		Failed = 3
	}
}
