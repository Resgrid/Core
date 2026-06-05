using System;
using System.Collections.Generic;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Unit Hour Utilization (UHU) and workload analytics: how much of each unit's in-service time was
	/// spent committed vs. available, plus busiest hours and resource-exhaustion windows.
	/// </summary>
	public class UtilizationReport
	{
		public int? DepartmentId { get; set; }
		public DateTime StartUtc { get; set; }
		public DateTime EndUtc { get; set; }
		public DateTime GeneratedUtc { get; set; }

		/// <summary>Committed-hours / total-hours across all units in the window (0..1).</summary>
		public double AggregateUhu { get; set; }

		/// <summary>Count of distinct intervals in the window where zero units were available.</summary>
		public long ZeroUnitsAvailableEvents { get; set; }

		/// <summary>Peak number of simultaneously active calls observed in the window.</summary>
		public int PeakConcurrentCalls { get; set; }

		public List<UnitUtilization> Units { get; set; } = new List<UnitUtilization>();

		/// <summary>Call/workload distribution by hour-of-day (0..23, UTC), dense.</summary>
		public List<MetricPoint> WorkloadByHour { get; set; } = new List<MetricPoint>();
	}

	public class UnitUtilization
	{
		public int UnitId { get; set; }
		public string UnitName { get; set; }
		public double BusySeconds { get; set; }
		public double InServiceSeconds { get; set; }

		/// <summary>BusySeconds / InServiceSeconds (0..1).</summary>
		public double Uhu { get; set; }

		public long CallCount { get; set; }
	}
}
