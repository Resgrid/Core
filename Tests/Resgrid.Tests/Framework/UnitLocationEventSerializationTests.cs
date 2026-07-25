using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using ProtoBuf;
using Resgrid.Framework;
using Resgrid.Model.Events;

namespace Resgrid.Tests.Framework
{
	[TestFixture]
	public class UnitLocationEventSerializationTests
	{
		[Test]
		public void Deserialize_should_preserve_legacy_members_and_default_new_members()
		{
			var timestamp = new DateTime(2026, 7, 24, 12, 30, 0, DateTimeKind.Utc);
			var legacyEvent = new LegacyUnitLocationEvent
			{
				EventId = "legacy-event",
				DepartmentId = 7,
				UnitId = 12,
				Timestamp = timestamp,
				Latitude = 39.7392m,
				Longitude = -104.9903m,
				Speed = 15.5m,
				Heading = 270m
			};
			using var stream = new MemoryStream();
			Serializer.Serialize(stream, legacyEvent);
			stream.Position = 0;

			var result = Serializer.Deserialize<UnitLocationEvent>(stream);

			result.EventId.Should().Be("legacy-event");
			result.DepartmentId.Should().Be(7);
			result.UnitId.Should().Be(12);
			result.Timestamp.Should().Be(timestamp);
			result.Latitude.Should().Be(39.7392m);
			result.Longitude.Should().Be(-104.9903m);
			result.Speed.Should().Be(15.5m);
			result.Heading.Should().Be(270m);
			result.ReceivedOn.Should().BeNull();
			result.IsValidFix.Should().BeNull();
			result.SourceType.Should().Be(0);
			result.SourcePriority.Should().Be(0);
		}

		[Test]
		public void Serialize_should_round_trip_tracking_members()
		{
			var receivedOn = new DateTime(2026, 7, 24, 12, 30, 5, DateTimeKind.Utc);
			var unitLocationEvent = new UnitLocationEvent
			{
				EventId = "tracking-event",
				DepartmentId = 7,
				UnitId = 12,
				Timestamp = receivedOn.AddSeconds(-5),
				ReceivedOn = receivedOn,
				SourceType = 2,
				SourceId = "binding-1",
				SourcePriority = 100,
				TransportType = 3,
				ProtocolKey = "teltonika-codec8",
				IsValidFix = true,
				Latitude = 39.7392m,
				Longitude = -104.9903m,
				Satellites = 12,
				Hdop = 0.8m,
				BatteryPercent = 88m,
				ExternalPowerVolts = 13.6m,
				SignalPercent = 75,
				Ignition = true,
				IsMoving = true,
				AlarmCode = "sos",
				TimestampSource = 1
			};

			var serialized = ObjectSerialization.Serialize(unitLocationEvent);
			var result = ObjectSerialization.Deserialize<UnitLocationEvent>(serialized);

			result.Should().BeEquivalentTo(unitLocationEvent);
		}

		[ProtoContract]
		public class LegacyUnitLocationEvent
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
		}
	}
}
