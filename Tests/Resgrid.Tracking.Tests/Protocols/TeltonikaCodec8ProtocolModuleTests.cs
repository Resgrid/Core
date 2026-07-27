using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.Providers.Tracking.Protocols.Teltonika;
using UnitTrackingDevice = Resgrid.Model.UnitTrackingDevice;

namespace Resgrid.Tracking.Tests.Protocols
{
	[TestFixture]
	public class TeltonikaCodec8ProtocolModuleTests
	{
		private const string Imei = "356307042441013";

		[Test]
		public void Module_TransportContract_AdvertisesTcpAndUdp()
		{
			// Arrange
			var module =
				new TeltonikaCodec8ProtocolModule();

			// Act
			var transports = module.SupportedTransports;

			// Assert
			module.ProtocolKey.Should().Be(
				"teltonika-codec8");
			transports.Should().BeEquivalentTo(
				new[]
				{
					TrackingSocketTransport.Tcp,
					TrackingSocketTransport.Udp
				});
			module.CreateSession(
					CreateContext(
						TrackingSocketTransport.Udp))
				.Should()
				.BeOfType<
					TeltonikaCodec8UdpProtocolSession>();
			var createUnknown = () => module.CreateSession(
				CreateContext(
					TrackingSocketTransport.Unknown));
			createUnknown.Should()
				.Throw<NotSupportedException>();
		}

		[Test]
		public void Parse_FragmentedLogin_WaitsThenReturnsImei()
		{
			// Arrange
			var session = CreateSession();
			var login = LoginBytes();
			var fragment = new ReadOnlySequence<byte>(
				login.AsMemory(0, 8));

			// Act
			var incomplete = session.Parse(
				ref fragment);
			var completeInput =
				new ReadOnlySequence<byte>(login);
			var complete = session.Parse(
				ref completeInput);

			// Assert
			incomplete.Status.Should().Be(
				ProtocolParseStatus.NeedMoreData);
			complete.Status.Should().Be(
				ProtocolParseStatus.Login);
			complete.Message.ExternalIdentifier
				.Should()
				.Be(Imei);
			complete.Message.RequiresResponse
				.Should()
				.BeTrue();
			ConsumedLength(
					completeInput,
					complete)
				.Should()
				.Be(login.Length);
		}

		[TestCase(
			TrackingAcceptanceStatus.Accepted,
			(byte)0x01)]
		[TestCase(
			TrackingAcceptanceStatus.Rejected,
			(byte)0x00)]
		public void BuildResponse_LoginDecision_UsesOneBinaryByte(
			TrackingAcceptanceStatus status,
			byte expected)
		{
			// Arrange
			var session = CreateSession();
			var login = ParseLogin(session);

			// Act
			var response = session.BuildResponse(
				login.Message,
				Acceptance(status));

			// Assert
			response.ToArray()
				.Should()
				.Equal(new[] { expected });
		}

		[Test]
		public void Parse_Codec8GoldenFrame_MapsCanonicalPosition()
		{
			// Arrange
			var session = AuthenticatedSession();
			var frame = Fixture("codec8-location.hex");
			var input = new ReadOnlySequence<byte>(
				frame);

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Positions);
			result.Message.Positions.Should()
				.ContainSingle();
			var position =
				result.Message.Positions.Single();
			position.TimestampUtc.Should().Be(
				new DateTime(
					2024,
					1,
					2,
					3,
					4,
					5,
					DateTimeKind.Utc));
			position.Longitude.Should().Be(
				-122.4194m);
			position.Latitude.Should().Be(
				37.7749m);
			position.AltitudeMeters.Should().Be(15m);
			position.HeadingDegrees.Should().Be(90m);
			position.Satellites.Should().Be(8);
			position.SpeedMetersPerSecond
				.Should()
				.Be(10m);
			position.IsValidFix.Should().BeTrue();
			position.EventId.Should()
				.StartWith("teltonika:");
			position.EventId.Should().HaveLength(74);
		}

