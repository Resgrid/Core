using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.Providers.Tracking.Protocols.Gt06;
using UnitTrackingDevice = Resgrid.Model.UnitTrackingDevice;

namespace Resgrid.Tracking.Tests.Protocols
{
	[TestFixture]
	public class Gt06ProtocolModuleTests
	{
		[Test]
		public void Module_TransportContract_AdvertisesTcpOnly()
		{
			// Arrange
			var module = new Gt06ProtocolModule();

			// Act
			var transports = module.SupportedTransports;

			// Assert
			module.ProtocolKey.Should().Be("gt06");
			transports.Should().BeEquivalentTo(
				new[] { TrackingSocketTransport.Tcp });
			var createUdp = () => module.CreateSession(
				CreateContext(
					TrackingSocketTransport.Udp));
			createUdp.Should()
				.Throw<NotSupportedException>();
		}

		[Test]
		public void Parse_FragmentedLogin_ReturnsImeiAndExactAcceptedResponse()
		{
			// Arrange
			var session = CreateSession();
			var login = Fixture("login.hex");
			var fragment = new ReadOnlySequence<byte>(
				login.AsMemory(0, 7));

			// Act
			var incomplete = session.Parse(ref fragment);
			var input = new ReadOnlySequence<byte>(login);
			var result = session.Parse(ref input);
			var response = session.BuildResponse(
				result.Message,
				Acceptance(
					TrackingAcceptanceStatus.Accepted));

			// Assert
			incomplete.Status.Should().Be(
				ProtocolParseStatus.NeedMoreData);
			result.Status.Should().Be(
				ProtocolParseStatus.Login);
			result.Message.ExternalIdentifier.Should()
				.Be("864717003283581");
			response.ToArray().Should().Equal(
				Convert.FromHexString(
					"78780501000955940D0A"));
		}

		[Test]
		public void Parse_StandardLocationFixture_MapsCanonicalPositionAndAck()
		{
			// Arrange
			var session = AuthenticatedSession();
			var input = new ReadOnlySequence<byte>(
				Fixture("location-standard.hex"));

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
			var position =
				result.Message.Positions.Single();
			position.TimestampUtc.Should().Be(
				new DateTime(
					2015,
					12,
					29,
					2,
					51,
					5,
					DateTimeKind.Utc));
			position.Latitude.Should().BeApproximately(
				23.1116933333333m,
				0.000000000001m);
			position.Longitude.Should().BeApproximately(
				114.409297777778m,
				0.000000000001m);
			position.IsValidFix.Should().BeTrue();
			position.EventId.Should()
				.StartWith("gt06:");
			response.ToArray().Should().Equal(
				Convert.FromHexString(
					"787805220008A8420D0A"));
		}

		[Test]
		public void Parse_JmVl03A0Fixture_MapsGpsAndIgnition()
		{
			// Arrange
			var session = AuthenticatedSession();
			var input = new ReadOnlySequence<byte>(
				Fixture(
					"location-jm-vl03-a0.hex"));

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Positions);
			var position =
				result.Message.Positions.Single();
			position.TimestampUtc.Should().Be(
				new DateTime(
					2023,
					7,
					4,
					17,
					34,
					38,
					DateTimeKind.Utc));
			position.Latitude.Should().BeApproximately(
				-12.9613488888889m,
				0.000000000001m);
			position.Longitude.Should().BeApproximately(
				-38.4829066666667m,
				0.000000000001m);
			position.Ignition.Should().BeTrue();
		}

