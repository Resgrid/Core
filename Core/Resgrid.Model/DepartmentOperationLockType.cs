namespace Resgrid.Model
{
	/// <summary>
	/// Reason class for a DepartmentOperationLocks row. Introduced for ADP bulk migrations but designed
	/// as a general mechanism; add new members rather than overloading AdpMigration. Values are
	/// persisted and append-only (Identifier Allocation Registry).
	/// </summary>
	public enum DepartmentOperationLockType
	{
		AdpMigration = 1,

		/// <summary>
		/// Held by a Department Import commit or rollback run (wizard plan section 11.7). It keeps
		/// ADP migrations, department deletion and a second import run out, but it does NOT freeze
		/// the department: a live department keeps working while an import commits.
		/// </summary>
		DepartmentImport = 2
	}

	public static class DepartmentOperationLockTypeExtensions
	{
		/// <summary>
		/// True when an active lock of this type refuses department mutations (the 423 filters, the
		/// worker DepartmentLockGuard and the chatbot gate). Unknown values block, which preserves the
		/// original "every lock freezes writes" behaviour for any type added without a decision here.
		/// </summary>
		public static bool BlocksDepartmentWrites(this DepartmentOperationLockType lockType)
		{
			switch (lockType)
			{
				case DepartmentOperationLockType.DepartmentImport:
					return false;
				default:
					return true;
			}
		}

		/// <summary>Persisted-value overload of <see cref="BlocksDepartmentWrites(DepartmentOperationLockType)"/>.</summary>
		public static bool BlocksDepartmentWrites(int lockType)
		{
			return ((DepartmentOperationLockType)lockType).BlocksDepartmentWrites();
		}
	}
}
