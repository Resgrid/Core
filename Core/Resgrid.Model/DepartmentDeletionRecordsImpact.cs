namespace Resgrid.Model
{
	/// <summary>
	/// Department deletion purges every Records (RMS) row regardless of retention periods; only an active legal hold
	/// stops it, and the deletion then waits until the hold is released.
	/// </summary>
	public class DepartmentDeletionRecordsImpact
	{
		/// <summary>The department has activated Records, so deletion removes its records, reports and attachments.</summary>
		public bool HasRecords { get; set; }

		/// <summary>Active legal holds; while any exists the deletion stays pending instead of running.</summary>
		public int ActiveLegalHolds { get; set; }
	}
}
