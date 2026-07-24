using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Web.Services.Models.v4.UnitTracking
{
	public sealed class UnitTrackingPositionInput
	{
		[JsonProperty("eventId")]
		public string EventId { get; set; }

		[JsonProperty("timestamp")]
		public JToken Timestamp { get; set; }

		[JsonProperty("latitude")]
		public decimal? Latitude { get; set; }

		[JsonProperty("longitude")]
		public decimal? Longitude { get; set; }

		[JsonProperty("accuracyMeters")]
		public decimal? AccuracyMeters { get; set; }

		[JsonProperty("altitudeMeters")]
		public decimal? AltitudeMeters { get; set; }

		[JsonProperty("speedMetersPerSecond")]
		public decimal? SpeedMetersPerSecond { get; set; }

		[JsonProperty("headingDegrees")]
		public decimal? HeadingDegrees { get; set; }

		[JsonProperty("satellites")]
		public int? Satellites { get; set; }

		[JsonProperty("hdop")]
		public decimal? Hdop { get; set; }

		[JsonProperty("batteryPercent")]
		public decimal? BatteryPercent { get; set; }

		[JsonProperty("externalPowerVolts")]
		public decimal? ExternalPowerVolts { get; set; }

		[JsonProperty("signalPercent")]
		public int? SignalPercent { get; set; }

		[JsonProperty("ignition")]
		public bool? Ignition { get; set; }

		[JsonProperty("moving")]
		public bool? Moving { get; set; }

		[JsonProperty("alarmCode")]
		public string AlarmCode { get; set; }

		[JsonProperty("validFix")]
		public bool? ValidFix { get; set; }

		[JsonProperty("deviceIdentifier")]
		public string DeviceIdentifier { get; set; }
	}

	public sealed class UnitTrackingPositionsInput
	{
		[JsonProperty("deviceIdentifier")]
		public string DeviceIdentifier { get; set; }

		[JsonProperty("positions")]
		public UnitTrackingPositionInput[] Positions { get; set; }
	}
}
