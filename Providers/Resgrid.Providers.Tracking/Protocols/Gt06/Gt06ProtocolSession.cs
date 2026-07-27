using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Resgrid.Model;
using Resgrid.Model.Tracking;

namespace Resgrid.Providers.Tracking.Protocols.Gt06
{
	public sealed class Gt06ProtocolSession :
		ITrackingProtocolSession,
		ITrackingProtocolPositionEnricher
	{
		private const ushort ShortHeader = 0x7878;
		private const ushort ExtendedHeader = 0x7979;
		private const ushort Tail = 0x0D0A;
		private const int MinimumDeclaredLength = 5;
		private const int GpsPayloadLength = 18;

		private const byte LoginType = 0x01;
		private const byte StatusType = 0x13;
		private const byte HeartbeatType = 0x23;
		private const byte GpsLbsStatusType = 0x16;
		private const byte GpsLbsType = 0x22;
		private const byte JmVl03GpsType = 0xA0;

		private static readonly IReadOnlySet<byte>
			PositionTypes = new HashSet<byte>
			{
				GpsLbsStatusType,
				GpsLbsType,
				JmVl03GpsType
			};

		private static readonly IReadOnlySet<byte>
			CommonStatusPositionTypes =
				new HashSet<byte>
				{
					GpsLbsStatusType
				};

		private readonly int _maximumFrameBytes;
		private bool _loginAccepted;

		public Gt06ProtocolSession(
			TrackingSessionContext context)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			if (context.MaxFrameBytes <= 0)
				throw new ArgumentOutOfRangeException(
					nameof(context.MaxFrameBytes));

			_maximumFrameBytes = context.MaxFrameBytes;
		}

		public ProtocolParseResult Parse(
			ref ReadOnlySequence<byte> input)
		{
			if (input.Length < 3)
				return NeedMore(input);

			var prefix = input.Slice(
					0,
					Math.Min(input.Length, 4))
				.ToArray();
			var header = BinaryPrimitives
				.ReadUInt16BigEndian(prefix);
			var extended = header == ExtendedHeader;
			if (header != ShortHeader &&
			    !extended)
			{
				return Terminal(
					input.End,
					ProtocolParseStatus.Malformed,
					"header-invalid");
			}

			var lengthFieldBytes = extended ? 2 : 1;
			if (input.Length <
			    2 + lengthFieldBytes)
				return NeedMore(input);
			var declaredLength = extended
				? BinaryPrimitives.ReadUInt16BigEndian(
					prefix.AsSpan(2, 2))
				: prefix[2];
			if (declaredLength <
			    MinimumDeclaredLength)
			{
				return Terminal(
					input.End,
					ProtocolParseStatus.Malformed,
					"length-invalid");
			}

			var frameLength =
				2 +
				lengthFieldBytes +
				declaredLength +
				2;
			if (frameLength > _maximumFrameBytes)
			{
				return Terminal(
					input.End,
					ProtocolParseStatus.Malformed,
					"frame-too-large");
			}
			if (input.Length < frameLength)
				return NeedMore(input);

			var consumed = input.GetPosition(
				frameLength);
			var frame = input.Slice(
					0,
					frameLength)
				.ToArray();
			if (BinaryPrimitives.ReadUInt16BigEndian(
				    frame.AsSpan(
					    frame.Length - 2,
					    2)) != Tail)
			{
				return Terminal(
					consumed,
					ProtocolParseStatus.Malformed,
					"tail-invalid");
			}

			var crcOffset = frame.Length - 4;
			var expectedCrc =
				BinaryPrimitives.ReadUInt16BigEndian(
					frame.AsSpan(crcOffset, 2));
			var actualCrc = Gt06Crc16.Compute(
				frame.AsSpan(
					2,
					crcOffset - 2));
			if (actualCrc != expectedCrc)
			{
				return Terminal(
					consumed,
					ProtocolParseStatus.Malformed,
					"crc-invalid");
			}

			var typeOffset =
				2 + lengthFieldBytes;
			var serialOffset = frame.Length - 6;
			if (serialOffset <= typeOffset)
			{
				return Terminal(
					consumed,
					ProtocolParseStatus.Malformed,
					"payload-length-invalid");
			}

			var type = frame[typeOffset];
			var serial = BinaryPrimitives
				.ReadUInt16BigEndian(
					frame.AsSpan(
						serialOffset,
						2));
			var payload = frame.AsSpan(
				typeOffset + 1,
				serialOffset -
				typeOffset -
				1);
			var acknowledgementToken =
				CreateAcknowledgementToken(
					extended,
					type,
					serial);

			if (type == LoginType)
			{
				if (!TryReadImei(
					    payload,
					    out var imei))
				{
					return Terminal(
						consumed,
						ProtocolParseStatus.Malformed,
						"imei-invalid");
				}

				return Message(
					consumed,
					ProtocolParseStatus.Login,
					new ProtocolMessage
					{
						MessageType =
							ProtocolMessageType.Login,
						ExternalIdentifier = imei,
						AcknowledgementToken =
							acknowledgementToken,
						RequiresResponse = true
					});
			}

			if (!_loginAccepted)
			{
				return Terminal(
					consumed,
					ProtocolParseStatus.CloseSession,
					"login-required");
			}

			if (type == StatusType ||
			    type == HeartbeatType)
			{
				return Message(
					consumed,
					ProtocolParseStatus.Heartbeat,
					new ProtocolMessage
					{
						MessageType =
							ProtocolMessageType.Heartbeat,
						AcknowledgementToken =
							acknowledgementToken,
						RequiresResponse = true
					});
			}

			if (!PositionTypes.Contains(type))
			{
				return Terminal(
					consumed,
					ProtocolParseStatus.Unsupported,
					"message-type-unsupported");
			}

			if (!TryParsePosition(
				    type,
				    payload,
				    out var position,
				    out var metadata))
			{
				return Terminal(
					consumed,
					ProtocolParseStatus.Malformed,
					"gps-payload-invalid");
			}

			position.EventId = CreateEventId(frame);
			return Message(
				consumed,
				ProtocolParseStatus.Positions,
				new ProtocolMessage
				{
					MessageType =
						ProtocolMessageType.Positions,
					Positions =
						new[] { position },
					ProtocolData =
						new Gt06ProtocolData(metadata),
					AcknowledgementToken =
						acknowledgementToken,
					RequiresResponse = true
				});
		}

