using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Resgrid.Model;

namespace Resgrid.Providers.Tracking.Protocols.Teltonika
{
	public sealed class TeltonikaCodec8UdpProtocolSession :
		ITrackingProtocolSession,
		ITrackingProtocolPositionEnricher
	{
		private const int ImeiLength = 15;
		private const int DatagramLengthFieldBytes = 2;
		private const int MinimumDataFieldLength = 3;
		private const int MinimumPacketLength =
			2 +
			1 +
			1 +
			2 +
			ImeiLength +
			MinimumDataFieldLength;
		private const byte ChannelMarker = 0x01;
		private const ushort ResponsePacketLength = 5;

		private readonly int _maximumFrameBytes;

		public TeltonikaCodec8UdpProtocolSession(
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
			if (input.Length <
			    DatagramLengthFieldBytes)
				return NeedMore(input);

			var reader = new SequenceReader<byte>(input);
			if (!TryReadUInt16(
				    ref reader,
				    out var packetLength))
				return NeedMore(input);

			var frameLength =
				(long)DatagramLengthFieldBytes +
				packetLength;
			if (frameLength > _maximumFrameBytes)
			{
				return Terminal(
					input.End,
					ProtocolParseStatus.Malformed,
					"frame-too-large");
			}
			if (input.Length < frameLength)
				return NeedMore(input);
			if (input.Length != frameLength)
			{
				return Terminal(
					input.End,
					ProtocolParseStatus.Malformed,
					"datagram-length-mismatch");
			}
			if (packetLength < MinimumPacketLength)
			{
				return Terminal(
					input.End,
					ProtocolParseStatus.Malformed,
					"data-length-invalid");
			}

			if (!TryReadUInt16(
				    ref reader,
				    out var channelPacketId) ||
			    !reader.TryRead(out var channelMarker) ||
			    channelMarker != ChannelMarker ||
			    !reader.TryRead(out var avlPacketId) ||
			    !TryReadUInt16(
				    ref reader,
				    out var imeiLength))
			{
				return Terminal(
					input.End,
					ProtocolParseStatus.Malformed,
					"udp-header-invalid");
			}
			if (imeiLength != ImeiLength ||
			    reader.Remaining <
			    imeiLength +
			    MinimumDataFieldLength)
			{
				return Terminal(
					input.End,
					ProtocolParseStatus.Malformed,
					"imei-length-invalid");
			}

			var imeiBytes = input
				.Slice(
					reader.Position,
					imeiLength)
				.ToArray();
			for (var index = 0;
			     index < imeiBytes.Length;
			     index++)
			{
				if (imeiBytes[index] < (byte)'0' ||
				    imeiBytes[index] > (byte)'9')
				{
					return Terminal(
						input.End,
						ProtocolParseStatus.Malformed,
						"imei-invalid");
				}
			}

			reader.Advance(imeiLength);
			var avlData =
				TeltonikaCodec8ProtocolSession
					.ParseDataField(
						input.Slice(
							reader.Position));
			if (avlData.Status !=
			    ProtocolParseStatus.Positions)
			{
				return Terminal(
					input.End,
					avlData.Status,
					avlData.ReasonCode);
			}

			var acknowledgementToken = new byte[3];
			BinaryPrimitives.WriteUInt16BigEndian(
				acknowledgementToken,
				channelPacketId);
			acknowledgementToken[2] = avlPacketId;
			return new ProtocolParseResult
			{
				Status = ProtocolParseStatus.Positions,
				Consumed = input.End,
				Examined = input.End,
				Message = new ProtocolMessage
				{
					MessageType =
						ProtocolMessageType.Positions,
					ExternalIdentifier =
						Encoding.ASCII.GetString(
							imeiBytes),
					Positions = avlData.Positions,
					ProtocolData = avlData.ProtocolData,
					AcknowledgementToken =
						acknowledgementToken,
					RequiresResponse = true
				}
			};
		}

		public void EnrichPositions(
			ProtocolMessage message,
			UnitTrackingDevice device)
		{
			TeltonikaIoMapper.EnrichPositions(
				message,
				device);
		}

		public ReadOnlyMemory<byte> BuildResponse(
			ProtocolMessage message,
			TrackingAcceptance acceptance)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));
			if (acceptance == null)
				throw new ArgumentNullException(nameof(acceptance));
			if (message.MessageType !=
				    ProtocolMessageType.Positions ||
			    message.AcknowledgementToken.Length != 3)
				return ReadOnlyMemory<byte>.Empty;

			byte acceptedRecords = 0;
			if (acceptance.Status ==
				    TrackingAcceptanceStatus.Accepted &&
			    message.Positions != null &&
			    acceptance.AcceptedPositions ==
			    message.Positions.Count &&
			    acceptance.AcceptedPositions <=
			    byte.MaxValue)
			{
				acceptedRecords =
					(byte)acceptance.AcceptedPositions;
			}

			var response = new byte[
				DatagramLengthFieldBytes +
				ResponsePacketLength];
			BinaryPrimitives.WriteUInt16BigEndian(
				response,
				ResponsePacketLength);
			message.AcknowledgementToken.Span
				.Slice(0, 2)
				.CopyTo(
					response.AsSpan(2, 2));
			response[4] = ChannelMarker;
			response[5] =
				message.AcknowledgementToken.Span[2];
			response[6] = acceptedRecords;
			return response;
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
}
