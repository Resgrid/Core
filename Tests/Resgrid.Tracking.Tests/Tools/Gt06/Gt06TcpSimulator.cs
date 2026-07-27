using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Tracking.Tests.Tools.Gt06
{
	internal sealed class Gt06TcpSimulator :
		IAsyncDisposable
	{
		private static readonly TimeSpan OperationTimeout =
			TimeSpan.FromSeconds(2);

		private readonly TcpClient _client;
		private readonly NetworkStream _stream;
		private bool _disposed;

		private Gt06TcpSimulator(TcpClient client)
		{
			_client = client;
			_stream = client.GetStream();
		}

		public static async Task<Gt06TcpSimulator>
			ConnectAsync(
				IPAddress address,
				int port,
				CancellationToken cancellationToken = default)
		{
			if (address == null)
				throw new ArgumentNullException(nameof(address));

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
				return new Gt06TcpSimulator(client);
			}
			catch
			{
				client.Dispose();
				throw;
			}
		}

		public async Task<byte[]> SendFrameAsync(
			ReadOnlyMemory<byte> frame,
			int fragmentSize = 0,
			CancellationToken cancellationToken = default)
		{
			await WriteFrameAsync(
				frame,
				fragmentSize,
				cancellationToken);
			return await ReadResponseAsync(
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
					"A GT06 frame is required.",
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

		public async Task<byte[]> ReadResponseAsync(
			CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			using var operationCancellation =
				CreateOperationCancellation(
					cancellationToken);
			var prefix = new byte[3];
			await ReadExactlyAsync(
				prefix,
				operationCancellation.Token);
			var header = BinaryPrimitives
				.ReadUInt16BigEndian(prefix);
			var extended = header == 0x7979;
			if (header != 0x7878 &&
			    !extended)
			{
				throw new InvalidDataException(
					"The GT06 response header is invalid.");
			}

			var lengthFieldBytes = extended ? 2 : 1;
			var responsePrefix = extended
				? new byte[4]
				: prefix;
			if (extended)
			{
				prefix.CopyTo(responsePrefix, 0);
				await ReadExactlyAsync(
					responsePrefix.AsMemory(3, 1),
					operationCancellation.Token);
			}

			var declaredLength = extended
				? BinaryPrimitives.ReadUInt16BigEndian(
					responsePrefix.AsSpan(2, 2))
				: responsePrefix[2];
			var response = new byte[
				2 +
				lengthFieldBytes +
				declaredLength +
				2];
			responsePrefix.CopyTo(response, 0);
			await ReadExactlyAsync(
				response.AsMemory(responsePrefix.Length),
				operationCancellation.Token);
			return response;
		}

		public async ValueTask DisposeAsync()
		{
			if (_disposed)
				return;

			_disposed = true;
			await _stream.DisposeAsync();
			_client.Dispose();
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
					nameof(Gt06TcpSimulator));
		}
	}
}
