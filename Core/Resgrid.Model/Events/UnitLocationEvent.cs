using ProtoBuf;
using System;

namespace Resgrid.Model.Events
{
	[ProtoContract]
	public class UnitLocationEvent
	{
		[ProtoMember(1)]
		public string EventId { get; set; }

		[ProtoMember(2)]
		public int UnitLocationId { get; set; }

		[ProtoMember(3)]
		public int DepartmentId { get; set; }

		[ProtoMember(4)]
		public int UnitId { get; set; }

		[ProtoMember(5)]
		public DateTime Timestamp { get; set; }

		[ProtoMember(6)]
		public decimal? Latitude { get; set; }

		[ProtoMember(7)]
		public decimal? Longitude { get; set; }

		[ProtoMember(8)]
		public decimal? Accuracy { get; set; }

		[ProtoMember(9)]
		public decimal? Altitude { get; set; }

		[ProtoMember(10)]
		public decimal? AltitudeAccuracy { get; set; }

		[ProtoMember(11)]
		public decimal? Speed { get; set; }

		[ProtoMember(12)]
		public decimal? Heading { get; set; }

		[ProtoMember(13)]
		public DateTime? ReceivedOn { get; set; }

		[ProtoMember(14)]
		public int SourceType { get; set; }

		[ProtoMember(15)]
		public string SourceId { get; set; }

		[ProtoMember(16)]
		public int SourcePriority { get; set; }

		[ProtoMember(17)]
		public int? TransportType { get; set; }

		[ProtoMember(18)]
		public string ProtocolKey { get; set; }

		[ProtoMember(19)]
		public bool? IsValidFix { get; set; }

		[ProtoMember(20)]
		public int? Satellites { get; set; }

		[ProtoMember(21)]
		public decimal? Hdop { get; set; }

		[ProtoMember(22)]
		public decimal? BatteryPercent { get; set; }

		[ProtoMember(23)]
		public decimal? ExternalPowerVolts { get; set; }

		[ProtoMember(24)]
		public int? SignalPercent { get; set; }

		[ProtoMember(25)]
		public bool? Ignition { get; set; }

		[ProtoMember(26)]
		public bool? IsMoving { get; set; }

		[ProtoMember(27)]
		public string AlarmCode { get; set; }

		[ProtoMember(28)]
		public int? TimestampSource { get; set; }

		public UnitLocationEvent()
		{
			EventId = Guid.NewGuid().ToString();
		}
	}
}
