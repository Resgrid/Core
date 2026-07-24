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