		[Test]
		public void Parse_Codec8ExtendedGoldenFrame_MapsCanonicalPosition()
		{
			// Arrange
			var session = AuthenticatedSession();
			var frame = Fixture(
				"codec8e-location.hex");
			var input = Segmented(
				frame,
				13,
				31);

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Positions);
			var position =
				result.Message.Positions.Single();
			position.Longitude.Should().Be(13.405m);
			position.Latitude.Should().Be(52.52m);
			position.AltitudeMeters.Should().Be(34m);
			position.HeadingDegrees.Should().Be(270m);
			position.Satellites.Should().Be(11);
			position.SpeedMetersPerSecond
				.Should()
				.Be(20m);
			position.IsValidFix.Should().BeTrue();
		}

		[Test]
		public void Parse_NonEmptyCodec8AndExtendedIoStructures_AcceptsRecords()
		{
			// Arrange
			var codec8Session = AuthenticatedSession();
			var codec8Input =
				new ReadOnlySequence<byte>(
					FrameWithIo(
						"codec8-location.hex",
						"01040101FF0102123401031234567801040102030405060708"));
			var extendedSession = AuthenticatedSession();
			var extendedInput =
				new ReadOnlySequence<byte>(
					FrameWithIo(
						"codec8e-location.hex",
						"0001000500010001FF0001000212340001000312345678000100040102030405060708000100050003AABBCC"));

			// Act
			var codec8 = codec8Session.Parse(
				ref codec8Input);
			var extended = extendedSession.Parse(
				ref extendedInput);

			// Assert
			codec8.Status.Should().Be(
				ProtocolParseStatus.Positions);
			codec8.Message.Positions.Should()
				.ContainSingle();
			extended.Status.Should().Be(
				ProtocolParseStatus.Positions);
			extended.Message.Positions.Should()
				.ContainSingle();
		}

		[Test]
		public void EnrichPositions_WaveOneProfile_MapsOnlyAllowlistedIoValues()
		{
			// Arrange
			var session = AuthenticatedSession();
			var input = new ReadOnlySequence<byte>(
				Fixture("codec8-io-location.hex"));
			var result = session.Parse(ref input);
			var position =
				result.Message.Positions.Single();

			// Act
			((ITrackingProtocolPositionEnricher)session)
				.EnrichPositions(
					result.Message,
					new UnitTrackingDevice
					{
						ModelKey =
							"teltonika-fmc920",
						ProtocolKey =
							"teltonika-codec8"
					});

			// Assert
			position.Hdop.Should().Be(1.2m);
			position.ExternalPowerVolts.Should()
				.Be(12.5m);
			position.Ignition.Should().BeTrue();
			position.IsMoving.Should().BeTrue();
			position.BatteryPercent.Should().BeNull();
			position.SignalPercent.Should().BeNull();
		}

		[Test]
		public void EnrichPositions_UnregisteredModel_DoesNotApplyFamilyIoMap()
		{
			// Arrange
			var session = AuthenticatedSession();
			var input = new ReadOnlySequence<byte>(
				Fixture("codec8-io-location.hex"));
			var result = session.Parse(ref input);
			var position =
				result.Message.Positions.Single();

			// Act
			((ITrackingProtocolPositionEnricher)session)
				.EnrichPositions(
					result.Message,
					new UnitTrackingDevice
					{
						ModelKey = "unknown-model",
						ProtocolKey =
							"teltonika-codec8"
					});

			// Assert
			position.ExternalPowerVolts.Should()
				.BeNull();
			position.Ignition.Should().BeNull();
			position.IsMoving.Should().BeNull();
		}

		[Test]
		public void Parse_MultiRecordPacket_ReturnsEveryRecordAndCountAcknowledgement()
		{
			// Arrange
			var session = AuthenticatedSession();
			var input = new ReadOnlySequence<byte>(
				MultiRecordFrame(
					"codec8-location.hex"));

			// Act
			var result = session.Parse(ref input);
			var response = session.BuildResponse(
				result.Message,
				Acceptance(
					TrackingAcceptanceStatus.Accepted,
					acceptedPositions: 2));

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Positions);
			result.Message.Positions.Should()
				.HaveCount(2);
			response.ToArray().Should().Equal(
				new byte[] { 0, 0, 0, 2 });
		}

		[Test]
		public void Parse_FragmentedAndCoalescedFrames_ConsumesOneCompleteFrameAtATime()
		{
			// Arrange
			var session = AuthenticatedSession();
			var frame = Fixture("codec8-location.hex");
			var fragment = new ReadOnlySequence<byte>(
				frame.AsMemory(0, frame.Length - 1));
			var coalesced = new ReadOnlySequence<byte>(
				frame.Concat(frame).ToArray());

			// Act
			var incomplete = session.Parse(
				ref fragment);
			var first = session.Parse(
				ref coalesced);
			var firstConsumed =
				ConsumedLength(coalesced, first);
			coalesced = coalesced.Slice(
				first.Consumed);
			var second = session.Parse(
				ref coalesced);

			// Assert
			incomplete.Status.Should().Be(
				ProtocolParseStatus.NeedMoreData);
			firstConsumed
				.Should()
				.Be(frame.Length);
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
		public void Parse_InvalidCrc_ReturnsMalformed()
		{
			// Arrange
			var session = AuthenticatedSession();
			var frame = Fixture("codec8-location.hex");
			frame[20] ^= 0x01;
			var input = new ReadOnlySequence<byte>(
				frame);

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Malformed);
			result.ReasonCode.Should().Be(
				"crc-invalid");
		}

		[Test]
		public void Parse_MismatchedRecordCounts_ReturnsMalformed()
		{
			// Arrange
			var session = AuthenticatedSession();
			var frame = Fixture("codec8-location.hex");
			var dataLength =
				(int)BinaryPrimitives.ReadUInt32BigEndian(
					frame.AsSpan(4, 4));
			frame[8 + dataLength - 1] = 2;
			RewriteCrc(frame);
			var input = new ReadOnlySequence<byte>(
				frame);

			// Act
			var result = session.Parse(ref input);

			// Assert
			result.Status.Should().Be(
				ProtocolParseStatus.Malformed);
			result.ReasonCode.Should().Be(
				"record-count-mismatch");
		}

		[Test]
		public void BuildResponse_PartialOrUnavailablePositionAcceptance_ReturnsZero()
		{
			// Arrange
			var session = AuthenticatedSession();
			var frame = Fixture("codec8-location.hex");
			var input = new ReadOnlySequence<byte>(
				frame);
			var positions = session.Parse(
				ref input);

			// Act
			var partial = session.BuildResponse(
				positions.Message,
				Acceptance(
					TrackingAcceptanceStatus.Accepted,
					acceptedPositions: 0));
			var unavailable = session.BuildResponse(
				positions.Message,
				Acceptance(
					TrackingAcceptanceStatus.Unavailable));

			// Assert
			partial.ToArray().Should().Equal(
				new byte[] { 0, 0, 0, 0 });
			unavailable.ToArray().Should().Equal(
				new byte[] { 0, 0, 0, 0 });
		}

		[Test]
		public void BuildResponse_FullPositionAcceptance_ReturnsFourByteRecordCount()
		{
			// Arrange
			var session = AuthenticatedSession();
			var frame = Fixture("codec8-location.hex");
			var input = new ReadOnlySequence<byte>(
				frame);
			var positions = session.Parse(
				ref input);

			// Act
			var response = session.BuildResponse(
				positions.Message,
				Acceptance(
					TrackingAcceptanceStatus.Accepted,
					acceptedPositions: 1));

			// Assert
			response.ToArray().Should().Equal(
				new byte[] { 0, 0, 0, 1 });
		}

		private static ITrackingProtocolSession
			AuthenticatedSession()
		{
			var session = CreateSession();
			var login = ParseLogin(session);
			session.BuildResponse(
				login.Message,
				Acceptance(
					TrackingAcceptanceStatus.Accepted));
			return session;
		}

		private static ProtocolParseResult ParseLogin(
			ITrackingProtocolSession session)
		{
			var input = new ReadOnlySequence<byte>(
				LoginBytes());
			return session.Parse(ref input);
		}

		private static ITrackingProtocolSession
			CreateSession()
		{
			return new TeltonikaCodec8ProtocolModule()
				.CreateSession(
					CreateContext(
						TrackingSocketTransport.Tcp));
		}

		private static TrackingSessionContext CreateContext(
			TrackingSocketTransport transport)
		{
			return new TrackingSessionContext
			{
				SessionId = "teltonika-test",
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

		private static byte[] LoginBytes()
		{
			return Convert.FromHexString(
				"000F333536333037303432343431303133");
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

		private static ReadOnlySequence<byte> Segmented(
			byte[] source,
			params int[] splitOffsets)
		{
			var offsets = new[] { 0 }
				.Concat(splitOffsets)
				.Concat(new[] { source.Length })
				.ToArray();
			TestSequenceSegment first = null;
			TestSequenceSegment last = null;
			for (var index = 0;
			     index < offsets.Length - 1;
			     index++)
			{
				var segment =
					new TestSequenceSegment(
						source.AsMemory(
							offsets[index],
							offsets[index + 1] -
							offsets[index]));
				if (first == null)
					first = segment;
				if (last != null)
					last.SetNext(segment);
				last = segment;
			}

			return new ReadOnlySequence<byte>(
				first,
				0,
				last,
				last.Memory.Length);
		}

		private static byte[] FrameWithIo(
			string fixtureName,
			string ioHex)
		{
			var frame = Fixture(fixtureName);
			var dataLength =
				(int)BinaryPrimitives.ReadUInt32BigEndian(
					frame.AsSpan(4, 4));
			var originalData = frame.AsSpan(
				8,
				dataLength);
			var io = Convert.FromHexString(ioHex);
			var data = new byte[
				2 +
				24 +
				io.Length +
				1];
			data[0] = originalData[0];
			data[1] = 1;
			originalData.Slice(2, 24).CopyTo(
				data.AsSpan(2));
			io.CopyTo(data, 26);
			data[^1] = 1;
			return BuildFrame(data);
		}

		private static byte[] MultiRecordFrame(
			string fixtureName)
		{
			var frame = Fixture(fixtureName);
			var dataLength =
				(int)BinaryPrimitives.ReadUInt32BigEndian(
					frame.AsSpan(4, 4));
			var originalData = frame.AsSpan(
				8,
				dataLength);
			var record = originalData.Slice(
				2,
				originalData.Length - 3);
			var data = new byte[
				2 +
				(record.Length * 2) +
				1];
			data[0] = originalData[0];
			data[1] = 2;
			record.CopyTo(data.AsSpan(2));
			record.CopyTo(
				data.AsSpan(2 + record.Length));
			data[^1] = 2;
			return BuildFrame(data);
		}

		private static byte[] BuildFrame(byte[] data)
		{
			var frame = new byte[8 + data.Length + 4];
			BinaryPrimitives.WriteUInt32BigEndian(
				frame.AsSpan(4, 4),
				(uint)data.Length);
			data.CopyTo(frame, 8);
			RewriteCrc(frame);
			return frame;
		}

		private static void RewriteCrc(byte[] frame)
		{
			var dataLength =
				(int)BinaryPrimitives.ReadUInt32BigEndian(
					frame.AsSpan(4, 4));
			ushort crc = 0;
			for (var offset = 8;
			     offset < 8 + dataLength;
			     offset++)
			{
				crc ^= frame[offset];
				for (var bit = 0;
				     bit < 8;
				     bit++)
				{
					crc = (crc & 1) != 0
						? (ushort)((crc >> 1) ^ 0xA001)
						: (ushort)(crc >> 1);
				}
			}

			BinaryPrimitives.WriteUInt32BigEndian(
				frame.AsSpan(
					8 + dataLength,
					4),
				crc);
		}

		private sealed class TestSequenceSegment :
			ReadOnlySequenceSegment<byte>
		{
			public TestSequenceSegment(
				ReadOnlyMemory<byte> memory)
			{
				Memory = memory;
			}

			public void SetNext(
				TestSequenceSegment next)
			{
				next.RunningIndex =
					RunningIndex + Memory.Length;
				Next = next;
			}
		}
	}
}
