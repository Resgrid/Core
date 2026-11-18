namespace Resgrid.Config
{
	/// <summary>
	/// System-wide Records retention knobs that are not a department's choice (setting 74 holds the department retention
	/// policy). Environment keys: RESGRID:RecordsRetentionConfig:DeletedIncidentReportPurgeDays.
	/// </summary>
	public static class RecordsRetentionConfig
	{
		/// <summary>
		/// Days a deleted incident report keeps its content before worker 43 erases it (leaving the content-free tombstone a
		/// retention purge leaves). The window lets support recover a mistaken delete and lets a legal hold placed after the
		/// delete stop the erasure. Values below 1 are treated as 1.
		/// </summary>
		public static int DeletedIncidentReportPurgeDays = 30;
	}
}
