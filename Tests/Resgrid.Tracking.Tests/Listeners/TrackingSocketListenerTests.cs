using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.TrackerGateway.Health;
using Resgrid.TrackerGateway.Hosting;
using Resgrid.TrackerGateway.Listeners;
using Resgrid.TrackerGateway.Sessions;

namespace Resgrid.Tracking.Tests.Listeners
{
	[TestFixture]
	public class TrackingSocketListenerTests
	{
		[Test]
		public async Task TcpListener_LoopbackFrame_DelegatesAndReturnsResponse()
		{
			// Arrange
			var port = GetAvailableTcpPort();
			var received = new TaskCompletionSource<byte[]>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var handler = new TestTransportSessionHandler
			{
				TcpHandler = async (
					definition,
					stream,
					remoteEndPoint,
					cancellationToken) =>
				{
					var request = new byte[4];
					await stream.ReadExactlyAsync(
						request,
						cancellationToken);
					received.TrySetResult(request);
					await stream.WriteAsync(
						Encoding.ASCII.GetBytes("pong"),
						cancellationToken);
				}
			};
			var settings = CreateSettings();
			var listener = CreateFactory(settings, handler).Create(
				new TrackingListenerDefinition(
					"synthetic-v1",
					TrackingSocketTransport.Tcp,
					port));

			// Act
			await listener.StartAsync(CancellationToken.None);
			using var client = new TcpClient();
			await client.ConnectAsync(
				IPAddress.Loopback,
				port);
			await client.GetStream().WriteAsync(
				Encoding.ASCII.GetBytes("ping"));
			var response = new byte[4];
			await client.GetStream().ReadExactlyAsync(response);

			// Assert
			listener.IsBound.Should().BeTrue();
			(await received.Task.WaitAsync(TimeSpan.FromSeconds(2)))
				.Should()
				.Equal(Encoding.ASCII.GetBytes("ping"));
			response.Should().Equal(Encoding.ASCII.GetBytes("pong"));

			await listener.StopAsync(CancellationToken.None);
			listener.IsBound.Should().BeFalse();
		}

