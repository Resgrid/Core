namespace Resgrid.Model
{
	/// <summary>
	/// Reason class for a DepartmentOperationLocks row. Introduced for ADP bulk migrations but designed
	/// as a general mechanism; add new members rather than overloading AdpMigration.
	/// </summary>
	public enum DepartmentOperationLockType
	{
		AdpMigration = 1
	}
}
