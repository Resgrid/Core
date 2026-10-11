namespace Resgrid.Model
{
	/// <summary>What the sweep did with one deleted department's leftover rows.</summary>
	public enum DeletedDepartmentPurgeOutcome
	{
		Purged = 0,
		/// <summary>An active Records legal hold keeps the department's data, as it defers a live department's deletion.</summary>
		LegalHold = 1,
		/// <summary>The ADP migration worker holds a live lock on the department; the next sweep retries.</summary>
		ProtectedDataMigration = 2
	}

	/// <summary>
	/// The daily sweep of departments deleted before department deletion purged every table without a foreign key to
	/// Departments (decided 2026-10-11). RMS rows keep their own sweep (worker 43).
	/// </summary>
	public class DeletedDepartmentSweepResult
	{
		public int DepartmentsFound { get; set; }
		public int DepartmentsPurged { get; set; }
		public int DepartmentsHeld { get; set; }
		public int Errors { get; set; }
		public string Message { get; set; }
	}
}
