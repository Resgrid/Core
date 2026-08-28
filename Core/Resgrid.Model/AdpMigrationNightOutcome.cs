namespace Resgrid.Model
{
	/// <summary>
	/// How one nightly ADP migration window ended for a department.
	/// </summary>
	public enum AdpMigrationNightOutcome
	{
		/// <summary>Every cataloged table's cursor reached the end; the run is ready for verification.</summary>
		CompletedAllTables = 1,

		/// <summary>The window closed first; cursors are checkpointed and work resumes next night.</summary>
		WindowClosed = 2,

		/// <summary>Unrecoverable batch error; the run is Failed at its last durable cursor.</summary>
		Failed = 3
	}
}
