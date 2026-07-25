using System;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Resgrid.Model.Repositories;

namespace Resgrid.Model
{
	[BsonCollection("unitLocations")]
	public class UnitsLocation : NoSqlDocument
	{
		[BsonElement("eventId")]
		[BsonIgnoreIfNull]
		public string EventId { get; set; }

		[BsonElement("departmentId")]
		public int DepartmentId { get; set; }

		[Required]
		[BsonElement("unitId")]
		public int UnitId { get; set; }

		[BsonElement("timestamp")]
		public DateTime Timestamp { get; set; }

		[BsonElement("receivedOn")]
		public DateTime? ReceivedOn { get; set; }

		[BsonElement("sourceType")]
		public int SourceType { get; set; }

		[BsonElement("sourceId")]
		public string SourceId { get; set; }

		[BsonElement("sourcePriority")]
		public int SourcePriority { get; set; }

		[BsonElement("transportType")]
		public int? TransportType { get; set; }

		[BsonElement("protocolKey")]
		public string ProtocolKey { get; set; }

		[BsonElement("isValidFix")]
		public bool? IsValidFix { get; set; }

		[BsonElement("latitude")]
		public decimal Latitude { get; set; }

		[BsonElement("longitude")]
		public decimal Longitude { get; set; }

		[BsonElement("accuracy")]
		public decimal? Accuracy { get; set; }

		[BsonElement("altitude")]
		public decimal? Altitude { get; set; }

		[BsonElement("altitudeAccuracy")]
		public decimal? AltitudeAccuracy { get; set; }

		[BsonElement("speed")]
		public decimal? Speed { get; set; }

		[BsonElement("heading")]
		public decimal? Heading { get; set; }

		[BsonElement("satellites")]
		public int? Satellites { get; set; }

		[BsonElement("hdop")]
		public decimal? Hdop { get; set; }

		[BsonElement("batteryPercent")]
		public decimal? BatteryPercent { get; set; }

		[BsonElement("externalPowerVolts")]
		public decimal? ExternalPowerVolts { get; set; }

		[BsonElement("signalPercent")]
		public int? SignalPercent { get; set; }

		[BsonElement("ignition")]
		public bool? Ignition { get; set; }

		[BsonElement("isMoving")]
		public bool? IsMoving { get; set; }

		[BsonElement("alarmCode")]
		public string AlarmCode { get; set; }

		[BsonElement("timestampSource")]
		public int? TimestampSource { get; set; }

		[BsonIgnore()]
		public string PgId { get; set; }

		public string GetId()
		{
			if (!String.IsNullOrWhiteSpace(PgId))
				return PgId;

			return Id.ToString();
		}
	}
}
