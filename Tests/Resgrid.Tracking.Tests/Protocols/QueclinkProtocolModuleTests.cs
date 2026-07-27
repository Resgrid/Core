using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.Providers.Tracking.Protocols.Queclink;

namespace Resgrid.Tracking.Tests.Protocols
{
	[TestFixture]
	public class QueclinkProtocolModuleTests
	{
		[Test]
		public void Module_TransportContract_AdvertisesTcpOnly()
		{
			// Arrange
			var module = new QueclinkProtocolModule();

			// Act
			var transports = module.SupportedTransports;

			// Assert
			module.ProtocolKey.Should().Be(
				"queclink-attrack");
			transports.Should().BeEquivalentTo(
				new[] { TrackingSocketTransport.Tcp });
			var createUdp = () => module.CreateSession(
				CreateContext(
					TrackingSocketTransport.Udp));
			createUdp.Should()
				.Throw<NotSupportedException>();
		}

		[Test]
		public void Parse_GtFriFixture_MapsCanonicalPosition()
		{
			// Arrange
			var session = CreateSession();
			var input = new ReadOnlySequence<byte>(
				Fixture("gtfri-live.txt"));

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Positions);
			result.Message.ExternalIdentifier.Should()
				.Be("868487004353181");
			result.Message.RequiresResponse.Should()
				.BeFalse();
			var position =
				result.Message.Positions.Single();
			position.TimestampUtc.Should().Be(
				new DateTime(
					2021,
					6,
					8,
					6,
					43,
					28,
					DateTimeKind.Utc));
			position.Longitude.Should().Be(
				114.015515m);
			position.Latitude.Should().Be(
				22.537178m);
			position.AltitudeMeters.Should()
				.Be(264.1m);
			position.ExternalPowerVolts.Should()
				.Be(14.051m);
			position.BatteryPercent.Should().Be(100m);
			position.EventId.Should()
				.StartWith("queclink:");
		}

		[Test]
		public void Parse_BufferedGteriFixture_MapsBufferedPosition()
		{
			// Arrange
			var session = CreateSession();
			var input = new ReadOnlySequence<byte>(
				Fixture("gteri-buffered.txt"));

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Positions);
			result.Message.ExternalIdentifier.Should()
				.Be("862364030261132");
			var position =
				result.Message.Positions.Single();
			position.TimestampUtc.Should().Be(
				new DateTime(
					2024,
					8,
					17,
					13,
					11,
					55,
					DateTimeKind.Utc));
			position.Longitude.Should().Be(
				46.723488m);
			position.Latitude.Should().Be(
				24.590880m);
			position.SpeedMetersPerSecond.Should()
				.BeApproximately(
					43.4m / 3.6m,
					0.000001m);
		}

		[Test]
		public void Parse_IgnitionFixture_MapsIgnition()
		{
			// Arrange
			var session = CreateSession();
			var input = new ReadOnlySequence<byte>(
				Fixture("gtign-live.txt"));

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Positions);
			result.Message.Positions.Single()
				.Ignition.Should()
				.BeTrue();
		}

		[Test]
		public void Parse_Heartbeat_BuildsResponseOnlyAfterAcceptance()
		{
			// Arrange
			var session = CreateSession();
			var input = new ReadOnlySequence<byte>(
				Fixture("heartbeat.txt"));
			var result = session.Parse(ref input);

			// Act
			var accepted = session.BuildResponse(
				result.Message,
				Acceptance(
					TrackingAcceptanceStatus.Accepted));
			var rejected = session.BuildResponse(
				result.Message,
				Acceptance(
					TrackingAcceptanceStatus.Rejected));

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Heartbeat);
			result.Message.ExternalIdentifier.Should()
				.Be("135790246811220");
			Encoding.ASCII.GetString(accepted.Span)
				.Should()
				.Be("+SACK:GTHBD,1A0401,11F0$");
			rejected.IsEmpty.Should().BeTrue();
		}

		[Test]
		public void Parse_FragmentedAndCoalescedFrames_ConsumesOneFrameAtATime()
		{
			// Arrange
			var session = CreateSession();
			var frame = Fixture("gtfri-live.txt");
			var fragment = new ReadOnlySequence<byte>(
				frame.AsMemory(0, frame.Length - 1));
			var coalesced = new ReadOnlySequence<byte>(
				frame.Concat(frame).ToArray());

			// Act
			var incomplete = session.Parse(ref fragment);
			var first = session.Parse(ref coalesced);
			var consumed = ConsumedLength(
				coalesced,
				first);
			coalesced = coalesced.Slice(
				first.Consumed);
			var second = session.Parse(ref coalesced);

			// Assert
			incomplete.Status.Should().Be(
				ProtocolParseStatus.NeedMoreData);
			consumed.Should().Be(frame.Length);
			first.Status.Should().Be(
				ProtocolParseStatus.Positions);
			second.Status.Should().Be(
				ProtocolParseStatus.Positions);
			first.Message.Positions.Single().EventId
				.Should()
				.Be(second.Message.Positions
					.Single()
					.EventId);
		}

		[Test]
		public void Parse_UnknownReportType_ReturnsUnsupported()
		{
			// Arrange
			var session = CreateSession();
			var input = new ReadOnlySequence<byte>(
				Encoding.ASCII.GetBytes(
					"+RESP:GTXYZ,DF0200,868487004353181,20210608064328,0001$"));

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Unsupported);
			result.ReasonCode.Should().Be(
				"report-type-unsupported");
		}

		[Test]
		public void Parse_NonDigitImei_ReturnsMalformed()
		{
			// Arrange
			var session = CreateSession();
			var input = new ReadOnlySequence<byte>(
				Encoding.ASCII.GetBytes(
					"+ACK:GTHBD,1A0401,13579024681122A,,20100214093254,11F0$"));

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Malformed);
			result.ReasonCode.Should().Be(
				"header-invalid");
		}

		private static ITrackingProtocolSession
			CreateSession()
		{
			return new QueclinkProtocolModule()
				.CreateSession(
					CreateContext(
						TrackingSocketTransport.Tcp));
		}

		private static TrackingSessionContext CreateContext(
			TrackingSocketTransport transport)
		{
			return new TrackingSessionContext
			{
				SessionId = "queclink-test",
				Transport = transport,
				ConnectedOnUtc = DateTime.UtcNow,
				MaxFrameBytes = 65536
			};
		}

		private static TrackingAcceptance Acceptance(
			TrackingAcceptanceStatus status)
		{
			return new TrackingAcceptance
			{
				Status = status
			};
		}

		private static byte[] Fixture(string fileName)
		{
			var path = Path.Combine(
				TestContext.CurrentContext.TestDirectory,
				"Data",
				"Queclink",
				fileName);
			return Encoding.ASCII.GetBytes(
				File.ReadAllText(path).Trim());
		}

		private static long ConsumedLength(
			ReadOnlySequence<byte> input,
			ProtocolParseResult result)
		{
			return input.Slice(
					0,
					result.Consumed)
				.Length;
		}
	}
}
