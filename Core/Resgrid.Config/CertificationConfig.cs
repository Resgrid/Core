namespace Resgrid.Config
{
	/// <summary>
	/// Certification expiry worker (Workforce &amp; Business Operations plan, Phase D5; worker 34). Environment keys:
	/// RESGRID:CertificationConfig:SweepEnabled, :SweepLocalHour, :MaxLeadDays.
	/// </summary>
	public static class CertificationConfig
	{
		/// <summary>Master switch for the nightly sweep (expire, expiring, unit, enforcement and digest passes).</summary>
		public static bool SweepEnabled = true;

		/// <summary>Department-local hour (0-23) at which the worker's hourly tick runs a department's sweep.</summary>
		public static int SweepLocalHour = 6;

		/// <summary>Largest lead day a department may configure; keeps the expiring read bounded.</summary>
		public static int MaxLeadDays = 365;
	}
}
