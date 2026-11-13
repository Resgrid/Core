using ProtoBuf;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Scalar totals for a dashboard report. "InWindow" values are scoped to the requested
	/// [startUtc, endUtc] range; "AllTime" values are scoped only by department (or system-wide).
	/// Personnel/Unit availability counts reflect each resource's current (latest) status mapped to
	/// a canonical <see cref="AvailabilityClass"/> via <see cref="AvailabilityMatrix"/>.
	/// </summary>
	[ProtoContract]
	public class ReportTotals
	{
		// Calls
		[ProtoMember(1)]
		public long CallsAllTime { get; set; }

		[ProtoMember(2)]
		public long CallsInWindow { get; set; }

		[ProtoMember(3)]
		public long ActiveCalls { get; set; }

		// Messages
		[ProtoMember(4)]
		public long MessagesInWindow { get; set; }

		// Personnel (current availability)
		[ProtoMember(5)]
		public int PersonnelTotal { get; set; }

		[ProtoMember(6)]
		public int PersonnelAvailable { get; set; }

		[ProtoMember(7)]
		public int PersonnelCommitted { get; set; }

		[ProtoMember(8)]
		public int PersonnelUnavailable { get; set; }

		// Units (current availability)
		[ProtoMember(9)]
		public int UnitsTotal { get; set; }

		[ProtoMember(10)]
		public int UnitsAvailable { get; set; }

		[ProtoMember(11)]
		public int UnitsCommitted { get; set; }

		[ProtoMember(12)]
		public int UnitsUnavailable { get; set; }

		// Users
		[ProtoMember(13)]
		public long NewUsersInWindow { get; set; }

		// System-wide only (null for a department-scoped report)
		[ProtoMember(14)]
		public long? DepartmentsTotal { get; set; }

		[ProtoMember(15)]
		public long? NewDepartmentsInWindow { get; set; }
	}
}
