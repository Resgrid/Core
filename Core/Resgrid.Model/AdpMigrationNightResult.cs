namespace Resgrid.Model
{
	/// <summary>
	/// Result of one department's nightly migration window. Error codes are value-free machine codes
	/// (never exception text or content) — they land in DepartmentDataProtectionMigrations.LastErrorCode,
	/// notifications, and the BackOffice attention view.
	/// </summary>
	public sealed class AdpMigrationNightResult
	{
		public AdpMigrationNightOutcome Outcome { get; set; }

		/// <summary>Value-free error code when Outcome is Failed.</summary>
		public string ErrorCode { get; set; }

		/// <summary>Rows processed across all tables this night (progress reporting only).</summary>
		public long RowsProcessed { get; set; }

		/// <summary>Percent complete across the whole run after this night, 0-100, when known.</summary>
		public int? PercentComplete { get; set; }

		public static AdpMigrationNightResult Completed(long rowsProcessed = 0) => new AdpMigrationNightResult
		{
			Outcome = AdpMigrationNightOutcome.CompletedAllTables,
			RowsProcessed = rowsProcessed,
			PercentComplete = 100
		};

		public static AdpMigrationNightResult WindowClosed(long rowsProcessed, int? percentComplete) => new AdpMigrationNightResult
		{
			Outcome = AdpMigrationNightOutcome.WindowClosed,
			RowsProcessed = rowsProcessed,
			PercentComplete = percentComplete
		};

		public static AdpMigrationNightResult Failed(string errorCode) => new AdpMigrationNightResult
		{
			Outcome = AdpMigrationNightOutcome.Failed,
			ErrorCode = errorCode
		};
	}
}
