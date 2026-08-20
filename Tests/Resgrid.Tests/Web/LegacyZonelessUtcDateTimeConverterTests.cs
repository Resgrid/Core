using System;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// TEMPORARY compatibility shim (RG-T132): deployed app builds parse CallResult.LoggedOnUtc as
	/// device-local time and offset "now" to compensate, so the wire value must stay zone-less until
	/// the fixed apps are rolled out. These tests pin that contract: writes carry no "Z", reads accept
	/// both the zone-less and the "Z" form as the same UTC instant. Delete alongside the converter.
	/// </summary>
	[TestFixture]
	public class LegacyZonelessUtcDateTimeConverterTests
	{
		private static readonly DateTime ExpectedUtc = new DateTime(2026, 8, 12, 13, 5, 22, 123, DateTimeKind.Utc);

		[TestCase(DateTimeKind.Unspecified, TestName = "An Unspecified instant serialises zone-less")]
		[TestCase(DateTimeKind.Utc, TestName = "A UTC instant serialises zone-less")]
		public void Serialize_WritesTheInstantWithoutAZoneMarker(DateTimeKind kind)
		{
			// Arrange
			var original = new LegacyTimestampPayload
			{
				Timestamp = DateTime.SpecifyKind(new DateTime(2026, 8, 12, 13, 5, 22, 123), kind)
			};

			// Act
			var serialized = JsonConvert.SerializeObject(original);

			// Assert
			serialized.Should().Contain("2026-08-12T13:05:22.123");
			serialized.Should().NotContain("2026-08-12T13:05:22.123Z");
		}

		[Test]
		public void Serialize_WithALocalInstant_WritesTheUtcWallTimeZoneless()
		{
			// Arrange -- a Local value means the process timezone leaked in somewhere upstream.
			var original = new LegacyTimestampPayload { Timestamp = ExpectedUtc.ToLocalTime() };

			// Act
			var serialized = JsonConvert.SerializeObject(original);

			// Assert
			serialized.Should().Contain("2026-08-12T13:05:22.123");
			serialized.Should().NotContain("2026-08-12T13:05:22.123Z");
		}

		[TestCase("2026-08-12T13:05:22.123", TestName = "Zone-less string reads back as the same UTC instant")]
		[TestCase("2026-08-12T13:05:22.123Z", TestName = "String with Z reads back as the same UTC instant")]
		[TestCase("2026-08-12T06:05:22.123-07:00", TestName = "String with an offset is converted to UTC")]
		public void ReadJson_AcceptsBothFormats(string serialized)
		{
			// Arrange -- DateParseHandling.None forces the reader to hand over a JsonToken.String.
			var settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };

			// Act
			var payload = JsonConvert.DeserializeObject<LegacyTimestampPayload>($"{{\"Timestamp\":\"{serialized}\"}}", settings);

			// Assert
			payload.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
			payload.Timestamp.Should().Be(ExpectedUtc);
		}

		[Test]
		public void SerializeThenDeserialize_PreservesTheInstant()
		{
			// Arrange
			var original = new LegacyTimestampPayload { Timestamp = ExpectedUtc };

			// Act
			var round = JsonConvert.DeserializeObject<LegacyTimestampPayload>(JsonConvert.SerializeObject(original));

			// Assert
			round.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
			round.Timestamp.Should().Be(ExpectedUtc);
		}

		private class LegacyTimestampPayload
		{
			[JsonConverter(typeof(LegacyZonelessUtcDateTimeConverter))]
			public DateTime Timestamp { get; set; }
		}
	}
}
