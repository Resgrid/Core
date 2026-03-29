using System;

namespace Resgrid.Model
{
	public class CheckInTimerStatus
	{
		public int TargetType { get; set; }

		public string TargetEntityId { get; set; }

		public string TargetName { get; set; }

		public int? UnitId { get; set; }

		public DateTime? LastCheckIn { get; set; }

		public int DurationMinutes { get; set; }

		public int WarningThresholdMinutes { get; set; }

		public double ElapsedMinutes { get; set; }

		public string Status { get; set; }
	}
}
