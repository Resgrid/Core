using System;

namespace Resgrid.Model.Tracking
{
	public sealed class CanonicalTrackingPosition
	{
		public string EventId { get; set; }
		public DateTime TimestampUtc { get; set; }
		public DateTime ReceivedOnUtc { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public decimal? AccuracyMeters { get; set; }
		public decimal? AltitudeMeters { get; set; }
		public decimal? SpeedMetersPerSecond { get; set; }
		public decimal? HeadingDegrees { get; set; }
		public int? Satellites { get; set; }
		public decimal? Hdop { get; set; }
		public decimal? BatteryPercent { get; set; }
		public decimal? ExternalPowerVolts { get; set; }
		public int? SignalPercent { get; set; }
		public bool? Ignition { get; set; }
		public bool? IsMoving { get; set; }
		public string AlarmCode { get; set; }
		public TrackingTimestampSource TimestampSource { get; set; }
		public bool IsValidFix { get; set; }
	}
}
