using System;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// The converter has to produce the same UTC instant no matter which token the reader hands it
	/// (String when date parsing is off, Date when it is on) and no matter what Kind the reader
	/// inferred. These run green on a server in any timezone -- that is the point of them.
	/// </summary>
	[TestFixture]
	public class UtcDateTimeConverterTests
	{
		private static readonly DateTime ExpectedUtc = new DateTime(2026, 8, 12, 13, 5, 22, 123, DateTimeKind.Utc);

		[TestCase("2026-08-12T13:05:22.123", TestName = "String token, no zone marker, is taken at face value as UTC")]
		[TestCase("2026-08-12T13:05:22.123Z", TestName = "String token with Z stays put")]
		[TestCase("2026-08-12T06:05:22.123-07:00", TestName = "String token with an offset is converted")]
		[TestCase("2026-08-12T13:05:22.1230000Z", TestName = "String token with a seven digit fraction is accepted")]
		public void ReadJson_WithStringToken_ReturnsUtc(string serialized)
		{
			// Arrange -- DateParseHandling.None forces the reader to hand over a JsonToken.String.
			var settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };

			// Act
			var payload = JsonConvert.DeserializeObject<UtcTimestampPayload>($"{{\"Timestamp\":\"{serialized}\"}}", settings);

			// Assert
			payload.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
			payload.Timestamp.Should().Be(ExpectedUtc);
		}

		[TestCase(DateTimeZoneHandling.Unspecified, TestName = "Date token the reader left Unspecified is marked UTC")]
		[TestCase(DateTimeZoneHandling.Local, TestName = "Date token the reader made Local is converted to UTC")]
		[TestCase(DateTimeZoneHandling.Utc, TestName = "Date token the reader already made UTC is untouched")]
		[TestCase(DateTimeZoneHandling.RoundtripKind, TestName = "Date token round-tripped from a Z keeps its instant")]
		public void ReadJson_WithDateToken_ReturnsUtc(DateTimeZoneHandling zoneHandling)
		{
			// Arrange -- DateParseHandling.DateTime makes the reader produce a JsonToken.Date.
			var settings = new JsonSerializerSettings
			{
				DateParseHandling = DateParseHandling.DateTime,
				DateTimeZoneHandling = zoneHandling
			};

			// Act
			var payload = JsonConvert.DeserializeObject<UtcTimestampPayload>("{\"Timestamp\":\"2026-08-12T13:05:22.123Z\"}", settings);

			// Assert
			payload.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
			payload.Timestamp.Should().Be(ExpectedUtc);
		}

		[TestCase(DateTimeKind.Unspecified, TestName = "An Unspecified instant from the repositories round-trips")]
		[TestCase(DateTimeKind.Utc, TestName = "A UTC instant round-trips")]
		public void SerializeThenDeserialize_PreservesTheInstant(DateTimeKind kind)
		{
			// Arrange
			var original = new UtcTimestampPayload
			{
				Timestamp = DateTime.SpecifyKind(new DateTime(2026, 8, 12, 13, 5, 22, 123), kind)
			};

			// Act
			var serialized = JsonConvert.SerializeObject(original);
			var round = JsonConvert.DeserializeObject<UtcTimestampPayload>(serialized);

			// Assert
			serialized.Should().Contain("2026-08-12T13:05:22.123Z");
			round.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
			round.Timestamp.Should().Be(ExpectedUtc);
		}

		[Test]
		public void SerializeThenDeserialize_WithALocalInstant_PreservesTheInstant()
		{
			// Arrange -- a Local value means the process timezone leaked in somewhere upstream.
			var original = new UtcTimestampPayload { Timestamp = ExpectedUtc.ToLocalTime() };

			// Act
			var round = JsonConvert.DeserializeObject<UtcTimestampPayload>(JsonConvert.SerializeObject(original));

			// Assert
			round.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
			round.Timestamp.Should().Be(ExpectedUtc);
		}

		[Test]
		public void ReadJson_WithANullValue_StaysNull()
		{
			// Arrange / Act
			var payload = JsonConvert.DeserializeObject<NullableUtcTimestampPayload>("{\"Timestamp\":null}");

			// Assert
			payload.Timestamp.Should().BeNull();
		}

		private class UtcTimestampPayload
		{
			[JsonConverter(typeof(UtcDateTimeConverter))]
			public DateTime Timestamp { get; set; }
		}

		private class NullableUtcTimestampPayload
		{
			[JsonConverter(typeof(UtcDateTimeConverter))]
			public DateTime? Timestamp { get; set; }
		}
	}
}
