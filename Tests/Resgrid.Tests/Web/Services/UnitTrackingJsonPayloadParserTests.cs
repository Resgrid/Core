using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Web.Services.ApplicationCore.UnitTracking;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	[NonParallelizable]
	public class UnitTrackingJsonPayloadParserTests
	{
		private UnitTrackingJsonPayloadParser _parser;
		private int _originalMaxRequestBytes;
		private int _originalMaxBatchPositions;
		private int _originalMaxJsonDepth;

		[SetUp]
		public void SetUp()
		{
			_parser = new UnitTrackingJsonPayloadParser();
			_originalMaxRequestBytes = UnitTrackingConfig.MaxRequestBytes;
			_originalMaxBatchPositions = UnitTrackingConfig.MaxBatchPositions;
			_originalMaxJsonDepth = UnitTrackingConfig.MaxJsonDepth;
			UnitTrackingConfig.MaxRequestBytes = 262144;
			UnitTrackingConfig.MaxBatchPositions = 100;
			UnitTrackingConfig.MaxJsonDepth = 16;
		}

		[TearDown]
		public void TearDown()
		{
			UnitTrackingConfig.MaxRequestBytes = _originalMaxRequestBytes;
			UnitTrackingConfig.MaxBatchPositions = _originalMaxBatchPositions;
			UnitTrackingConfig.MaxJsonDepth = _originalMaxJsonDepth;
		}

		[Test]
		public async Task ParseAsync_SingleGenericPayload_MapsCanonicalUnitsAndServerTimestamp()
		{
			var receivedOn = new DateTime(2026, 7, 24, 18, 42, 52, DateTimeKind.Utc);
			var request = Request("""
				{
				  "eventId": "device-record-1",
				  "latitude": 39.7392,
				  "longitude": -104.9903,
				  "speedMetersPerSecond": 13.4,
				  "moving": true,
				  "deviceIdentifier": "device-1234",
				  "unknownTelemetry": { "ignored": true }
				}
				""");

			var result = await _parser.ParseAsync(request, receivedOn);

			result.Status.Should().Be(UnitTrackingPayloadParseStatus.Success);
			result.ReportedDeviceIdentifier.Should().Be("device-1234");
			result.Positions.Should().ContainSingle();
			var position = System.Linq.Enumerable.Single(result.Positions);
			position.TimestampUtc.Should().Be(receivedOn);
			position.TimestampSource.Should().Be(TrackingTimestampSource.Server);
			position.SpeedMetersPerSecond.Should().Be(13.4m);
			position.IsMoving.Should().BeTrue();
		}

		[Test]
		public async Task ParseAsync_InvalidDeviceTimestamp_UsesServerTimestamp()
		{
			var receivedOn = new DateTime(2026, 7, 24, 18, 42, 52, DateTimeKind.Utc);
			var request = Request(
				"{\"eventId\":\"record\",\"timestamp\":\"not-a-device-time\",\"latitude\":1,\"longitude\":2}");

			var result = await _parser.ParseAsync(request, receivedOn);

			result.Status.Should().Be(UnitTrackingPayloadParseStatus.Success);
			var position = result.Positions.Should().ContainSingle().Subject;
			position.TimestampUtc.Should().Be(receivedOn);
			position.TimestampSource.Should().Be(TrackingTimestampSource.Server);
		}

		[Test]
		public async Task ParseAsync_BatchMissingRequiredField_RejectsWholeBatch()
		{
			var request = Request("""
				{
				  "positions": [
				    { "eventId": "1", "latitude": 1, "longitude": 2 },
				    { "eventId": "2", "latitude": 1 }
				  ]
				}
				""");

			var result = await _parser.ParseAsync(request, DateTime.UtcNow);

			result.Status.Should().Be(UnitTrackingPayloadParseStatus.Invalid);
			result.Errors.Should().Contain(error => error.Contains("positions[1]"));
			result.Positions.Should().BeEmpty();
		}

		[Test]
		public async Task ParseAsync_BodyExceedsConfiguredLimit_ReturnsTooLarge()
		{
			UnitTrackingConfig.MaxRequestBytes = 32;
			var request = Request(
				"{\"eventId\":\"record\",\"latitude\":1,\"longitude\":2,\"padding\":\"xxxxxxxxxxxxxxxx\"}");

			var result = await _parser.ParseAsync(request, DateTime.UtcNow);

			result.Status.Should().Be(UnitTrackingPayloadParseStatus.TooLarge);
		}

		[Test]
		public async Task ParseAsync_JsonExceedsConfiguredDepth_ReturnsMalformed()
		{
			UnitTrackingConfig.MaxJsonDepth = 3;
			var request = Request(
				"{\"eventId\":\"record\",\"latitude\":1,\"longitude\":2,\"nested\":{\"a\":{\"b\":{\"c\":1}}}}");

			var result = await _parser.ParseAsync(request, DateTime.UtcNow);

			result.Status.Should().Be(UnitTrackingPayloadParseStatus.Malformed);
		}

		[Test]
		public async Task ParseAsync_DuplicateRequiredProperty_ReturnsMalformed()
		{
			var request = Request(
				"{\"eventId\":\"one\",\"eventId\":\"two\",\"latitude\":1,\"longitude\":2}");

			var result = await _parser.ParseAsync(request, DateTime.UtcNow);

			result.Status.Should().Be(UnitTrackingPayloadParseStatus.Malformed);
		}

		[Test]
		public async Task ParseAsync_GzipContentEncoding_ReturnsUnsupportedMediaType()
		{
			var request = Request("{\"eventId\":\"record\",\"latitude\":1,\"longitude\":2}");
			request.Headers.ContentEncoding = "gzip";

			var result = await _parser.ParseAsync(request, DateTime.UtcNow);

			result.Status.Should().Be(UnitTrackingPayloadParseStatus.UnsupportedMediaType);
		}

		[Test]
		public void Supports_KnownAdapters_RecognizesGenericAndPinnedTraccar()
		{
			_parser.Supports("resgrid-json-v1").Should().BeTrue();
			_parser.Supports(" TRACCAR-JSON-V1 ").Should().BeTrue();
			_parser.Supports("unknown-json-v1").Should().BeFalse();
		}

		[Test]
		public async Task ParseAsync_TraccarFixture_MapsPinnedPositionAndSelectedAttributes()
		{
			var receivedOn = new DateTime(2026, 7, 24, 18, 42, 53, DateTimeKind.Utc);
			var fixture = ReadFixture("sinotrack-st901-h02-position.json");

			var result = await _parser.ParseAsync(
				Request(fixture),
				"traccar-json-v1",
				receivedOn);

			result.Status.Should().Be(UnitTrackingPayloadParseStatus.Success);
			result.ReportedDeviceIdentifier.Should().Be("917000000000");
			var position = result.Positions.Should().ContainSingle().Subject;
			position.EventId.Should().StartWith("traccar:6.14.5:fingerprint:");
			position.TimestampUtc.Should().Be(
				new DateTime(2026, 7, 24, 18, 42, 51, 123, DateTimeKind.Utc));
			position.ReceivedOnUtc.Should().Be(receivedOn);
			position.Latitude.Should().Be(39.7392m);
			position.Longitude.Should().Be(-104.9903m);
			position.AccuracyMeters.Should().Be(4.8m);
			position.AltitudeMeters.Should().Be(1608.2m);
			position.SpeedMetersPerSecond.Should().Be(5.144440m);
			position.HeadingDegrees.Should().Be(271.5m);
			position.Satellites.Should().Be(11);
			position.Hdop.Should().Be(0.8m);
			position.BatteryPercent.Should().Be(87m);
			position.ExternalPowerVolts.Should().Be(13.6m);
			position.Ignition.Should().BeTrue();
			position.IsMoving.Should().BeTrue();
			position.AlarmCode.Should().Be("sos");
			position.SignalPercent.Should().BeNull(
				"Traccar rssi has no stable percentage unit in the pinned contract");
			position.TimestampSource.Should().Be(TrackingTimestampSource.Device);
			position.IsValidFix.Should().BeTrue();

			var retry = await _parser.ParseAsync(
				Request(fixture),
				"traccar-json-v1",
				receivedOn.AddSeconds(30));
			retry.Positions.Should().ContainSingle()
				.Which.EventId.Should().Be(position.EventId);
		}

		[Test]
		public async Task ParseAsync_TraccarDeviceIdsDoNotMatch_RejectsPayload()
		{
			var request = Request("""
				{
				  "position": {
				    "deviceId": 42,
				    "fixTime": "2026-07-24T18:42:51.123Z",
				    "valid": true,
				    "latitude": 39.7392,
				    "longitude": -104.9903
				  },
				  "device": {
				    "id": 99,
				    "uniqueId": "917000000000"
				  }
				}
				""");

			var result = await _parser.ParseAsync(
				request,
				"traccar-json-v1",
				DateTime.UtcNow);

			result.Status.Should().Be(UnitTrackingPayloadParseStatus.Invalid);
			result.Errors.Should().Contain(error => error.Contains("must match"));
			result.Positions.Should().BeEmpty();
		}

		[Test]
		public async Task ParseAsync_TraccarHasNoValidTimestamp_RejectsPayload()
		{
			var request = Request("""
				{
				  "position": {
				    "deviceId": 42,
				    "fixTime": "not-a-time",
				    "valid": true,
				    "latitude": 39.7392,
				    "longitude": -104.9903
				  },
				  "device": {
				    "id": 42,
				    "uniqueId": "917000000000"
				  }
				}
				""");

			var result = await _parser.ParseAsync(
				request,
				"traccar-json-v1",
				DateTime.UtcNow);

			result.Status.Should().Be(UnitTrackingPayloadParseStatus.Invalid);
			result.Errors.Should().Contain(error => error.Contains("valid position"));
			result.Positions.Should().BeEmpty();
		}

		private static string ReadFixture(string fileName) =>
			System.IO.File.ReadAllText(
				Path.Combine(
					TestContext.CurrentContext.TestDirectory,
					"Data",
					"UnitTracking",
					"Fixtures",
					"traccar",
					"v6.14.5",
					fileName));

		private static HttpRequest Request(string body)
		{
			var bytes = Encoding.UTF8.GetBytes(body);
			var context = new DefaultHttpContext();
			context.Request.ContentType = "application/json; charset=utf-8";
			context.Request.ContentLength = bytes.Length;
			context.Request.Body = new MemoryStream(bytes);
			return context.Request;
		}
	}
}
