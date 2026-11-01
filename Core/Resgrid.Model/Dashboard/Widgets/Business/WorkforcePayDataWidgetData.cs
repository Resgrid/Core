using System;

namespace Resgrid.Model.Dashboard.Widgets.Business
{
	/// <summary>
	/// workforce.payData (plan §7.9): California pay data reporting completeness for the reporting year (last calendar year in the
	/// department's time zone, as the Pay Data page defaults). Counts only — never a demographic response.
	/// </summary>
	public sealed class WorkforcePayDataWidgetData
	{
		public int ReportingYear { get; set; }

		public int ActiveWorkers { get; set; }

		/// <summary>Workers with a demographic response (self-identified, declined or observer perception).</summary>
		public int WithResponse { get; set; }

		public int Missing { get; set; }

		/// <summary>WithResponse / ActiveWorkers, 0–100; 100 when there are no active workers.</summary>
		public int CompletenessPercent { get; set; }

		public DateTime? DueDate { get; set; }

		public bool ProfileAvailable { get; set; }

		public int OpenRuns { get; set; }

		public int UnresolvedExceptions { get; set; }

		public int AnnualFactsMissing { get; set; }

		public bool HasFrozenRun { get; set; }

		public bool HasCertifiedRun { get; set; }
	}
}
