using System;
using System.Buffers;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.Providers.Tracking.Protocols.Teltonika;

namespace Resgrid.Tracking.Tests.Protocols
{
	[TestFixture]
	public class TeltonikaCodec8UdpProtocolSessionTests
	{
		private const string Imei = "356307042441013";

		[Test]
		public void Parse_Codec8GoldenDatagram_MapsPositionAndBuildsAcceptedResponse()
		{
			// Arrange
			var session = CreateSession();
			var datagram = Fixture(
				"codec8-udp-location.hex");
			var input = new ReadOnlySequence<byte>(
				datagram);

			// Act
			var result = session.Parse(ref input);
			var response = session.BuildResponse(
				result.Message,
				Acceptance(
					TrackingAcceptanceStatus.Accepted,
					acceptedPositions: 1));

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Positions);
			result.Message.ExternalIdentifier.Should()
				.Be(Imei);
			result.Message.Positions.Should()
				.ContainSingle()
				.Which.Longitude.Should()
				.Be(-122.4194m);
			response.ToArray().Should().Equal(
				Convert.FromHexString(
					"0005CAFE010501"));
			ConsumedLength(input, result).Should()
				.Be(datagram.Length);
		}

		[Test]
		public void Parse_Codec8ExtendedGoldenDatagram_PartialAcceptanceReturnsZero()
		{
			// Arrange
			var session = CreateSession();
			var input = new ReadOnlySequence<byte>(
				Fixture(
					"codec8e-udp-location.hex"));

			// Act
			var result = session.Parse(ref input);
			var response = session.BuildResponse(
				result.Message,
				Acceptance(
					TrackingAcceptanceStatus.Accepted,
					acceptedPositions: 0));

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Positions);
			result.Message.Positions.Should()
				.ContainSingle()
				.Which.Latitude.Should()
				.Be(52.52m);
			response.ToArray().Should().Equal(
				Convert.FromHexString(
					"0005BEEF010700"));
		}

		[Test]
		public void Parse_DeclaredLengthDoesNotMatchDatagram_ReturnsMalformed()
		{
			// Arrange
			var session = CreateSession();
			var datagram = Fixture(
				"codec8-udp-location.hex");
			datagram[1]--;
			var input = new ReadOnlySequence<byte>(
				datagram);

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Malformed);
			result.ReasonCode.Should().Be(
				"datagram-length-mismatch");
		}

		[Test]
		public void Parse_InvalidChannelMarker_ReturnsMalformed()
		{
			// Arrange
			var session = CreateSession();
			var datagram = Fixture(
				"codec8-udp-location.hex");
			datagram[4] = 0;
			var input = new ReadOnlySequence<byte>(
				datagram);

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Malformed);
			result.ReasonCode.Should().Be(
				"udp-header-invalid");
		}

		[Test]
		public void Parse_NonDigitImei_ReturnsMalformed()
		{
			// Arrange
			var session = CreateSession();
			var datagram = Fixture(
				"codec8-udp-location.hex");
			datagram[9] = (byte)'A';
			var input = new ReadOnlySequence<byte>(
				datagram);

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Malformed);
			result.ReasonCode.Should().Be(
				"imei-invalid");
		}

		private static ITrackingProtocolSession
			CreateSession()
		{
			return new TeltonikaCodec8ProtocolModule()
				.CreateSession(
					new TrackingSessionContext
					{
						SessionId =
							"teltonika-udp-test",
						Transport =
							TrackingSocketTransport.Udp,
						ConnectedOnUtc =
							DateTime.UtcNow,
						MaxFrameBytes = 65536
					});
		}

		private static TrackingAcceptance Acceptance(
			TrackingAcceptanceStatus status,
			int acceptedPositions = 0)
		{
			return new TrackingAcceptance
			{
				Status = status,
				AcceptedPositions = acceptedPositions
			};
		}

		private static byte[] Fixture(
			string fileName)
		{
			var path = Path.Combine(
				TestContext.CurrentContext.TestDirectory,
				"Data",
				"Teltonika",
				fileName);
			return Convert.FromHexString(
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