		[Test]
		public async Task TcpListener_StopRequested_DrainsActiveSessionBeforeClosing()
		{
			// Arrange
			var port = GetAvailableTcpPort();
			var sessionStarted = new TaskCompletionSource(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseSession = new TaskCompletionSource(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var handler = new TestTransportSessionHandler
			{
				TcpHandler = async (
					definition,
					stream,
					remoteEndPoint,
					cancellationToken) =>
				{
					var request = new byte[1];
					await stream.ReadExactlyAsync(
						request,
						cancellationToken);
					sessionStarted.TrySetResult();
					await releaseSession.Task.WaitAsync(cancellationToken);
				}
			};
			var settings = CreateSettings();
			var admission = new TrackingConnectionAdmission(settings);
			var listener = CreateFactory(
				settings,
				handler,
				admission).Create(
				new TrackingListenerDefinition(
					"synthetic-v1",
					TrackingSocketTransport.Tcp,
					port));
			await listener.StartAsync(CancellationToken.None);
			using var client = new TcpClient();
			await client.ConnectAsync(
				IPAddress.Loopback,
				port);
			await client.GetStream().WriteAsync(new byte[] { 1 });
			await sessionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

			// Act
			using var shutdownCancellation =
				new CancellationTokenSource(TimeSpan.FromSeconds(2));
			var stopTask = listener.StopAsync(
				shutdownCancellation.Token);

			// Assert
			stopTask.IsCompleted.Should().BeFalse();
			admission.CurrentConnections.Should().Be(1);
			listener.IsBound.Should().BeFalse();

			releaseSession.TrySetResult();
			await stopTask;
			admission.CurrentConnections.Should().Be(0);
			listener.IsBound.Should().BeFalse();
		}

		[Test]
		public async Task UdpListener_LoopbackDatagram_DelegatesAndReturnsResponse()
		{
			// Arrange
			var port = GetAvailableUdpPort();
			var received = new TaskCompletionSource<byte[]>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var handler = new TestTransportSessionHandler
			{
				UdpHandler = (
					definition,
					datagram,
					remoteEndPoint,
					cancellationToken) =>
				{
					received.TrySetResult(datagram.ToArray());
					return Task.FromResult<ReadOnlyMemory<byte>>(
						Encoding.ASCII.GetBytes("pong"));
				}
			};
			var settings = CreateSettings();
			var listener = CreateFactory(settings, handler).Create(
				new TrackingListenerDefinition(
					"synthetic-v1",
					TrackingSocketTransport.Udp,
					port));

			// Act
			await listener.StartAsync(CancellationToken.None);
			using var client = new UdpClient(AddressFamily.InterNetwork);
			client.Connect(IPAddress.Loopback, port);
			await client.SendAsync(
				Encoding.ASCII.GetBytes("ping"),
				4);
			using var responseCancellation =
				new CancellationTokenSource(TimeSpan.FromSeconds(2));
			var response = await client.ReceiveAsync(
				responseCancellation.Token);

			// Assert
			listener.IsBound.Should().BeTrue();
			(await received.Task.WaitAsync(TimeSpan.FromSeconds(2)))
				.Should()
				.Equal(Encoding.ASCII.GetBytes("ping"));
			response.Buffer.Should().Equal(
				Encoding.ASCII.GetBytes("pong"));

			await listener.StopAsync(CancellationToken.None);
			listener.IsBound.Should().BeFalse();
		}

		[Test]
		public async Task UdpListener_DatagramExceedsFrameLimit_DropsWithoutDelegating()
		{
			// Arrange
			var port = GetAvailableUdpPort();
			var received = new TaskCompletionSource(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var handler = new TestTransportSessionHandler
			{
				UdpHandler = (
					definition,
					datagram,
					remoteEndPoint,
					cancellationToken) =>
				{
					received.TrySetResult();
					return Task.FromResult(ReadOnlyMemory<byte>.Empty);
				}
			};
			var settings = CreateSettings(maxFrameBytes: 4);
			var listener = CreateFactory(settings, handler).Create(
				new TrackingListenerDefinition(
					"synthetic-v1",
					TrackingSocketTransport.Udp,
					port));
			await listener.StartAsync(CancellationToken.None);
			using var client = new UdpClient(AddressFamily.InterNetwork);
			client.Connect(IPAddress.Loopback, port);

			// Act
			await client.SendAsync(new byte[5], 5);
			var completed = await Task.WhenAny(
				received.Task,
				Task.Delay(TimeSpan.FromMilliseconds(200)));

			// Assert
			completed.Should().NotBeSameAs(received.Task);

			await listener.StopAsync(CancellationToken.None);
		}

		private static TrackingSocketListenerFactory CreateFactory(
			TrackingGatewaySettings settings,
			ITrackingTransportSessionHandler handler,
			TrackingConnectionAdmission admission = null)
		{
			return new TrackingSocketListenerFactory(
				settings,
				admission ?? new TrackingConnectionAdmission(settings),
				handler,
				new TrackingGatewayMetrics(),
				NullLoggerFactory.Instance);
		}

		private static TrackingGatewaySettings CreateSettings(
			int maxFrameBytes = 65536)
		{
			return new TrackingGatewaySettings(
				trackingEnabled: true,
				nativeGatewayEnabled: true,
				credentialPepper: "test-pepper",
				tcpIdleTimeoutSeconds: 300,
				maxFrameBytes,
				maxConnections: 10,
				maxConnectionsPerIp: 5,
				gracefulShutdownSeconds: 5,
				internalHealthPort: 8080,
				protocols: Array.Empty<TrackingProtocolListenerSettings>());
		}

		private static int GetAvailableTcpPort()
		{
			var listener = new TcpListener(
				IPAddress.Loopback,
				0);
			listener.Start();
			try
			{
				return ((IPEndPoint)listener.LocalEndpoint).Port;
			}
			finally
			{
				listener.Stop();
			}
		}

		private static int GetAvailableUdpPort()
		{
			using var client = new UdpClient(
				new IPEndPoint(IPAddress.Loopback, 0));
			return ((IPEndPoint)client.Client.LocalEndPoint).Port;
		}

		private sealed class TestTransportSessionHandler :
			ITrackingTransportSessionHandler
		{
			public Func<
				TrackingListenerDefinition,
				Stream,
				EndPoint,
				CancellationToken,
				Task> TcpHandler { get; set; } =
				(definition, stream, remoteEndPoint, cancellationToken) =>
					Task.CompletedTask;

			public Func<
				TrackingListenerDefinition,
				ReadOnlyMemory<byte>,
				EndPoint,
				CancellationToken,
				Task<ReadOnlyMemory<byte>>> UdpHandler { get; set; } =
				(definition, datagram, remoteEndPoint, cancellationToken) =>
					Task.FromResult(ReadOnlyMemory<byte>.Empty);

			public Task HandleTcpAsync(
				TrackingListenerDefinition definition,
				Stream stream,
				EndPoint remoteEndPoint,
				CancellationToken cancellationToken)
			{
				return TcpHandler(
					definition,
					stream,
					remoteEndPoint,
					cancellationToken);
			}

			public Task<ReadOnlyMemory<byte>> HandleUdpAsync(
				TrackingListenerDefinition definition,
				ReadOnlyMemory<byte> datagram,
				EndPoint remoteEndPoint,
				CancellationToken cancellationToken)
			{
				return UdpHandler(
					definition,
					datagram,
					remoteEndPoint,
					cancellationToken);
			}
		}
	}
}