		public ReadOnlyMemory<byte> BuildResponse(
			ProtocolMessage message,
			TrackingAcceptance acceptance)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));
			if (acceptance == null)
				throw new ArgumentNullException(nameof(acceptance));

			var accepted =
				acceptance.Status ==
				TrackingAcceptanceStatus.Accepted;
			if (message.MessageType ==
			    ProtocolMessageType.Login)
				_loginAccepted = accepted;

			if (!accepted ||
			    message.AcknowledgementToken.Length != 4)
				return ReadOnlyMemory<byte>.Empty;
			if (message.MessageType ==
				    ProtocolMessageType.Positions &&
			    (message.Positions == null ||
			     acceptance.AcceptedPositions !=
			     message.Positions.Count))
			{
				return ReadOnlyMemory<byte>.Empty;
			}

			var token =
				message.AcknowledgementToken.Span;
			return BuildAcknowledgement(
				token[0] == 1,
				token[1],
				BinaryPrimitives.ReadUInt16BigEndian(
					token.Slice(2, 2)));
		}

		public void EnrichPositions(
			ProtocolMessage message,
			UnitTrackingDevice device)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));
			if (device == null)
				throw new ArgumentNullException(nameof(device));
			if (message.ProtocolData is not
				    Gt06ProtocolData protocolData ||
			    message.Positions == null ||
			    message.Positions.Count != 1)
				return;

			var position = message.Positions.Single();
			var alarmCode = MapAlarm(
				protocolData.Metadata.Alarm,
				string.Equals(
					device.ModelKey,
					"jimi-vl103m",
					StringComparison.OrdinalIgnoreCase));
			if (!string.IsNullOrEmpty(alarmCode))
				position.AlarmCode = alarmCode;
		}

		private static bool TryParsePosition(
			byte type,
			ReadOnlySpan<byte> payload,
			out CanonicalTrackingPosition position,
			out Gt06PositionMetadata metadata)
		{
			position = null;
			metadata = new Gt06PositionMetadata();
			if (payload.Length < GpsPayloadLength ||
			    !TryReadTimestamp(
				    payload.Slice(0, 6),
				    out var timestamp))
				return false;

			var satellites = payload[6] & 0x0F;
			var latitude =
				BinaryPrimitives.ReadUInt32BigEndian(
					payload.Slice(7, 4)) /
				1800000m;
			var longitude =
				BinaryPrimitives.ReadUInt32BigEndian(
					payload.Slice(11, 4)) /
				1800000m;
			var speedKilometersPerHour = payload[15];
			var flags =
				BinaryPrimitives.ReadUInt16BigEndian(
					payload.Slice(16, 2));
			if ((flags & (1 << 10)) == 0)
				latitude = -latitude;
			if ((flags & (1 << 11)) != 0)
				longitude = -longitude;
			if (latitude < -90m ||
			    latitude > 90m ||
			    longitude < -180m ||
			    longitude > 180m)
				return false;

			position = new CanonicalTrackingPosition
			{
				TimestampUtc = timestamp,
				ReceivedOnUtc = DateTime.UtcNow,
				Latitude = latitude,
				Longitude = longitude,
				SpeedMetersPerSecond =
					speedKilometersPerHour / 3.6m,
				HeadingDegrees = flags & 0x03FF,
				Satellites = satellites,
				IsMoving =
					speedKilometersPerHour > 0,
				TimestampSource =
					TrackingTimestampSource.Device,
				IsValidFix =
					(flags & (1 << 12)) != 0
			};
			if ((flags & (1 << 14)) != 0)
			{
				position.Ignition =
					(flags & (1 << 15)) != 0;
			}

			if (type == JmVl03GpsType &&
			    payload.Length >= 36)
			{
				position.Ignition = payload[33] > 0;
			}
			else if (CommonStatusPositionTypes.Contains(
				         type) &&
			         payload.Length >= 30)
			{
				var status = payload[26];
				position.Ignition =
					(status & (1 << 1)) != 0;
				position.BatteryPercent =
					NormalizeBattery(payload[27]);
				position.SignalPercent =
					NormalizeSignal(payload[28]);
				metadata.Alarm = payload[29];
				position.AlarmCode =
					MapStatusAlarm(status);
			}
			else if (payload.Length == 29)
			{
				position.Ignition = payload[26] > 0;
			}

			return true;
		}

		private static decimal? NormalizeBattery(
			byte value)
		{
			if (value <= 6)
				return value * 100m / 6m;
			return value <= 100
				? value
				: null;
		}

		private static int? NormalizeSignal(byte value)
		{
			if (value <= 4)
				return value * 25;
			return value <= 100
				? value
				: null;
		}

		private static string MapStatusAlarm(byte status)
		{
			return ((status >> 3) & 0x07) switch
			{
				1 => "vibration",
				2 => "powerCut",
				3 => "lowBattery",
				4 => "sos",
				6 => "geofence",
				7 => "removing",
				_ => null
			};
		}

		private static string MapAlarm(
			byte? value,
			bool modelVl)
		{
			return value switch
			{
				0x01 => "sos",
				0x02 => "powerCut",
				0x03 => "vibration",
				0x04 => "geofenceEnter",
				0x05 => "geofenceExit",
				0x06 => "overspeed",
				0x09 => modelVl ? "tow" : "vibration",
				0x0E or 0x0F => "lowBattery",
				0x11 => "powerOff",
				0x0C or 0x13 or 0x25 =>
					"tampering",
				0x14 => "door",
				0x19 => "lowBattery",
				0x1A or 0x27 => "braking",
				0x1B or 0x2A or 0x2B or 0x2E =>
					"cornering",
				0x23 => "fallDown",
				0x26 => "acceleration",
				0x2C => "accident",
				0x30 => modelVl ? "braking" : "jamming",
				_ => null
			};
		}

		private static bool TryReadImei(
			ReadOnlySpan<byte> payload,
			out string imei)
		{
			imei = null;
			if (payload.Length < 8)
				return false;

			var bcd = Convert.ToHexString(
					payload.Slice(0, 8))
				.ToLowerInvariant();
			if (bcd.Length != 16 ||
			    bcd[0] != '0')
				return false;

			var candidate = bcd.Substring(1);
			if (candidate.Any(
				    value =>
					    value < '0' ||
					    value > '9'))
				return false;

			imei = candidate;
			return true;
		}

		private static bool TryReadTimestamp(
			ReadOnlySpan<byte> value,
			out DateTime timestamp)
		{
			timestamp = default;
			if (value.Length != 6)
				return false;

			try
			{
				timestamp = new DateTime(
					2000 + value[0],
					value[1],
					value[2],
					value[3],
					value[4],
					value[5],
					DateTimeKind.Utc);
				return true;
			}
			catch (ArgumentOutOfRangeException)
			{
				return false;
			}
		}

		private static byte[] CreateAcknowledgementToken(
			bool extended,
			byte type,
			ushort serial)
		{
			var token = new byte[4];
			token[0] = extended ? (byte)1 : (byte)0;
			token[1] = type;
			BinaryPrimitives.WriteUInt16BigEndian(
				token.AsSpan(2),
				serial);
			return token;
		}

		private static byte[] BuildAcknowledgement(
			bool extended,
			byte type,
			ushort serial)
		{
			var lengthFieldBytes = extended ? 2 : 1;
			var response = new byte[
				2 +
				lengthFieldBytes +
				MinimumDeclaredLength +
				2];
			BinaryPrimitives.WriteUInt16BigEndian(
				response,
				extended
					? ExtendedHeader
					: ShortHeader);
			if (extended)
			{
				BinaryPrimitives.WriteUInt16BigEndian(
					response.AsSpan(2, 2),
					MinimumDeclaredLength);
			}
			else
			{
				response[2] =
					MinimumDeclaredLength;
			}

			var typeOffset =
				2 + lengthFieldBytes;
			response[typeOffset] = type;
			BinaryPrimitives.WriteUInt16BigEndian(
				response.AsSpan(
					typeOffset + 1,
					2),
				serial);
			var crcOffset = response.Length - 4;
			BinaryPrimitives.WriteUInt16BigEndian(
				response.AsSpan(crcOffset, 2),
				Gt06Crc16.Compute(
					response.AsSpan(
						2,
						crcOffset - 2)));
			BinaryPrimitives.WriteUInt16BigEndian(
				response.AsSpan(
					response.Length - 2,
					2),
				Tail);
			return response;
		}

		private static string CreateEventId(
			ReadOnlySpan<byte> frame)
		{
			return "gt06:" +
			       Convert.ToHexString(
					       SHA256.HashData(frame))
				       .ToLowerInvariant();
		}

		private static ProtocolParseResult NeedMore(
			ReadOnlySequence<byte> input)
		{
			return new ProtocolParseResult
			{
				Status =
					ProtocolParseStatus.NeedMoreData,
				Consumed = input.Start,
				Examined = input.End
			};
		}

		private static ProtocolParseResult Message(
			SequencePosition consumed,
			ProtocolParseStatus status,
			ProtocolMessage message)
		{
			return new ProtocolParseResult
			{
				Status = status,
				Consumed = consumed,
				Examined = consumed,
				Message = message
			};
		}

		private static ProtocolParseResult Terminal(
			SequencePosition consumed,
			ProtocolParseStatus status,
			string reasonCode)
		{
			return new ProtocolParseResult
			{
				Status = status,
				Consumed = consumed,
				Examined = consumed,
				ReasonCode = reasonCode
			};
		}
	}

	internal sealed class Gt06ProtocolData
	{
		public Gt06ProtocolData(
			Gt06PositionMetadata metadata)
		{
			Metadata = metadata ??
				throw new ArgumentNullException(
					nameof(metadata));
		}

		public Gt06PositionMetadata Metadata { get; }
	}

	internal sealed class Gt06PositionMetadata
	{
		public byte? Alarm { get; set; }
	}
}
