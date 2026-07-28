using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Tracking.Tests.Tools.Teltonika
{
	internal sealed class TeltonikaTcpSimulator :
		IAsyncDisposable
	{
		private static readonly TimeSpan OperationTimeout =
			TimeSpan.FromSeconds(2);

		private readonly TcpClient _client;
		private readonly NetworkStream _stream;
		private bool _disposed;

		private TeltonikaTcpSimulator(TcpClient client)
		{
			_client = client;
			_stream = client.GetStream();
		}

		public static async Task<TeltonikaTcpSimulator>
			ConnectAndLoginAsync(
				IPAddress address,
				int port,
				string imei,
				CancellationToken cancellationToken = default)
		{
			if (address == null)
				throw new ArgumentNullException(nameof(address));
			if (string.IsNullOrWhiteSpace(imei) ||
			    imei.Length != 15)
			{
				throw new ArgumentException(
					"A Teltonika IMEI must contain exactly 15 digits.",
					nameof(imei));
			}

			foreach (var character in imei)
			{
				if (character < '0' || character > '9')
				{
					throw new ArgumentException(
						"A Teltonika IMEI must contain only digits.",
						nameof(imei));
				}
			}

			var client = new TcpClient(
				address.AddressFamily);
			try
			{
				using var operationCancellation =
					CreateOperationCancellation(
						cancellationToken);
				await client.ConnectAsync(
					address,
					port,
					operationCancellation.Token);
				var simulator =
					new TeltonikaTcpSimulator(client);
				await simulator.LoginAsync(
					imei,
					operationCancellation.Token);
				return simulator;
			}
			catch
			{
				client.Dispose();
				throw;
			}
		}

		public async Task<uint> SendFrameAsync(
			ReadOnlyMemory<byte> frame,
			int fragmentSize = 0,
			CancellationToken cancellationToken = default)
		{
			await WriteFrameAsync(
				frame,
				fragmentSize,
				cancellationToken);
			return await ReadAcceptedRecordCountAsync(
				cancellationToken);
		}

		public async Task WriteFrameAsync(
			ReadOnlyMemory<byte> frame,
			int fragmentSize = 0,
			CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			if (frame.IsEmpty)
				throw new ArgumentException(
					"A Teltonika frame is required.",
					nameof(frame));
			if (fragmentSize < 0)
				throw new ArgumentOutOfRangeException(
					nameof(fragmentSize));

			using var operationCancellation =
				CreateOperationCancellation(
					cancellationToken);
			var size = fragmentSize == 0
				? frame.Length
				: fragmentSize;
			for (var offset = 0;
			     offset < frame.Length;
			     offset += size)
			{
				var count = Math.Min(
					size,
					frame.Length - offset);
				await _stream.WriteAsync(
					frame.Slice(offset, count),
					operationCancellation.Token);
				if (fragmentSize > 0)
					await Task.Yield();
			}
		}

		public async Task<uint>
			ReadAcceptedRecordCountAsync(
				CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			var response = new byte[4];
			using var operationCancellation =
				CreateOperationCancellation(
					cancellationToken);
			await ReadExactlyAsync(
				response,
				operationCancellation.Token);
			return BinaryPrimitives.ReadUInt32BigEndian(
				response);
		}

		public async Task DisconnectMidFrameAsync(
			ReadOnlyMemory<byte> frame,
			int bytesToSend,
			CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			if (bytesToSend <= 0 ||
			    bytesToSend >= frame.Length)
			{
				throw new ArgumentOutOfRangeException(
					nameof(bytesToSend),
					"The partial frame length must be within the frame.");
			}

			await WriteFrameAsync(
				frame.Slice(0, bytesToSend),
				cancellationToken: cancellationToken);
			await DisposeAsync();
		}

		public async ValueTask DisposeAsync()
		{
			if (_disposed)
				return;

			_disposed = true;
			await _stream.DisposeAsync();
			_client.Dispose();
		}

		private async Task LoginAsync(
			string imei,
			CancellationToken cancellationToken)
		{
			var imeiBytes = Encoding.ASCII.GetBytes(imei);
			var login = new byte[imeiBytes.Length + 2];
			BinaryPrimitives.WriteUInt16BigEndian(
				login,
				(ushort)imeiBytes.Length);
			imeiBytes.CopyTo(login, 2);
			await _stream.WriteAsync(
				login,
				cancellationToken);

			var response = new byte[1];
			await ReadExactlyAsync(
				response,
				cancellationToken);
			if (response[0] != 0x01)
			{
				throw new InvalidDataException(
					"The gateway rejected the Teltonika IMEI login.");
			}
		}

		private async Task ReadExactlyAsync(
			Memory<byte> destination,
			CancellationToken cancellationToken)
		{
			var offset = 0;
			while (offset < destination.Length)
			{
				var read = await _stream.ReadAsync(
					destination.Slice(offset),
					cancellationToken);
				if (read == 0)
					throw new EndOfStreamException();
				offset += read;
			}
		}

		private static CancellationTokenSource
			CreateOperationCancellation(
				CancellationToken cancellationToken)
		{
			var operationCancellation =
				CancellationTokenSource
					.CreateLinkedTokenSource(
						cancellationToken);
			operationCancellation.CancelAfter(
				OperationTimeout);
			return operationCancellation;
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
				throw new ObjectDisposedException(
					nameof(TeltonikaTcpSimulator));
		}
	}

	internal static class TeltonikaFixtureBuilder
	{
		public static byte[] BuildTimestampedBatch(
			byte[] singleRecordFrame,
			params DateTime[] timestampsUtc)
		{
			if (singleRecordFrame == null)
			{
				throw new ArgumentNullException(
					nameof(singleRecordFrame));
			}
			if (timestampsUtc == null ||
			    timestampsUtc.Length == 0 ||
			    timestampsUtc.Length > byte.MaxValue)
			{
				throw new ArgumentException(
					"At least one timestamp and no more than 255 timestamps are required.",
					nameof(timestampsUtc));
			}

			var data = GetData(singleRecordFrame);
			if (data.Length < 4 ||
			    data[1] != 1 ||
			    data[^1] != 1)
			{
				throw new ArgumentException(
					"The template must contain exactly one AVL record.",
					nameof(singleRecordFrame));
			}

			var record = data.AsSpan(2, data.Length - 3);
			if (record.Length < sizeof(long))
			{
				throw new ArgumentException(
					"The AVL record is too short to contain a timestamp.",
					nameof(singleRecordFrame));
			}

			var batchData = new byte[
				2 +
				(record.Length * timestampsUtc.Length) +
				1];
			batchData[0] = data[0];
			batchData[1] = (byte)timestampsUtc.Length;
			for (var index = 0;
			     index < timestampsUtc.Length;
			     index++)
			{
				var destination = batchData.AsSpan(
					2 + (record.Length * index),
					record.Length);
				record.CopyTo(destination);
				var timestamp = timestampsUtc[index];
				if (timestamp.Kind != DateTimeKind.Utc)
				{
					throw new ArgumentException(
						"Simulator timestamps must be UTC.",
						nameof(timestampsUtc));
				}

				BinaryPrimitives.WriteInt64BigEndian(
					destination,
					new DateTimeOffset(timestamp)
						.ToUnixTimeMilliseconds());
			}

			batchData[^1] =
				(byte)timestampsUtc.Length;
			return BuildFrame(batchData);
		}

		public static byte[] CorruptCrc(byte[] frame)
		{
			if (frame == null)
				throw new ArgumentNullException(nameof(frame));
			if (frame.Length < 12)
			{
				throw new ArgumentException(
					"The Teltonika frame is too short.",
					nameof(frame));
			}

			var corrupted = (byte[])frame.Clone();
			corrupted[^1] ^= 0xFF;
			return corrupted;
		}

		public static byte[] WrapUdp(
			byte[] tcpFrame,
			string imei,
			ushort channelPacketId,
			byte avlPacketId)
		{
			if (tcpFrame == null)
				throw new ArgumentNullException(nameof(tcpFrame));
			if (string.IsNullOrWhiteSpace(imei) ||
			    imei.Length != 15)
			{
				throw new ArgumentException(
					"A Teltonika IMEI must contain exactly 15 digits.",
					nameof(imei));
			}

			var imeiBytes = Encoding.ASCII.GetBytes(imei);
			foreach (var character in imeiBytes)
			{
				if (character < (byte)'0' ||
				    character > (byte)'9')
				{
					throw new ArgumentException(
						"A Teltonika IMEI must contain only digits.",
						nameof(imei));
				}
			}

			var data = GetData(tcpFrame);
			var packetLength =
				sizeof(ushort) +
				sizeof(byte) +
				sizeof(byte) +
				sizeof(ushort) +
				imeiBytes.Length +
				data.Length;
			if (packetLength > ushort.MaxValue)
			{
				throw new ArgumentException(
					"The Teltonika UDP packet is too large.",
					nameof(tcpFrame));
			}

			var datagram = new byte[
				sizeof(ushort) + packetLength];
			BinaryPrimitives.WriteUInt16BigEndian(
				datagram,
				(ushort)packetLength);
			BinaryPrimitives.WriteUInt16BigEndian(
				datagram.AsSpan(2, 2),
				channelPacketId);
			datagram[4] = 0x01;
			datagram[5] = avlPacketId;
			BinaryPrimitives.WriteUInt16BigEndian(
				datagram.AsSpan(6, 2),
				(ushort)imeiBytes.Length);
			imeiBytes.CopyTo(datagram, 8);
			data.CopyTo(
				datagram,
				8 + imeiBytes.Length);
			return datagram;
		}

		private static byte[] GetData(byte[] frame)
		{
			if (frame.Length < 12)
			{
				throw new ArgumentException(
					"The Teltonika frame is too short.",
					nameof(frame));
			}

			var dataLength =
				(int)BinaryPrimitives.ReadUInt32BigEndian(
					frame.AsSpan(4, 4));
			if (dataLength <= 0 ||
			    dataLength != frame.Length - 12)
			{
				throw new ArgumentException(
					"The Teltonika frame data length is invalid.",
					nameof(frame));
			}

			return frame.AsSpan(8, dataLength)
				.ToArray();
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
	}
}
