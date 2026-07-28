using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Tracking.Tests.Tools.Teltonika
{
	internal sealed class TeltonikaUdpSimulator :
		IDisposable
	{
		private static readonly TimeSpan OperationTimeout =
			TimeSpan.FromSeconds(2);

		private readonly UdpClient _client;
		private bool _disposed;

		public TeltonikaUdpSimulator(
			IPAddress address,
			int port)
		{
			if (address == null)
				throw new ArgumentNullException(nameof(address));

			_client = new UdpClient(
				address.AddressFamily);
			_client.Connect(address, port);
		}

		public async Task<TeltonikaUdpAcknowledgement>
			SendDatagramAsync(
				ReadOnlyMemory<byte> datagram,
				CancellationToken cancellationToken = default)
		{
			await WriteDatagramAsync(
				datagram,
				cancellationToken);
			return await ReadAcknowledgementAsync(
				cancellationToken);
		}

		public async Task WriteDatagramAsync(
			ReadOnlyMemory<byte> datagram,
			CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			if (datagram.IsEmpty)
				throw new ArgumentException(
					"A Teltonika datagram is required.",
					nameof(datagram));

			using var operationCancellation =
				CreateOperationCancellation(
					cancellationToken);
			await _client.SendAsync(
				datagram,
				operationCancellation.Token);
		}

		public async Task<TeltonikaUdpAcknowledgement>
			ReadAcknowledgementAsync(
				CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			using var operationCancellation =
				CreateOperationCancellation(
					cancellationToken);
			var response = await _client.ReceiveAsync(
				operationCancellation.Token);
			if (response.Buffer.Length != 7 ||
			    BinaryPrimitives.ReadUInt16BigEndian(
				    response.Buffer) != 5 ||
			    response.Buffer[4] != 0x01)
			{
				throw new InvalidOperationException(
					"The gateway returned an invalid Teltonika UDP acknowledgement.");
			}

			return new TeltonikaUdpAcknowledgement(
				BinaryPrimitives.ReadUInt16BigEndian(
					response.Buffer.AsSpan(2, 2)),
				response.Buffer[5],
				response.Buffer[6]);
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			_client.Dispose();
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
					nameof(TeltonikaUdpSimulator));
		}
	}

	internal readonly struct TeltonikaUdpAcknowledgement
	{
		public TeltonikaUdpAcknowledgement(
			ushort channelPacketId,
			byte avlPacketId,
			byte acceptedRecords)
		{
			ChannelPacketId = channelPacketId;
			AvlPacketId = avlPacketId;
			AcceptedRecords = acceptedRecords;
		}

		public ushort ChannelPacketId { get; }
		public byte AvlPacketId { get; }
		public byte AcceptedRecords { get; }
	}
}
