namespace Resgrid.Model
{
	/// <summary>
	/// Lifecycle status of one wrapped department data encryption key (DEK) version in
	/// DepartmentDataProtectionKeys. New writes always use the single Active version; Retiring versions
	/// remain resolvable for reads until rotation re-encryption completes, after which they become
	/// Retired. Ordinary offboarding never deletes key rows — cryptographic erasure is a separate
	/// dual-controlled retention operation.
	/// </summary>
	public enum DepartmentDataProtectionKeyStatus
	{
		Pending = 0,
		Active = 1,
		Retiring = 2,
		Retired = 3
	}
}
