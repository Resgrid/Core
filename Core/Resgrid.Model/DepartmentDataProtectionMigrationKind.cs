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
		Rotation = 2,

		/// <summary>
		/// A catalog upgrade: the code's protected-field catalog has advanced past the version this
		/// department was migrated to, so the fields added since are still plaintext. The sweep
		/// encrypts ONLY those fields and then stamps the department's new catalog version.
		/// Existing envelopes are untouched — the catalog version is not an AAD component.
		/// </summary>
		CatalogUpgrade = 3
	}
}
