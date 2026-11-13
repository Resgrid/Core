using System;
using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Unit Hour Utilization (UHU) and workload analytics: how much of each unit's in-service time was
	/// spent committed vs. available, plus busiest hours and resource-exhaustion windows.
	/// </summary>
	/// <remarks>
	/// Cached through <see cref="Providers.ICacheProvider"/> (protobuf-net), so every type in the graph is a
	/// [ProtoContract]. Dates use <see cref="DataFormat.WellKnown"/> so a cache hit keeps <see cref="DateTimeKind.Utc"/>.
	/// </remarks>
	[ProtoContract]
	public class UtilizationReport
	{
		[ProtoMember(1)]
		public int? DepartmentId { get; set; }

		[ProtoMember(2, DataFormat = DataFormat.WellKnown)]
		public DateTime StartUtc { get; set; }

		[ProtoMember(3, DataFormat = DataFormat.WellKnown)]
		public DateTime EndUtc { get; set; }

		[ProtoMember(4, DataFormat = DataFormat.WellKnown)]
		public DateTime GeneratedUtc { get; set; }

		/// <summary>Committed-hours / total-hours across all units in the window (0..1).</summary>
		[ProtoMember(5)]
		public double AggregateUhu { get; set; }

		/// <summary>Count of distinct intervals in the window where zero units were available.</summary>
		[ProtoMember(6)]
		public long ZeroUnitsAvailableEvents { get; set; }

		/// <summary>Peak number of simultaneously active calls observed in the window.</summary>
		[ProtoMember(7)]
		public int PeakConcurrentCalls { get; set; }

		[ProtoMember(8)]
		public List<UnitUtilization> Units { get; set; } = new List<UnitUtilization>();

		/// <summary>Call/workload distribution by hour-of-day (0..23, UTC), dense.</summary>
		[ProtoMember(9)]
		public List<MetricPoint> WorkloadByHour { get; set; } = new List<MetricPoint>();
	}

	[ProtoContract]
	public class UnitUtilization
	{
		[ProtoMember(1)]
		public int UnitId { get; set; }

		[ProtoMember(2)]
		public string UnitName { get; set; }

		[ProtoMember(3)]
		public double BusySeconds { get; set; }

		[ProtoMember(4)]
		public double InServiceSeconds { get; set; }

		/// <summary>BusySeconds / InServiceSeconds (0..1).</summary>
		[ProtoMember(5)]
		public double Uhu { get; set; }

		[ProtoMember(6)]
		public long CallCount { get; set; }
	}
}
