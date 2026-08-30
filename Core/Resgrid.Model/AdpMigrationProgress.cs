namespace Resgrid.Model
{
	/// <summary>
	/// What the enrollment wizard's status panel shows while a department is being encrypted,
	/// decrypted or rotated (plan 18). Value-free by construction: row counts and table NAMES only,
	/// never a value out of any of them.
	///
	/// The numbers come from the same cursor rows the migration engine writes, so the panel and the
	/// worker agree by construction rather than by a second estimate.
	/// </summary>
	public class AdpMigrationProgress
	{
		/// <summary>True when a run of any kind currently has cursor rows in flight.</summary>
		public bool IsRunning { get; set; }

		/// <summary>The kind of run in flight (enrollment, offboarding, rotation, catalog upgrade).</summary>
		public DepartmentDataProtectionMigrationKind Kind { get; set; }

		public string KindName => Kind.ToString();

		/// <summary>
		/// Rows done over rows known, 0-100, or null when nothing has been counted yet. Tables the
		/// run has not opened yet have no cursor row, so early in a run this UNDERSTATES progress —
		/// it is the same figure the engine reports, deliberately, so the two never disagree.
		/// </summary>
		public int? PercentComplete { get; set; }

		public long RowsTotal { get; set; }

		/// <summary>Rows the run has written plus rows it found already protected.</summary>
		public long RowsCompleted { get; set; }

		/// <summary>
		/// Rows that could not be handled (a foreign envelope, a decrypt that did not
		/// authenticate). Non-zero means the run halted or needs an operator.
		/// </summary>
		public long RowsAnomalous { get; set; }

		/// <summary>How many tables the run has opened a cursor for.</summary>
		public int TablesStarted { get; set; }

		/// <summary>The table with the least-finished cursor — what the run is working through.</summary>
		public string CurrentTable { get; set; }
	}
}
