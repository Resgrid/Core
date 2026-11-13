using System;
using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Personnel participation and compliance analytics: per-member call response rate, event/training
	/// attendance, and certification expiry status. Especially relevant to volunteer departments.
	/// </summary>
	/// <remarks>
	/// Cached through <see cref="Providers.ICacheProvider"/> (protobuf-net), so every type in the graph is a
	/// [ProtoContract]. Dates use <see cref="DataFormat.WellKnown"/> so a cache hit keeps <see cref="DateTimeKind.Utc"/>.
	/// </remarks>
	[ProtoContract]
	public class ParticipationReport
	{
		[ProtoMember(1)]
		public int? DepartmentId { get; set; }

		[ProtoMember(2, DataFormat = DataFormat.WellKnown)]
		public DateTime StartUtc { get; set; }

		[ProtoMember(3, DataFormat = DataFormat.WellKnown)]
		public DateTime EndUtc { get; set; }

		[ProtoMember(4, DataFormat = DataFormat.WellKnown)]
		public DateTime GeneratedUtc { get; set; }

		/// <summary>Number of calls in the window used as the denominator for response rates.</summary>
		[ProtoMember(5)]
		public long CallsInWindow { get; set; }

		[ProtoMember(6)]
		public List<MemberParticipation> Members { get; set; } = new List<MemberParticipation>();

		/// <summary>Count of members with at least one expired certification.</summary>
		[ProtoMember(7)]
		public int MembersWithExpiredCertifications { get; set; }
	}

	[ProtoContract]
	public class MemberParticipation
	{
		[ProtoMember(1)]
		public string UserId { get; set; }

		[ProtoMember(2)]
		public string Name { get; set; }

		[ProtoMember(3)]
		public long CallsResponded { get; set; }

		/// <summary>CallsResponded / calls in window (0..1).</summary>
		[ProtoMember(4)]
		public double ResponseRate { get; set; }

		[ProtoMember(5)]
		public long EventsAttended { get; set; }

		[ProtoMember(6)]
		public long TrainingAttended { get; set; }

		[ProtoMember(7)]
		public int ExpiredCertifications { get; set; }

		[ProtoMember(8)]
		public int ExpiringCertifications { get; set; }
	}
}
