using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Resgrid.Model;
using Resgrid.Model.Tracking;

namespace Resgrid.Providers.Tracking.Protocols.Teltonika
{
	public sealed class TeltonikaCodec8ProtocolSession :
		ITrackingProtocolSession,
		ITrackingProtocolPositionEnricher
	{
		private const byte Codec8 = 0x08;
		private const byte Codec8Extended = 0x8E;
		private const int ImeiLength = 15;
		private const int AvlHeaderLength = 8;
		private const int AvlCrcLength = 4;
		private const int MinimumDataFieldLength = 3;
		private const int MaximumIoElementsPerRecord = 1024;
		private const int MaximumVariableIoValueBytes = 4096;
		private const decimal CoordinateScale = 10000000m;

		private readonly int _maximumFrameBytes;
		private bool _loginAccepted;

		public TeltonikaCodec8ProtocolSession(
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
			return _loginAccepted
				? ParseAvlPacket(input)
				: ParseLogin(input);
		}

		public ReadOnlyMemory<byte> BuildResponse(
			ProtocolMessage message,
			TrackingAcceptance acceptance)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));
			if (acceptance == null)
				throw new ArgumentNullException(nameof(acceptance));

			if (message.MessageType == ProtocolMessageType.Login)
			{
				_loginAccepted =
					acceptance.Status ==
					TrackingAcceptanceStatus.Accepted;
				return new byte[]
				{
					_loginAccepted ? (byte)0x01 : (byte)0x00
				};
			}

			if (message.MessageType !=
			    ProtocolMessageType.Positions)
				return ReadOnlyMemory<byte>.Empty;

			var acceptedRecords = 0;
			if (acceptance.Status ==
				    TrackingAcceptanceStatus.Accepted &&
			    message.Positions != null &&
			    acceptance.AcceptedPositions ==
			    message.Positions.Count)
			{
				acceptedRecords =
					acceptance.AcceptedPositions;
			}

			var response = new byte[sizeof(int)];
			BinaryPrimitives.WriteInt32BigEndian(
				response,
				acceptedRecords);
			return response;
		}

		public void EnrichPositions(
			ProtocolMessage message,
			UnitTrackingDevice device)
		{
			TeltonikaIoMapper.EnrichPositions(
				message,
				device);
		}

		private ProtocolParseResult ParseLogin(
			ReadOnlySequence<byte> input)
		{
			if (input.Length < sizeof(ushort))
				return NeedMore(input);

			var reader = new SequenceReader<byte>(input);
			if (!TryReadUInt16(
				    ref reader,
				    out var imeiLength))
				return NeedMore(input);
			if (imeiLength != ImeiLength)
			{
				return Terminal(
					input,
					input.End,
					ProtocolParseStatus.Malformed,
					"imei-length-invalid");
			}
			if (reader.Remaining < imeiLength)
				return NeedMore(input);

			var imeiBytes = input
				.Slice(reader.Position, imeiLength)
				.ToArray();
			for (var index = 0;
			     index < imeiBytes.Length;
			     index++)
			{
				if (imeiBytes[index] < (byte)'0' ||
				    imeiBytes[index] > (byte)'9')
				{
					return Terminal(
						input,
						input.GetPosition(
							sizeof(ushort) + imeiLength),
						ProtocolParseStatus.Malformed,
						"imei-invalid");
				}
			}

			var consumed = input.GetPosition(
				sizeof(ushort) + imeiLength);
			return new ProtocolParseResult
			{
				Status = ProtocolParseStatus.Login,
				Consumed = consumed,
				Examined = consumed,
				Message = new ProtocolMessage
				{
					MessageType =
						ProtocolMessageType.Login,
					ExternalIdentifier =
						Encoding.ASCII.GetString(
							imeiBytes),
					RequiresResponse = true
				}
			};
		}

		private ProtocolParseResult ParseAvlPacket(
			ReadOnlySequence<byte> input)
		{
			if (input.Length < AvlHeaderLength)
				return NeedMore(input);

			var headerReader =
				new SequenceReader<byte>(input);
			if (!TryReadUInt32(
				    ref headerReader,
				    out var preamble) ||
			    !TryReadUInt32(
				    ref headerReader,
				    out var dataFieldLength))
				return NeedMore(input);
			if (preamble != 0)
			{
				return Terminal(
					input,
					input.End,
					ProtocolParseStatus.Malformed,
					"preamble-invalid");
			}
			if (dataFieldLength <
			    MinimumDataFieldLength)
			{
				return Terminal(
					input,
					input.End,
					ProtocolParseStatus.Malformed,
					"data-length-invalid");
			}

			var frameLength =
				(long)AvlHeaderLength +
				dataFieldLength +
				AvlCrcLength;
			if (frameLength > _maximumFrameBytes)
			{
				return Terminal(
					input,
					input.End,
					ProtocolParseStatus.Malformed,
					"frame-too-large");
			}
			if (input.Length < frameLength)
				return NeedMore(input);

			var consumed = input.GetPosition(
				frameLength);
			var dataField = input.Slice(
				AvlHeaderLength,
				dataFieldLength);
			if (!TryReadExpectedCrc(
				    input,
				    dataFieldLength,
				    out var expectedCrc) ||
			    TeltonikaCrc16.Compute(dataField) !=
			    expectedCrc)
			{
				return Terminal(
					input,
					consumed,
					ProtocolParseStatus.Malformed,
					"crc-invalid");
			}

			var avlData = ParseDataField(dataField);
			if (avlData.Status !=
			    ProtocolParseStatus.Positions)
			{
				return Terminal(
					input,
					consumed,
					avlData.Status,
					avlData.ReasonCode);
			}

			return new ProtocolParseResult
			{
				Status = ProtocolParseStatus.Positions,
				Consumed = consumed,
				Examined = consumed,
				Message = new ProtocolMessage
				{
					MessageType =
						ProtocolMessageType.Positions,
					Positions = avlData.Positions,
					ProtocolData = avlData.ProtocolData,
					AcknowledgementToken =
						new byte[] { avlData.RecordCount },
					RequiresResponse = true
				}
			};
		}

		internal static TeltonikaAvlDataParseResult
			ParseDataField(
				ReadOnlySequence<byte> dataField)
		{
			var dataReader =
				new SequenceReader<byte>(dataField);
			if (!dataReader.TryRead(out var codec))
			{
				return TeltonikaAvlDataParseResult.Failure(
					ProtocolParseStatus.Malformed,
					"codec-required");
			}
			if (codec != Codec8 &&
			    codec != Codec8Extended)
			{
				return TeltonikaAvlDataParseResult.Failure(
					ProtocolParseStatus.Unsupported,
					"codec-unsupported");
			}
			if (!dataReader.TryRead(
				    out var recordCount) ||
			    recordCount == 0)
			{
				return TeltonikaAvlDataParseResult.Failure(
					ProtocolParseStatus.Malformed,
					"record-count-invalid");
			}

			var positions =
				new List<CanonicalTrackingPosition>(
					recordCount);
			var ioRecords =
				new List<IReadOnlyDictionary<int, ulong>>(
					recordCount);
			var receivedOn = DateTime.UtcNow;
			for (var recordIndex = 0;
			     recordIndex < recordCount;
			     recordIndex++)
			{
				var recordStart = dataReader.Position;
				if (!TryReadPosition(
					    ref dataReader,
					    codec,
					    receivedOn,
					    out var position,
					    out var ioValues))
				{
					return TeltonikaAvlDataParseResult
						.Failure(
							ProtocolParseStatus.Malformed,
							"record-invalid");
				}

				position.EventId = CreateEventId(
					codec,
					dataField.Slice(
						recordStart,
						dataReader.Position));
				positions.Add(position);
				ioRecords.Add(ioValues);
			}

			if (!dataReader.TryRead(
				    out var repeatedRecordCount) ||
			    repeatedRecordCount != recordCount ||
			    dataReader.Remaining != 0)
			{
				return TeltonikaAvlDataParseResult.Failure(
					ProtocolParseStatus.Malformed,
					"record-count-mismatch");
			}

			return new TeltonikaAvlDataParseResult
			{
				Status = ProtocolParseStatus.Positions,
				Positions = positions,
				ProtocolData =
					new TeltonikaProtocolData(ioRecords),
				RecordCount = recordCount
			};
		}

		private static bool TryReadPosition(
			ref SequenceReader<byte> reader,
			byte codec,
			DateTime receivedOn,
			out CanonicalTrackingPosition position,
			out IReadOnlyDictionary<int, ulong> ioValues)
		{
			position = null;
			ioValues = null;
			if (!reader.TryReadBigEndian(
				    out long timestampMilliseconds) ||
			    timestampMilliseconds < 0 ||
			    !reader.TryRead(out var priority) ||
			    priority > 2 ||
			    !reader.TryReadBigEndian(
				    out int longitudeValue) ||
			    !reader.TryReadBigEndian(
				    out int latitudeValue) ||
			    !TryReadUInt16(
				    ref reader,
				    out var altitude) ||
			    !TryReadUInt16(
				    ref reader,
				    out var angle) ||
			    angle > 360 ||
			    !reader.TryRead(out var satellites) ||
			    !TryReadUInt16(
				    ref reader,
				    out var speedKilometersPerHour))
				return false;

			DateTime timestamp;
			try
			{
				timestamp = DateTimeOffset
					.FromUnixTimeMilliseconds(
						timestampMilliseconds)
					.UtcDateTime;
			}
			catch (ArgumentOutOfRangeException)
			{
				return false;
			}

			var longitude =
				longitudeValue / CoordinateScale;
			var latitude =
				latitudeValue / CoordinateScale;
			if (longitude < -180m ||
			    longitude > 180m ||
			    latitude < -90m ||
			    latitude > 90m)
				return false;

			if (!TryReadIoElements(
				    ref reader,
				    codec,
				    out ioValues))
				return false;

			position = new CanonicalTrackingPosition
			{
				TimestampUtc = timestamp,
				ReceivedOnUtc = receivedOn,
				Latitude = latitude,
				Longitude = longitude,
				AltitudeMeters = altitude,
				SpeedMetersPerSecond =
					speedKilometersPerHour / 3.6m,
				HeadingDegrees = angle,
				Satellites = satellites,
				TimestampSource =
					TrackingTimestampSource.Device,
				IsValidFix = satellites > 0
			};
			return true;
		}

		private static bool TryReadIoElements(
			ref SequenceReader<byte> reader,
			byte codec,
			out IReadOnlyDictionary<int, ulong> ioValues)
		{
			ioValues = null;
			var extended = codec ==
			               Codec8Extended;
			if (!TryReadCount(
				    ref reader,
				    extended,
				    out _) ||
			    !TryReadCount(
				    ref reader,
				    extended,
				    out var totalCount) ||
			    totalCount >
			    MaximumIoElementsPerRecord)
				return false;

			var capturedValues =
				new Dictionary<int, ulong>();
			var parsedCount = 0;
			if (!TryReadFixedIoGroup(
				    ref reader,
				    extended,
				    1,
				    capturedValues,
				    ref parsedCount) ||
			    !TryReadFixedIoGroup(
				    ref reader,
				    extended,
				    2,
				    capturedValues,
				    ref parsedCount) ||
			    !TryReadFixedIoGroup(
				    ref reader,
				    extended,
				    4,
				    capturedValues,
				    ref parsedCount) ||
			    !TryReadFixedIoGroup(
				    ref reader,
				    extended,
				    8,
				    capturedValues,
				    ref parsedCount))
				return false;

			if (extended)
			{
				if (!TryReadUInt16(
					    ref reader,
					    out var variableCount) ||
				    parsedCount + variableCount >
				    MaximumIoElementsPerRecord)
					return false;

				for (var index = 0;
				     index < variableCount;
				     index++)
				{
					if (!TryReadUInt16(
						    ref reader,
						    out _) ||
					    !TryReadUInt16(
						    ref reader,
						    out var valueLength) ||
					    valueLength >
					    MaximumVariableIoValueBytes ||
					    !TryAdvance(
						    ref reader,
						    valueLength))
						return false;
				}

				parsedCount += variableCount;
			}

			if (parsedCount != totalCount)
				return false;

			ioValues = capturedValues;
			return true;
		}

		private static bool TryReadFixedIoGroup(
			ref SequenceReader<byte> reader,
			bool extended,
			int valueLength,
			IDictionary<int, ulong> capturedValues,
			ref int parsedCount)
		{
			if (!TryReadCount(
				    ref reader,
				    extended,
				    out var count) ||
			    parsedCount + count >
			    MaximumIoElementsPerRecord)
				return false;

			for (var index = 0;
			     index < count;
			     index++)
			{
				if (!TryReadIoId(
					    ref reader,
					    extended,
					    out var avlId) ||
				    !TryReadUnsigned(
					    ref reader,
					    valueLength,
					    out var rawValue))
					return false;

				if (TeltonikaIoMapper.IsAllowlisted(
					    avlId,
					    valueLength))
					capturedValues[avlId] = rawValue;
			}

			parsedCount += count;
			return true;
		}

		private static bool TryReadIoId(
			ref SequenceReader<byte> reader,
			bool extended,
			out int avlId)
		{
			if (!extended)
			{
				if (!reader.TryRead(out var byteId))
				{
					avlId = 0;
					return false;
				}

				avlId = byteId;
				return true;
			}

			if (!TryReadUInt16(
				    ref reader,
				    out var shortId))
			{
				avlId = 0;
				return false;
			}

			avlId = shortId;
			return true;
		}

		private static bool TryReadUnsigned(
			ref SequenceReader<byte> reader,
			int length,
			out ulong value)
		{
			value = 0;
			for (var index = 0;
			     index < length;
			     index++)
			{
				if (!reader.TryRead(out var next))
				return false;
				value = (value << 8) | next;
			}

			return true;
		}

		private static bool TryReadCount(
			ref SequenceReader<byte> reader,
			bool extended,
			out int count)
		{
			count = 0;
			if (!extended)
			{
				if (!reader.TryRead(out var byteCount))
					return false;
				count = byteCount;
				return true;
			}

			if (!TryReadUInt16(
				    ref reader,
				    out var shortCount))
				return false;
			count = shortCount;
			return true;
		}

		private static bool TryReadExpectedCrc(
			ReadOnlySequence<byte> input,
			uint dataFieldLength,
			out ushort expectedCrc)
		{
			var crcReader = new SequenceReader<byte>(
				input.Slice(
					AvlHeaderLength +
					dataFieldLength,
					AvlCrcLength));
			if (!TryReadUInt32(
				    ref crcReader,
				    out var crcValue) ||
			    crcValue > ushort.MaxValue)
			{
				expectedCrc = 0;
				return false;
			}

			expectedCrc = (ushort)crcValue;
			return true;
		}

		private static bool TryReadUInt16(
			ref SequenceReader<byte> reader,
			out ushort value)
		{
			if (!reader.TryReadBigEndian(
				    out short signedValue))
			{
				value = 0;
				return false;
			}

			value = unchecked((ushort)signedValue);
			return true;
		}

		private static bool TryReadUInt32(
			ref SequenceReader<byte> reader,
			out uint value)
		{
			if (!reader.TryReadBigEndian(
				    out int signedValue))
			{
				value = 0;
				return false;
			}

			value = unchecked((uint)signedValue);
			return true;
		}

		private static bool TryAdvance(
			ref SequenceReader<byte> reader,
			int length)
		{
			if (length < 0 ||
			    reader.Remaining < length)
				return false;

			reader.Advance(length);
			return true;
		}

		private static string CreateEventId(
			byte codec,
			ReadOnlySequence<byte> record)
		{
			using var hash = IncrementalHash.CreateHash(
				HashAlgorithmName.SHA256);
			hash.AppendData(new[] { codec });
			foreach (var segment in record)
				hash.AppendData(segment.Span);
			return "teltonika:" +
			       Convert.ToHexString(
					       hash.GetHashAndReset())
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

		private static ProtocolParseResult Terminal(
			ReadOnlySequence<byte> input,
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

	internal sealed class TeltonikaAvlDataParseResult
	{
		public ProtocolParseStatus Status { get; set; }
		public IReadOnlyCollection<CanonicalTrackingPosition>
			Positions { get; set; } =
				Array.Empty<CanonicalTrackingPosition>();
		public byte RecordCount { get; set; }
		public TeltonikaProtocolData ProtocolData { get; set; }
		public string ReasonCode { get; set; }

		public static TeltonikaAvlDataParseResult Failure(
			ProtocolParseStatus status,
			string reasonCode)
		{
			return new TeltonikaAvlDataParseResult
			{
				Status = status,
				ReasonCode = reasonCode
			};
		}
	}
}
