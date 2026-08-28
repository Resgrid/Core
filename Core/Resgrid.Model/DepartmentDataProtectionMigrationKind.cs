namespace Resgrid.Model
{
	/// <summary>
	/// Direction/purpose of a bulk ADP migration run recorded in DepartmentDataProtectionMigrations.
	/// Enrollment encrypts plaintext into rgdp envelopes, Offboarding decrypts envelopes back to
	/// plaintext, Rotation re-encrypts under a new department key version. All three share the same
	/// cursor, checkpoint and idempotency machinery.
	/// </summary>
	public enum DepartmentDataProtectionMigrationKind
	{
		Enrollment = 0,
		Offboarding = 1,
		Rotation = 2
	}
}