		[Test]
		public void Parse_Heartbeat_BuildsResponseOnlyAfterAcceptance()
		{
			// Arrange
			var session = AuthenticatedSession();
			var input = new ReadOnlySequence<byte>(
				Fixture("heartbeat.hex"));
			var result = session.Parse(ref input);

			// Act
			var accepted = session.BuildResponse(
				result.Message,
				Acceptance(
					TrackingAcceptanceStatus.Accepted));
			var unavailable = session.BuildResponse(
				result.Message,
				Acceptance(
					TrackingAcceptanceStatus.Unavailable));

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Heartbeat);
			accepted.ToArray().Should().Equal(
				Convert.FromHexString(
					"7878051301295D630D0A"));
			unavailable.IsEmpty.Should().BeTrue();
		}

		[Test]
		public void Parse_ExtendedHeaderLocation_MapsAndAcknowledgesExtendedFrame()
		{
			// Arrange
			var session = AuthenticatedSession();
			var shortFrame =
				Fixture("location-standard.hex");
			var extendedFrame = ToExtended(shortFrame);
			var input = new ReadOnlySequence<byte>(
				extendedFrame);

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
			response.ToArray().Should().Equal(
				Convert.FromHexString(
					"797900052200089BEB0D0A"));
		}

		[Test]
		public void Parse_InvalidCrc_ReturnsMalformed()
		{
			// Arrange
			var session = AuthenticatedSession();
			var frame = Fixture(
				"location-standard.hex");
			frame[12] ^= 0x01;
			var input = new ReadOnlySequence<byte>(frame);

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Malformed);
			result.ReasonCode.Should().Be(
				"crc-invalid");
		}

		[Test]
		public void Parse_PositionBeforeLogin_ClosesSession()
		{
			// Arrange
			var session = CreateSession();
			var input = new ReadOnlySequence<byte>(
				Fixture("location-standard.hex"));

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.CloseSession);
			result.ReasonCode.Should().Be(
				"login-required");
		}

		[Test]
		public void Parse_UnrecognizedCloneMessage_ReturnsUnsupported()
		{
			// Arrange
			var session = AuthenticatedSession();
			var input = new ReadOnlySequence<byte>(
				BuildShortFrame(
					0x44,
					Array.Empty<byte>(),
					0x0102));

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Unsupported);
			result.ReasonCode.Should().Be(
				"message-type-unsupported");
		}

		[TestCase(0x12)]
		[TestCase(0x31)]
		public void Parse_UncertifiedPositionVariant_ReturnsUnsupported(
			byte messageType)
		{
			// Arrange
			var session = AuthenticatedSession();
			var input = new ReadOnlySequence<byte>(
				BuildShortFrame(
					messageType,
					Array.Empty<byte>(),
					0x0102));

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Unsupported);
			result.ReasonCode.Should().Be(
				"message-type-unsupported");
		}

		[Test]
		public void Parse_CoalescedDuplicateFrames_ConsumesOneAndKeepsStableEventId()
		{
			// Arrange
			var session = AuthenticatedSession();
			var frame =
				Fixture("location-standard.hex");
			var input = new ReadOnlySequence<byte>(
				frame.Concat(frame).ToArray());

			// Act
			var first = session.Parse(ref input);
			var consumed = ConsumedLength(input, first);
			input = input.Slice(first.Consumed);
			var second = session.Parse(ref input);

			// Assert
			consumed.Should().Be(frame.Length);
			first.Message.Positions.Single().EventId
				.Should()
				.Be(second.Message.Positions
					.Single()
					.EventId);
		}

		[Test]
		public void EnrichPositions_Vl103Alarm_UsesModelSpecificMapping()
		{
			// Arrange
			var session = AuthenticatedSession();
			var frame = StatusLocationFrame(
				alarm: 0x09);
			var input = new ReadOnlySequence<byte>(frame);
			var result = session.Parse(ref input);

			// Act
			((ITrackingProtocolPositionEnricher)session)
				.EnrichPositions(
					result.Message,
					new UnitTrackingDevice
					{
						ModelKey = "jimi-vl103m",
						ProtocolKey = "gt06"
					});

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Positions);
			result.Message.Positions.Single()
				.AlarmCode.Should()
				.Be("tow");
		}

		private static ITrackingProtocolSession
			AuthenticatedSession()
		{
			var session = CreateSession();
			var loginInput =
				new ReadOnlySequence<byte>(
					Fixture("login.hex"));
			var login = session.Parse(ref loginInput);
			session.BuildResponse(
				login.Message,
				Acceptance(
					TrackingAcceptanceStatus.Accepted));
			return session;
		}

		private static ITrackingProtocolSession
			CreateSession()
		{
			return new Gt06ProtocolModule()
				.CreateSession(
					CreateContext(
						TrackingSocketTransport.Tcp));
		}

		private static TrackingSessionContext CreateContext(
			TrackingSocketTransport transport)
		{
			return new TrackingSessionContext
			{
				SessionId = "gt06-test",
				Transport = transport,
				ConnectedOnUtc = DateTime.UtcNow,
				MaxFrameBytes = 65536
			};
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

		private static byte[] Fixture(string fileName)
		{
			var path = Path.Combine(
				TestContext.CurrentContext.TestDirectory,
				"Data",
				"Gt06",
				fileName);
			return Convert.FromHexString(
				File.ReadAllText(path).Trim());
		}

		private static byte[] ToExtended(
			byte[] shortFrame)
		{
			var declaredLength = shortFrame[2];
			var extended = new byte[
				shortFrame.Length + 1];
			BinaryPrimitives.WriteUInt16BigEndian(
				extended,
				0x7979);
			BinaryPrimitives.WriteUInt16BigEndian(
				extended.AsSpan(2, 2),
				declaredLength);
			shortFrame.AsSpan(
					3,
					shortFrame.Length - 3)
				.CopyTo(extended.AsSpan(4));
			RewriteCrc(extended);
			return extended;
		}

		private static byte[] StatusLocationFrame(
			byte alarm)
		{
			var standard =
				Fixture("location-standard.hex");
			var gps = standard.AsSpan(4, 18)
				.ToArray();
			var payload = new byte[30];
			gps.CopyTo(payload, 0);
			payload[26] = 0x02;
			payload[27] = 6;
			payload[28] = 4;
			payload[29] = alarm;
			return BuildShortFrame(
				0x16,
				payload,
				0x0008);
		}

		private static byte[] BuildShortFrame(
			byte type,
			byte[] payload,
			ushort serial)
		{
			var declaredLength =
				1 + payload.Length + 2 + 2;
			var frame = new byte[
				declaredLength + 5];
			BinaryPrimitives.WriteUInt16BigEndian(
				frame,
				0x7878);
			frame[2] = checked((byte)declaredLength);
			frame[3] = type;
			payload.CopyTo(frame, 4);
			BinaryPrimitives.WriteUInt16BigEndian(
				frame.AsSpan(
					frame.Length - 6,
					2),
				serial);
			BinaryPrimitives.WriteUInt16BigEndian(
				frame.AsSpan(
					frame.Length - 2,
					2),
				0x0D0A);
			RewriteCrc(frame);
			return frame;
		}

		private static void RewriteCrc(byte[] frame)
		{
			var crcOffset = frame.Length - 4;
			BinaryPrimitives.WriteUInt16BigEndian(
				frame.AsSpan(crcOffset, 2),
				CrcX25(
					frame.AsSpan(
						2,
						crcOffset - 2)));
		}

		private static ushort CrcX25(
			ReadOnlySpan<byte> data)
		{
			ushort crc = 0xFFFF;
			foreach (var value in data)
			{
				crc ^= value;
				for (var bit = 0;
				     bit < 8;
				     bit++)
				{
					crc = (crc & 1) != 0
						? (ushort)((crc >> 1) ^ 0x8408)
						: (ushort)(crc >> 1);
				}
			}

			return (ushort)~crc;
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
