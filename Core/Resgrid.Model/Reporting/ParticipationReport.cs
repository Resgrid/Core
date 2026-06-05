using System;
using System.Collections.Generic;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Personnel participation and compliance analytics: per-member call response rate, event/training
	/// attendance, and certification expiry status. Especially relevant to volunteer departments.
	/// </summary>
	public class ParticipationReport
	{
		public int? DepartmentId { get; set; }
		public DateTime StartUtc { get; set; }
		public DateTime EndUtc { get; set; }
		public DateTime GeneratedUtc { get; set; }

		/// <summary>Number of calls in the window used as the denominator for response rates.</summary>
		public long CallsInWindow { get; set; }

		public List<MemberParticipation> Members { get; set; } = new List<MemberParticipation>();

		/// <summary>Count of members with at least one expired certification.</summary>
		public int MembersWithExpiredCertifications { get; set; }
	}

	public class MemberParticipation
	{
		public string UserId { get; set; }
		public string Name { get; set; }

		public long CallsResponded { get; set; }

		/// <summary>CallsResponded / calls in window (0..1).</summary>
		public double ResponseRate { get; set; }

		public long EventsAttended { get; set; }
		public long TrainingAttended { get; set; }

		public int ExpiredCertifications { get; set; }
		public int ExpiringCertifications { get; set; }
	}
}
