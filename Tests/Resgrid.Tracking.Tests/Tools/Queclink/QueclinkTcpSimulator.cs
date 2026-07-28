using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Tracking.Tests.Tools.Queclink
{
	internal sealed class QueclinkTcpSimulator :
		IAsyncDisposable
	{
		private static readonly TimeSpan OperationTimeout =
			TimeSpan.FromSeconds(2);

		private readonly TcpClient _client;
		private readonly NetworkStream _stream;
		private bool _disposed;

		private QueclinkTcpSimulator(TcpClient client)
		{
			_client = client;
			_stream = client.GetStream();
		}

		public static async Task<QueclinkTcpSimulator>
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
				return new QueclinkTcpSimulator(client);
			}
			catch
			{
				client.Dispose();
				throw;
			}
		}

		public async Task SendFrameAsync(
			ReadOnlyMemory<byte> frame,
			int fragmentSize = 0,
			CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			if (frame.IsEmpty)
				throw new ArgumentException(
					"A Queclink frame is required.",
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
			using var response = new MemoryStream();
			var buffer = new byte[1];
			while (response.Length < 1024)
			{
				var read = await _stream.ReadAsync(
					buffer,
					operationCancellation.Token);
				if (read == 0)
					throw new EndOfStreamException();

				response.WriteByte(buffer[0]);
				if (buffer[0] == (byte)'$')
					return response.ToArray();
			}

			throw new InvalidDataException(
				"The Queclink response exceeded the simulator limit.");
		}

		public async ValueTask DisposeAsync()
		{
			if (_disposed)
				return;

			_disposed = true;
			await _stream.DisposeAsync();
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
					nameof(QueclinkTcpSimulator));
		}
	}
}
