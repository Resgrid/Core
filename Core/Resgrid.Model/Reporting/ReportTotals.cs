namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Scalar totals for a dashboard report. "InWindow" values are scoped to the requested
	/// [startUtc, endUtc] range; "AllTime" values are scoped only by department (or system-wide).
	/// Personnel/Unit availability counts reflect each resource's current (latest) status mapped to
	/// a canonical <see cref="AvailabilityClass"/> via <see cref="AvailabilityMatrix"/>.
	/// </summary>
	public class ReportTotals
	{
		// Calls
		public long CallsAllTime { get; set; }
		public long CallsInWindow { get; set; }
		public long ActiveCalls { get; set; }

		// Messages
		public long MessagesInWindow { get; set; }

		// Personnel (current availability)
		public int PersonnelTotal { get; set; }
		public int PersonnelAvailable { get; set; }
		public int PersonnelCommitted { get; set; }
		public int PersonnelUnavailable { get; set; }

		// Units (current availability)
		public int UnitsTotal { get; set; }
		public int UnitsAvailable { get; set; }
		public int UnitsCommitted { get; set; }
		public int UnitsUnavailable { get; set; }

		// Users
		public long NewUsersInWindow { get; set; }

		// System-wide only (null for a department-scoped report)
		public long? DepartmentsTotal { get; set; }
		public long? NewDepartmentsInWindow { get; set; }
	}
}
