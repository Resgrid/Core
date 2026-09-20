namespace Resgrid.Config
{
	/// <summary>
	/// Protected workforce pay data, field costing and California pay data reporting (Workforce &amp; Business Operations
	/// plan, Phase E). Environment keys: RESGRID:WorkforceConfig:DailyOvertimeThresholdHours, :UsageConflictTolerancePercent,
	/// :ExportArtifactRetentionDays, :FilingSeasonStartMonth, :FilingSeasonEndMonth, :ReadinessReminderEnabled.
	/// </summary>
	public static class WorkforceConfig
	{
		/// <summary>Deployment time-report hours per person per day above this count are priced at the Overtime pay code in a cost run (an estimate — the payroll system's approved cost wins when supplied).</summary>
		public static decimal DailyOvertimeThresholdHours = 8m;

		/// <summary>An automatic (GPS / tracker) and a manual usage reading for the same unit, date and context that differ by more than this percentage are queued for review.</summary>
		public static decimal UsageConflictTolerancePercent = 10m;

		/// <summary>Days a CRD export artifact stays downloadable before worker 49 purges its bytes (the run, its snapshots and rows stay).</summary>
		public static int ExportArtifactRetentionDays = 30;

		/// <summary>California pay data filing season (inclusive months) during which worker 49 sends the value-free readiness reminder.</summary>
		public static int FilingSeasonStartMonth = 1;
		public static int FilingSeasonEndMonth = 5;

		/// <summary>Master switch for worker 49.</summary>
		public static bool ReadinessReminderEnabled = true;
	}
}
