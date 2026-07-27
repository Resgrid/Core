using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.Providers.Tracking.Protocols.Gt06;
using Resgrid.Providers.Tracking.Protocols.Queclink;
using Resgrid.Providers.Tracking.Protocols.Teltonika;
using Resgrid.TrackerGateway.Health;
using Resgrid.TrackerGateway.Hosting;
using Resgrid.TrackerGateway.Listeners;
using Resgrid.TrackerGateway.Sessions;
using Resgrid.Tracking.Tests.Tools.Gt06;
using Resgrid.Tracking.Tests.Tools.Queclink;
using Resgrid.Tracking.Tests.Tools.Teltonika;

namespace Resgrid.Tracking.Tests.Sessions
{
	[TestFixture]
	public class TrackingTransportSessionHandlerTests
	{
		private const string ProtocolKey = "synthetic-v1";
		private const string TeltonikaImei =
			"356307042441013";
		private const string QueclinkImei =
			"868487004353181";
		private const string Gt06Imei =
			"864717003283581";

		[Test]
		public async Task TcpSession_FragmentedFrames_WaitsForIngressBeforeAcknowledgement()
		{
			// Arrange
			var ingressStarted = new TaskCompletionSource(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseIngress =
				new TaskCompletionSource<TrackingIngressResult>(
					TaskCreationOptions.RunContinuationsAsynchronously);
			using var host = CreateHost();
			host.Ingress.OnAccept = async (
				source,
				positions,
				cancellationToken) =>
			{
				ingressStarted.TrySetResult();
				return await releaseIngress.Task.WaitAsync(
					cancellationToken);
			};
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var client = new TcpClient();
				await client.ConnectAsync(
					IPAddress.Loopback,
					port);
				using var reader = CreateReader(
					client.GetStream());

				// Act
				await client.GetStream().WriteAsync(
					Encoding.ASCII.GetBytes("L|dev"));
				await Task.Delay(50);

				// Assert
				host.Authentication.ProtocolLookupCount
					.Should()
					.Be(0);

				await client.GetStream().WriteAsync(
					Encoding.ASCII.GetBytes("ice-1\n"));
				(await ReadLineAsync(reader))
					.Should()
					.Be("ACK|Login|0");

				await client.GetStream().WriteAsync(
					Encoding.ASCII.GetBytes(
						"H|device-1\n"));
				(await ReadLineAsync(reader))
					.Should()
					.Be("ACK|Heartbeat|0");
				host.Ingress.HeartbeatCount
					.Should()
					.Be(1);

				await client.GetStream().WriteAsync(
					Encoding.ASCII.GetBytes(
						"P|device-1|event-1\n"));
				await ingressStarted.Task.WaitAsync(
					TimeSpan.FromSeconds(2));
				var positionAck = reader
					.ReadLineAsync()
					.WaitAsync(TimeSpan.FromSeconds(2));
				await Task.Delay(50);
				positionAck.IsCompleted.Should().BeFalse();

				releaseIngress.TrySetResult(
					AcceptedIngress(1));
				(await positionAck)
					.Should()
					.Be("ACK|Positions|1");
				host.Ingress.LastSource
					.ReportedDeviceIdentifier
					.Should()
					.Be("device-1");
				host.Authentication.ProtocolLookupCount
					.Should()
					.Be(3);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TeltonikaTcpSession_LoginAndPosition_ReturnsProtocolAcknowledgements()
		{
			// Arrange
			using var host = CreateHost(
				maxFrameBytes: 1024,
				module: new TeltonikaCodec8ProtocolModule(),
				deviceIdentifier: TeltonikaImei,
				modelKey: "teltonika-fmc920");
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var client = new TcpClient();
				await client.ConnectAsync(
					IPAddress.Loopback,
					port);
				var stream = client.GetStream();
				var imei = Encoding.ASCII.GetBytes(
					TeltonikaImei);
				var login = new byte[imei.Length + 2];
				BinaryPrimitives.WriteUInt16BigEndian(
					login,
					(ushort)imei.Length);
				imei.CopyTo(login, 2);

				// Act
				await stream.WriteAsync(login);
				var loginResponse = await ReadBytesAsync(
					stream,
					1);
				await stream.WriteAsync(
					TeltonikaFixture(
						"codec8-io-location.hex"));
				var positionResponse =
					await ReadBytesAsync(
						stream,
						4);

				// Assert
				loginResponse.Should().Equal(
					new byte[] { 0x01 });
				BinaryPrimitives.ReadUInt32BigEndian(
						positionResponse)
					.Should()
					.Be(1);
				host.Authentication.ProtocolLookupCount
					.Should()
					.Be(2);
				host.Ingress.AcceptCount.Should().Be(1);
				host.Ingress.LastSource
					.ReportedDeviceIdentifier
					.Should()
					.Be(TeltonikaImei);
				var position = host.Ingress.LastPositions
					.Should()
					.ContainSingle()
					.Which;
				position.EventId.Should()
					.StartWith("teltonika:");
				position.Hdop.Should().Be(1.2m);
				position.ExternalPowerVolts.Should()
					.Be(12.5m);
				position.Ignition.Should().BeTrue();
				position.IsMoving.Should().BeTrue();
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TeltonikaTcpSimulator_FragmentedBufferedBatchAndDuplicate_ReturnsStableAcknowledgements()
		{
			// Arrange
			using var host = CreateHost(
				maxFrameBytes: 2048,
				module: new TeltonikaCodec8ProtocolModule(),
				deviceIdentifier: TeltonikaImei,
				modelKey: "teltonika-fmc920");
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				await using var simulator =
					await TeltonikaTcpSimulator
						.ConnectAndLoginAsync(
							IPAddress.Loopback,
							port,
							TeltonikaImei);
				var currentTimestamp =
					new DateTime(
						2026,
						7,
						26,
						16,
						30,
						0,
						DateTimeKind.Utc);
				var bufferedTimestamp =
					currentTimestamp.AddHours(-4);
				var batch =
					TeltonikaFixtureBuilder
						.BuildTimestampedBatch(
							TeltonikaFixture(
								"codec8-io-location.hex"),
							currentTimestamp,
							bufferedTimestamp);

				// Act
				var firstAcknowledgement =
					await simulator.SendFrameAsync(
						batch,
						fragmentSize: 3);
				var firstPositions =
					host.Ingress.LastPositions
						.ToArray();
				var duplicateAcknowledgement =
					await simulator.SendFrameAsync(batch);
				var duplicatePositions =
					host.Ingress.LastPositions
						.ToArray();

				// Assert
				firstAcknowledgement.Should().Be(2);
				duplicateAcknowledgement.Should().Be(2);
				firstPositions.Select(
						position => position.TimestampUtc)
					.Should()
					.Equal(
						currentTimestamp,
						bufferedTimestamp);
				duplicatePositions.Select(
						position => position.EventId)
					.Should()
					.Equal(
						firstPositions.Select(
							position => position.EventId));
				host.Ingress.AcceptCount.Should().Be(2);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TeltonikaTcpSimulator_Acknowledgement_WaitsForIngressConfirmation()
		{
			// Arrange
			var ingressStarted = new TaskCompletionSource(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseIngress =
				new TaskCompletionSource<TrackingIngressResult>(
					TaskCreationOptions.RunContinuationsAsynchronously);
			using var host = CreateHost(
				maxFrameBytes: 1024,
				module: new TeltonikaCodec8ProtocolModule(),
				deviceIdentifier: TeltonikaImei,
				modelKey: "teltonika-fmc920");
			host.Ingress.OnAccept = async (
				source,
				positions,
				cancellationToken) =>
			{
				ingressStarted.TrySetResult();
				return await releaseIngress.Task.WaitAsync(
					cancellationToken);
			};
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				await using var simulator =
					await TeltonikaTcpSimulator
						.ConnectAndLoginAsync(
							IPAddress.Loopback,
							port,
							TeltonikaImei);

				// Act
				var acknowledgement =
					simulator.SendFrameAsync(
						TeltonikaFixture(
							"codec8-io-location.hex"));
				await ingressStarted.Task.WaitAsync(
					TimeSpan.FromSeconds(2));

				// Assert
				acknowledgement.IsCompleted.Should()
					.BeFalse();
				releaseIngress.TrySetResult(
					AcceptedIngress(1));
				(await acknowledgement).Should().Be(1);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TeltonikaTcpSimulator_CorruptCrc_ClosesWithoutAcknowledgement()
		{
			// Arrange
			using var host = CreateHost(
				maxFrameBytes: 1024,
				module: new TeltonikaCodec8ProtocolModule(),
				deviceIdentifier: TeltonikaImei,
				modelKey: "teltonika-fmc920");
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				await using var simulator =
					await TeltonikaTcpSimulator
						.ConnectAndLoginAsync(
							IPAddress.Loopback,
							port,
							TeltonikaImei);
				var corrupted =
					TeltonikaFixtureBuilder.CorruptCrc(
						TeltonikaFixture(
							"codec8-io-location.hex"));

				// Act
				Func<Task> act = async () =>
					await simulator.SendFrameAsync(
						corrupted);

				// Assert
				await act.Should()
					.ThrowAsync<EndOfStreamException>();
				host.Ingress.AcceptCount.Should().Be(0);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TeltonikaTcpSimulator_DisconnectMidFrame_DoesNotReachIngress()
		{
			// Arrange
			using var host = CreateHost(
				maxFrameBytes: 1024,
				module: new TeltonikaCodec8ProtocolModule(),
				deviceIdentifier: TeltonikaImei,
				modelKey: "teltonika-fmc920");
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				await using var simulator =
					await TeltonikaTcpSimulator
						.ConnectAndLoginAsync(
							IPAddress.Loopback,
							port,
							TeltonikaImei);
				var frame = TeltonikaFixture(
					"codec8-io-location.hex");

				// Act
				await simulator.DisconnectMidFrameAsync(
					frame,
					frame.Length / 2);
				await WaitUntilAsync(
					() => host.GenerationRegistry
						.ActiveCount == 0);

				// Assert
				host.Ingress.AcceptCount.Should().Be(0);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task QueclinkTcpSimulator_FragmentedDuplicateAndHeartbeat_ReachesIngressAndAcknowledgesHeartbeat()
		{
			// Arrange
			using var host = CreateHost(
				maxFrameBytes: 2048,
				module: new QueclinkProtocolModule(),
				deviceIdentifier: QueclinkImei,
				modelKey: "queclink-gv57mg");
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				await using var simulator =
					await QueclinkTcpSimulator
						.ConnectAsync(
							IPAddress.Loopback,
							port);
				var frame = QueclinkFixture(
					"gtfri-live.txt");

				// Act
				await simulator.SendFrameAsync(
					frame,
					fragmentSize: 7);
				await WaitUntilAsync(
					() => host.Ingress.AcceptCount == 1);
				var firstEventId =
					host.Ingress.LastPositions
						.Single()
						.EventId;
				await simulator.SendFrameAsync(frame);
				await WaitUntilAsync(
					() => host.Ingress.AcceptCount == 2);
				var duplicateEventId =
					host.Ingress.LastPositions
						.Single()
						.EventId;
				var heartbeat = Encoding.ASCII.GetBytes(
					Encoding.ASCII.GetString(
							QueclinkFixture(
								"heartbeat.txt"))
						.Replace(
							"135790246811220",
							QueclinkImei,
							StringComparison.Ordinal));
				await simulator.SendFrameAsync(heartbeat);
				var heartbeatResponse =
					await simulator.ReadResponseAsync();

				// Assert
				duplicateEventId.Should().Be(firstEventId);
				Encoding.ASCII.GetString(
						heartbeatResponse)
					.Should()
					.Be("+SACK:GTHBD,1A0401,11F0$");
				host.Ingress.HeartbeatCount.Should().Be(1);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task Gt06TcpSimulator_LoginAndLocation_AcknowledgementWaitsForIngress()
		{
			// Arrange
			var ingressStarted = new TaskCompletionSource(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseIngress =
				new TaskCompletionSource<TrackingIngressResult>(
					TaskCreationOptions.RunContinuationsAsynchronously);
			using var host = CreateHost(
				maxFrameBytes: 1024,
				module: new Gt06ProtocolModule(),
				deviceIdentifier: Gt06Imei,
				modelKey: "jimi-jm-vl03");
			host.Ingress.OnAccept = async (
				source,
				positions,
				cancellationToken) =>
			{
				ingressStarted.TrySetResult();
				return await releaseIngress.Task.WaitAsync(
					cancellationToken);
			};
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				await using var simulator =
					await Gt06TcpSimulator
						.ConnectAsync(
							IPAddress.Loopback,
							port);
				var loginResponse =
					await simulator.SendFrameAsync(
						Gt06Fixture("login.hex"),
						fragmentSize: 3);

				// Act
				var locationResponse =
					simulator.SendFrameAsync(
						Gt06Fixture(
							"location-jm-vl03-a0.hex"),
						fragmentSize: 5);
				await ingressStarted.Task.WaitAsync(
					TimeSpan.FromSeconds(2));

				// Assert
				loginResponse.Should().Equal(
					Convert.FromHexString(
						"78780501000955940D0A"));
				locationResponse.IsCompleted.Should()
					.BeFalse();
				releaseIngress.TrySetResult(
					AcceptedIngress(1));
				(await locationResponse).Should().Equal(
					Convert.FromHexString(
						"787805A00146A3B40D0A"));
				host.Ingress.LastPositions.Single()
					.Ignition.Should()
					.BeTrue();
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TeltonikaUdpSimulator_BufferedBatchAndDuplicate_ReturnsMatchingAcknowledgements()
		{
			// Arrange
			using var host = CreateHost(
				maxFrameBytes: 2048,
				module: new TeltonikaCodec8ProtocolModule(),
				deviceIdentifier: TeltonikaImei,
				modelKey: "teltonika-fmc920");
			var port = GetAvailableUdpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Udp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var simulator =
					new TeltonikaUdpSimulator(
						IPAddress.Loopback,
						port);
				var currentTimestamp =
					new DateTime(
						2026,
						7,
						26,
						17,
						0,
						0,
						DateTimeKind.Utc);
				var bufferedTimestamp =
					currentTimestamp.AddHours(-6);
				var batch =
					TeltonikaFixtureBuilder
						.BuildTimestampedBatch(
							TeltonikaFixture(
								"codec8-io-location.hex"),
							currentTimestamp,
							bufferedTimestamp);
				var datagram =
					TeltonikaFixtureBuilder.WrapUdp(
						batch,
						TeltonikaImei,
						0xCAFE,
						0x05);

				// Act
				var firstAcknowledgement =
					await simulator.SendDatagramAsync(
						datagram);
				var firstPositions =
					host.Ingress.LastPositions
						.ToArray();
				var duplicateAcknowledgement =
					await simulator.SendDatagramAsync(
						datagram);
				var duplicatePositions =
					host.Ingress.LastPositions
						.ToArray();

				// Assert
				firstAcknowledgement.ChannelPacketId
					.Should()
					.Be(0xCAFE);
				firstAcknowledgement.AvlPacketId
					.Should()
					.Be(0x05);
				firstAcknowledgement.AcceptedRecords
					.Should()
					.Be(2);
				duplicateAcknowledgement.AcceptedRecords
					.Should()
					.Be(2);
				firstPositions.Select(
						position => position.TimestampUtc)
					.Should()
					.Equal(
						currentTimestamp,
						bufferedTimestamp);
				duplicatePositions.Select(
						position => position.EventId)
					.Should()
					.Equal(
						firstPositions.Select(
							position => position.EventId));
				host.Ingress.AcceptCount.Should().Be(2);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TeltonikaUdpSimulator_Acknowledgement_WaitsForIngressConfirmation()
		{
			// Arrange
			var ingressStarted = new TaskCompletionSource(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseIngress =
				new TaskCompletionSource<TrackingIngressResult>(
					TaskCreationOptions.RunContinuationsAsynchronously);
			using var host = CreateHost(
				maxFrameBytes: 1024,
				module: new TeltonikaCodec8ProtocolModule(),
				deviceIdentifier: TeltonikaImei,
				modelKey: "teltonika-fmc920");
			host.Ingress.OnAccept = async (
				source,
				positions,
				cancellationToken) =>
			{
				ingressStarted.TrySetResult();
				return await releaseIngress.Task.WaitAsync(
					cancellationToken);
			};
			var port = GetAvailableUdpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Udp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var simulator =
					new TeltonikaUdpSimulator(
						IPAddress.Loopback,
						port);
				var datagram =
					TeltonikaFixtureBuilder.WrapUdp(
						TeltonikaFixture(
							"codec8-io-location.hex"),
						TeltonikaImei,
						0xBEEF,
						0x07);

				// Act
				var acknowledgement =
					simulator.SendDatagramAsync(
						datagram);
				await ingressStarted.Task.WaitAsync(
					TimeSpan.FromSeconds(2));

				// Assert
				acknowledgement.IsCompleted.Should()
					.BeFalse();
				releaseIngress.TrySetResult(
					AcceptedIngress(1));
				(await acknowledgement)
					.AcceptedRecords.Should()
					.Be(1);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TeltonikaUdpSimulator_InvalidRecordCount_ReturnsNoAcknowledgement()
		{
			// Arrange
			using var host = CreateHost(
				maxFrameBytes: 1024,
				module: new TeltonikaCodec8ProtocolModule(),
				deviceIdentifier: TeltonikaImei,
				modelKey: "teltonika-fmc920");
			var port = GetAvailableUdpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Udp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var simulator =
					new TeltonikaUdpSimulator(
						IPAddress.Loopback,
						port);
				var datagram =
					TeltonikaFixtureBuilder.WrapUdp(
						TeltonikaFixture(
							"codec8-io-location.hex"),
						TeltonikaImei,
						0xCAFE,
						0x05);
				datagram[^1] = 2;
				using var responseCancellation =
					new CancellationTokenSource(
						TimeSpan.FromMilliseconds(250));

				// Act
				await simulator.WriteDatagramAsync(
					datagram);
				Func<Task> act = async () =>
					await simulator
						.ReadAcknowledgementAsync(
							responseCancellation.Token);

				// Assert
				await act.Should()
					.ThrowAsync<OperationCanceledException>();
				host.Ingress.AcceptCount.Should().Be(0);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TeltonikaUdpSession_Position_ReturnsChannelAcknowledgement()
		{
			// Arrange
			using var host = CreateHost(
				maxFrameBytes: 1024,
				module: new TeltonikaCodec8ProtocolModule(),
				deviceIdentifier: TeltonikaImei);
			var port = GetAvailableUdpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Udp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var client = new UdpClient(
					AddressFamily.InterNetwork);
				client.Connect(
					IPAddress.Loopback,
					port);

				// Act
				await client.SendAsync(
					TeltonikaFixture(
						"codec8-udp-location.hex"));
				using var responseCancellation =
					new CancellationTokenSource(
						TimeSpan.FromSeconds(2));
				var response = await client.ReceiveAsync(
					responseCancellation.Token);

				// Assert
				response.Buffer.Should().Equal(
					Convert.FromHexString(
						"0005CAFE010501"));
				host.Authentication.ProtocolLookupCount
					.Should()
					.Be(1);
				host.Ingress.AcceptCount.Should().Be(1);
				host.Ingress.LastSource
					.ReportedDeviceIdentifier
					.Should()
					.Be(TeltonikaImei);
				host.Ingress.LastPositions.Should()
					.ContainSingle()
					.Which.EventId.Should()
					.StartWith("teltonika:");
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TcpSession_IngressUnavailable_DoesNotSendSuccessAndCloses()
		{
			// Arrange
			using var host = CreateHost();
			host.Ingress.OnAccept = (
				source,
				positions,
				cancellationToken) =>
				Task.FromResult(
					new TrackingIngressResult
					{
						Status =
							TrackingIngressStatus.Unavailable
					});
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var client = new TcpClient();
				await client.ConnectAsync(
					IPAddress.Loopback,
					port);
				using var reader = CreateReader(
					client.GetStream());
				await LoginAsync(client, reader);

				// Act
				await client.GetStream().WriteAsync(
					Encoding.ASCII.GetBytes(
						"P|device-1|event-1\n"));

				// Assert
				(await ReadLineAsync(reader))
					.Should()
					.Be(
						"NACK|Unavailable|ingress-unavailable");
				(await WaitForCloseAsync(reader))
					.Should()
					.BeTrue();
				host.Module.Acceptances
					.Should()
					.Contain(acceptance =>
						acceptance.Status ==
						TrackingAcceptanceStatus.Unavailable);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TcpSession_SourceOutsideAllowedCidrs_RejectsBeforeIngress()
		{
			// Arrange
			using var host = CreateHost(
				allowedSourceCidrs: "10.0.0.0/8");
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var client = new TcpClient();
				await client.ConnectAsync(
					IPAddress.Loopback,
					port);
				using var reader = CreateReader(
					client.GetStream());

				// Act
				await client.GetStream().WriteAsync(
					Encoding.ASCII.GetBytes(
						"L|device-1\n"));

				// Assert
				(await ReadLineAsync(reader))
					.Should()
					.Be(
						"NACK|Rejected|source-not-allowed");
				host.Ingress.AcceptCount.Should().Be(0);
				(await WaitForCloseAsync(reader))
					.Should()
					.BeTrue();
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TcpSession_FrameReachesLimitWithoutBoundary_ClosesWithoutAuthentication()
		{
			// Arrange
			using var host = CreateHost(maxFrameBytes: 16);
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var client = new TcpClient();
				await client.ConnectAsync(
					IPAddress.Loopback,
					port);
				using var reader = CreateReader(
					client.GetStream());

				// Act
				await client.GetStream().WriteAsync(
					new byte[16]);

				// Assert
				(await WaitForCloseAsync(reader))
					.Should()
					.BeTrue();
				host.Authentication.ProtocolLookupCount
					.Should()
					.Be(0);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TcpSession_NoDataBeforeIdleTimeout_ClosesSession()
		{
			// Arrange
			using var host = CreateHost(
				tcpIdleTimeoutSeconds: 1);
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var client = new TcpClient();
				await client.ConnectAsync(
					IPAddress.Loopback,
					port);
				using var reader = CreateReader(
					client.GetStream());

				// Act
				var closed = await WaitForCloseAsync(
					reader,
					TimeSpan.FromSeconds(3));

				// Assert
				closed.Should().BeTrue();
				host.GenerationRegistry.ActiveCount
					.Should()
					.Be(0);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task TcpSession_NewerAuthenticatedReconnect_ClosesStaleSession()
		{
			// Arrange
			using var host = CreateHost();
			var port = GetAvailableTcpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Tcp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var firstClient = new TcpClient();
				await firstClient.ConnectAsync(
					IPAddress.Loopback,
					port);
				using var firstReader = CreateReader(
					firstClient.GetStream());
				await LoginAsync(
					firstClient,
					firstReader);
				host.GenerationRegistry.ActiveCount
					.Should()
					.Be(1);

				// Act
				using var secondClient = new TcpClient();
				await secondClient.ConnectAsync(
					IPAddress.Loopback,
					port);
				using var secondReader = CreateReader(
					secondClient.GetStream());
				await LoginAsync(
					secondClient,
					secondReader);

				// Assert
				(await WaitForCloseAsync(firstReader))
					.Should()
					.BeTrue();
				host.GenerationRegistry.ActiveCount
					.Should()
					.Be(1);

				secondClient.Dispose();
				await WaitUntilAsync(
					() => host.GenerationRegistry.ActiveCount == 0);
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		[Test]
		public async Task UdpSession_Position_WaitsForIngressBeforeAcknowledgement()
		{
			// Arrange
			var ingressStarted = new TaskCompletionSource(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseIngress =
				new TaskCompletionSource<TrackingIngressResult>(
					TaskCreationOptions.RunContinuationsAsynchronously);
			using var host = CreateHost();
			host.Ingress.OnAccept = async (
				source,
				positions,
				cancellationToken) =>
			{
				ingressStarted.TrySetResult();
				return await releaseIngress.Task.WaitAsync(
					cancellationToken);
			};
			var port = GetAvailableUdpPort();
			var listener = host.CreateListener(
				TrackingSocketTransport.Udp,
				port);
			await listener.StartAsync(CancellationToken.None);

			try
			{
				using var client = new UdpClient(
					AddressFamily.InterNetwork);
				client.Connect(
					IPAddress.Loopback,
					port);
				await client.SendAsync(
					Encoding.ASCII.GetBytes(
						"L|device-1\n"));
				using (var loginCancellation =
				       new CancellationTokenSource(
					       TimeSpan.FromSeconds(2)))
				{
					var loginResponse = await client.ReceiveAsync(
						loginCancellation.Token);
					Encoding.ASCII
						.GetString(loginResponse.Buffer)
						.Should()
						.Be("ACK|Login|0\n");
				}

				await client.SendAsync(
					Encoding.ASCII.GetBytes(
						"P|device-1|event-1\n"));
				using var responseCancellation =
					new CancellationTokenSource(
						TimeSpan.FromSeconds(2));
				var responseTask = client
					.ReceiveAsync(
						responseCancellation.Token)
					.AsTask();

				// Act
				await ingressStarted.Task.WaitAsync(
					TimeSpan.FromSeconds(2));
				await Task.Delay(50);

				// Assert
				responseTask.IsCompleted.Should().BeFalse();

				releaseIngress.TrySetResult(
					AcceptedIngress(1));
				var response = await responseTask;
				Encoding.ASCII
					.GetString(response.Buffer)
					.Should()
					.Be("ACK|Positions|1\n");
				host.GenerationRegistry.ActiveCount
					.Should()
					.Be(0);
				var metrics =
					TrackingGatewayMetricsWriter.Write(
						new TrackingGatewayReadinessSnapshot(
							expectedListeners: 1,
							boundListeners: 1,
							isReady: true,
							hasFailure: false),
						host.Metrics);
				metrics.Should().Contain(
					"resgrid_tracking_ingress_messages_total{transport=\"udp\",protocol=\"synthetic-v1\",outcome=\"accepted\"} 2");
				metrics.Should().Contain(
					"resgrid_tracking_positions_total{transport=\"udp\",protocol=\"synthetic-v1\",outcome=\"accepted\"} 1");
				metrics.Should().Contain(
					"resgrid_tracking_queue_publish_duration_seconds_count{transport=\"udp\"} 1");
				metrics.Should().Contain(
					"resgrid_tracking_frame_bytes_count{protocol=\"synthetic-v1\"} 2");
			}
			finally
			{
				await StopAsync(listener);
			}
		}

		private static TestHost CreateHost(
			int tcpIdleTimeoutSeconds = 5,
			int maxFrameBytes = 256,
			string allowedSourceCidrs = null,
			ITrackingProtocolModule module = null,
			string deviceIdentifier = "device-1",
			string modelKey = null)
		{
			return new TestHost(
				tcpIdleTimeoutSeconds,
				maxFrameBytes,
				allowedSourceCidrs,
				module,
				deviceIdentifier,
				modelKey);
		}

		private static async Task LoginAsync(
			TcpClient client,
			StreamReader reader)
		{
			await client.GetStream().WriteAsync(
				Encoding.ASCII.GetBytes(
					"L|device-1\n"));
			(await ReadLineAsync(reader))
				.Should()
				.Be("ACK|Login|0");
		}

		private static async Task<string> ReadLineAsync(
			StreamReader reader)
		{
			return await reader
				.ReadLineAsync()
				.WaitAsync(TimeSpan.FromSeconds(2));
		}

		private static async Task<byte[]> ReadBytesAsync(
			Stream stream,
			int count)
		{
			var buffer = new byte[count];
			var offset = 0;
			using var cancellation =
				new CancellationTokenSource(
					TimeSpan.FromSeconds(2));
			while (offset < buffer.Length)
			{
				var read = await stream.ReadAsync(
					buffer.AsMemory(
						offset,
						buffer.Length - offset),
					cancellation.Token);
				if (read == 0)
					throw new EndOfStreamException();
				offset += read;
			}

			return buffer;
		}

		private static byte[] TeltonikaFixture(
			string fileName)
		{
			var path = Path.Combine(
				TestContext.CurrentContext.TestDirectory,
				"Data",
				"Teltonika",
				fileName);
			return Convert.FromHexString(
				System.IO.File.ReadAllText(path).Trim());
		}

		private static byte[] QueclinkFixture(
			string fileName)
		{
			var path = Path.Combine(
				TestContext.CurrentContext.TestDirectory,
				"Data",
				"Queclink",
				fileName);
			return Encoding.ASCII.GetBytes(
				System.IO.File.ReadAllText(path).Trim());
		}

		private static byte[] Gt06Fixture(
			string fileName)
		{
			var path = Path.Combine(
				TestContext.CurrentContext.TestDirectory,
				"Data",
				"Gt06",
				fileName);
			return Convert.FromHexString(
				System.IO.File.ReadAllText(path).Trim());
		}

		private static async Task<bool> WaitForCloseAsync(
			StreamReader reader,
			TimeSpan? timeout = null)
		{
			try
			{
				var line = await reader
					.ReadLineAsync()
					.WaitAsync(
						timeout ??
						TimeSpan.FromSeconds(2));
				return line == null;
			}
			catch (IOException)
			{
				return true;
			}
			catch (SocketException)
			{
				return true;
			}
		}

		private static async Task WaitUntilAsync(
			Func<bool> condition)
		{
			var timeout = DateTime.UtcNow.AddSeconds(2);
			while (!condition() &&
			       DateTime.UtcNow < timeout)
				await Task.Delay(20);

			condition().Should().BeTrue();
		}

		private static StreamReader CreateReader(
			Stream stream)
		{
			return new StreamReader(
				stream,
				Encoding.ASCII,
				detectEncodingFromByteOrderMarks: false,
				bufferSize: 256,
				leaveOpen: true);
		}

		private static TrackingIngressResult AcceptedIngress(
			int accepted)
		{
			return new TrackingIngressResult
			{
				Status = TrackingIngressStatus.Accepted,
				Accepted = accepted,
				ReceivedOn = DateTime.UtcNow
			};
		}

		private static async Task StopAsync(
			ITrackingListener listener)
		{
			using var cancellation =
				new CancellationTokenSource(
					TimeSpan.FromSeconds(2));
			await listener.StopAsync(
				cancellation.Token);
		}

		private static int GetAvailableTcpPort()
		{
			var listener = new TcpListener(
				IPAddress.Loopback,
				0);
			listener.Start();
			try
			{
				return ((IPEndPoint)
					listener.LocalEndpoint).Port;
			}
			finally
			{
				listener.Stop();
			}
		}

		private static int GetAvailableUdpPort()
		{
			using var client = new UdpClient(
				new IPEndPoint(
					IPAddress.Loopback,
					0));
			return ((IPEndPoint)
				client.Client.LocalEndPoint).Port;
		}

		private sealed class TestHost : IDisposable
		{
			private readonly IContainer _container;
			private readonly TrackingGatewaySettings _settings;
			private readonly TrackingSocketListenerFactory _factory;
			private readonly string _protocolKey;

			public TestHost(
				int tcpIdleTimeoutSeconds,
				int maxFrameBytes,
				string allowedSourceCidrs,
				ITrackingProtocolModule module,
				string deviceIdentifier,
				string modelKey)
			{
				var selectedModule =
					module ??
					new SyntheticProtocolModule();
				_protocolKey =
					selectedModule.ProtocolKey;
				var device = new UnitTrackingDevice
				{
					UnitTrackingDeviceId = "tracking-device-1",
					ModelKey = modelKey,
					ProtocolKey = _protocolKey,
					DeviceIdentifier = deviceIdentifier,
					IsEnabled = true,
					AllowedSourceCidrs = allowedSourceCidrs
				};
				Authentication =
					new TestAuthenticationService(device);
				Ingress = new TestIngressService();
				Module =
					selectedModule as
						SyntheticProtocolModule;
				GenerationRegistry =
					new TrackingSessionGenerationRegistry();
				Metrics = new TrackingGatewayMetrics();
				_settings = new TrackingGatewaySettings(
					trackingEnabled: true,
					nativeGatewayEnabled: true,
					credentialPepper: "test-pepper",
					tcpIdleTimeoutSeconds,
					maxFrameBytes,
					maxConnections: 20,
					maxConnectionsPerIp: 10,
					gracefulShutdownSeconds: 2,
					internalHealthPort: 8080,
					protocols:
					Array.Empty<
						TrackingProtocolListenerSettings>());

				var builder = new ContainerBuilder();
				builder.RegisterInstance(Authentication)
					.As<IUnitTrackingAuthenticationService>();
				builder.RegisterInstance(Ingress)
					.As<IUnitTrackingIngressService>();
				_container = builder.Build();

				var moduleRegistry =
					new TrackingProtocolModuleRegistry(
						new[] { selectedModule });
				var handler =
					new TrackingTransportSessionHandler(
						_settings,
						moduleRegistry,
						_container,
						GenerationRegistry,
						Metrics,
						NullLogger<
							TrackingTransportSessionHandler>
							.Instance);
				_factory = new TrackingSocketListenerFactory(
					_settings,
					new TrackingConnectionAdmission(
						_settings),
					handler,
					Metrics,
					NullLoggerFactory.Instance);
			}

			public TestAuthenticationService Authentication
				{ get; }
			public TestIngressService Ingress { get; }
			public SyntheticProtocolModule Module { get; }
			public TrackingSessionGenerationRegistry
				GenerationRegistry { get; }
			public TrackingGatewayMetrics Metrics { get; }

			public ITrackingListener CreateListener(
				TrackingSocketTransport transport,
				int port)
			{
				return _factory.Create(
					new TrackingListenerDefinition(
						_protocolKey,
						transport,
						port));
			}

			public void Dispose()
			{
				_container.Dispose();
			}
		}

		private sealed class TestAuthenticationService :
			IUnitTrackingAuthenticationService
		{
			private readonly UnitTrackingDevice _device;
			private int _protocolLookupCount;

			public TestAuthenticationService(
				UnitTrackingDevice device)
			{
				_device = device;
			}

			public int ProtocolLookupCount =>
				Volatile.Read(ref _protocolLookupCount);

			public UnitTrackingGeneratedCredential
				GenerateCredential()
			{
				throw new NotSupportedException();
			}

			public string ComputeSecretHash(string token)
			{
				throw new NotSupportedException();
			}

			public bool VerifySecret(
				string token,
				string storedHash)
			{
				throw new NotSupportedException();
			}

			public Task<UnitTrackingAuthenticationResult>
				AuthenticateAsync(
					string token,
					DateTime? utcNow = null,
					CancellationToken cancellationToken = default)
			{
				throw new NotSupportedException();
			}

			public Task<UnitTrackingDevice>
				GetEnabledDeviceByEndpointIdAsync(
					string deviceId,
					CancellationToken cancellationToken = default)
			{
				throw new NotSupportedException();
			}

			public Task<UnitTrackingDevice>
				GetEnabledDeviceByProtocolIdentifierAsync(
					string protocolKey,
					string deviceIdentifier,
					CancellationToken cancellationToken = default)
			{
				cancellationToken
					.ThrowIfCancellationRequested();
				Interlocked.Increment(
					ref _protocolLookupCount);
				var matches =
					string.Equals(
						protocolKey,
						_device.ProtocolKey,
						StringComparison.OrdinalIgnoreCase) &&
					string.Equals(
						deviceIdentifier,
						_device.DeviceIdentifier,
						StringComparison.Ordinal);
				return Task.FromResult(
					matches ? _device : null);
			}

			public Task<IReadOnlyCollection<
					UnitTrackingCredential>>
				GetActiveCredentialsForDeviceAsync(
					string deviceId,
					DateTime? utcNow = null,
					CancellationToken cancellationToken = default)
			{
				return Task.FromResult<
					IReadOnlyCollection<
						UnitTrackingCredential>>(
					Array.Empty<
						UnitTrackingCredential>());
			}

			public Task InvalidateCredentialAsync(
				string secretHash)
			{
				return Task.CompletedTask;
			}

			public Task InvalidateDeviceAsync(
				UnitTrackingDevice device)
			{
				return Task.CompletedTask;
			}
		}

		private sealed class TestIngressService :
			IUnitTrackingIngressService
		{
			private int _acceptCount;
			private int _heartbeatCount;

			public Func<
				AuthenticatedTrackingSource,
				IReadOnlyCollection<
					CanonicalTrackingPosition>,
				CancellationToken,
				Task<TrackingIngressResult>> OnAccept
				{ get; set; } =
				(source, positions, cancellationToken) =>
					Task.FromResult(
						AcceptedIngress(
							positions.Count));

			public int AcceptCount =>
				Volatile.Read(ref _acceptCount);
			public int HeartbeatCount =>
				Volatile.Read(ref _heartbeatCount);
			public AuthenticatedTrackingSource LastSource
				{ get; private set; }
			public IReadOnlyCollection<
					CanonicalTrackingPosition>
				LastPositions { get; private set; }

			public Task<TrackingIngressResult> AcceptAsync(
				AuthenticatedTrackingSource source,
				IReadOnlyCollection<
					CanonicalTrackingPosition> positions,
				CancellationToken cancellationToken = default)
			{
				Interlocked.Increment(
					ref _acceptCount);
				LastSource = source;
				LastPositions = positions;
				return OnAccept(
					source,
					positions,
					cancellationToken);
			}

			public Task<TrackingIngressResult>
				AcceptHeartbeatAsync(
					AuthenticatedTrackingSource source,
					DateTime receivedOnUtc,
					CancellationToken cancellationToken = default)
			{
				cancellationToken.ThrowIfCancellationRequested();
				Interlocked.Increment(
					ref _heartbeatCount);
				LastSource = source;
				return Task.FromResult(
					new TrackingIngressResult
					{
						Status =
							TrackingIngressStatus.Accepted,
						ReceivedOn = receivedOnUtc
					});
			}
		}

		private sealed class SyntheticProtocolModule :
			ITrackingProtocolModule
		{
			public string ProtocolKey =>
				TrackingTransportSessionHandlerTests
					.ProtocolKey;
			public IReadOnlySet<TrackingSocketTransport>
				SupportedTransports { get; } =
				new HashSet<TrackingSocketTransport>
				{
					TrackingSocketTransport.Tcp,
					TrackingSocketTransport.Udp
				};
			public ConcurrentQueue<TrackingAcceptance>
				Acceptances { get; } =
					new ConcurrentQueue<
						TrackingAcceptance>();

			public ITrackingProtocolSession CreateSession(
				TrackingSessionContext context)
			{
				return new SyntheticProtocolSession(
					Acceptances);
			}
		}

		private sealed class SyntheticProtocolSession :
			ITrackingProtocolSession
		{
			private readonly ConcurrentQueue<
				TrackingAcceptance> _acceptances;

			public SyntheticProtocolSession(
				ConcurrentQueue<
					TrackingAcceptance> acceptances)
			{
				_acceptances = acceptances;
			}

			public ProtocolParseResult Parse(
				ref ReadOnlySequence<byte> input)
			{
				var reader =
					new SequenceReader<byte>(input);
				if (!reader.TryReadTo(
					    out ReadOnlySequence<byte> line,
					    (byte)'\n',
					    advancePastDelimiter: true))
				{
					return new ProtocolParseResult
					{
						Status =
							ProtocolParseStatus
								.NeedMoreData,
						Consumed = input.Start,
						Examined = input.End
					};
				}

				var text = Encoding.ASCII.GetString(
					line.ToArray());
				var parts = text.Split('|');
				var result = new ProtocolParseResult
				{
					Consumed = reader.Position,
					Examined = reader.Position
				};

				if (parts.Length == 2 &&
				    parts[0] == "L")
				{
					result.Status =
						ProtocolParseStatus.Login;
					result.Message = Message(
						ProtocolMessageType.Login,
						parts[1],
						null);
					return result;
				}

				if (parts.Length == 2 &&
				    parts[0] == "H")
				{
					result.Status =
						ProtocolParseStatus.Heartbeat;
					result.Message = Message(
						ProtocolMessageType.Heartbeat,
						parts[1],
						null);
					return result;
				}

				if (parts.Length == 3 &&
				    parts[0] == "P")
				{
					result.Status =
						ProtocolParseStatus.Positions;
					result.Message = Message(
						ProtocolMessageType.Positions,
						parts[1],
						parts[2]);
					return result;
				}

				result.Status =
					ProtocolParseStatus.Malformed;
				return result;
			}

			public ReadOnlyMemory<byte> BuildResponse(
				ProtocolMessage message,
				TrackingAcceptance acceptance)
			{
				_acceptances.Enqueue(
					new TrackingAcceptance
					{
						Status = acceptance.Status,
						AcceptedPositions =
							acceptance.AcceptedPositions,
						ReasonCode =
							acceptance.ReasonCode
					});
				var response =
					acceptance.Status ==
					TrackingAcceptanceStatus.Accepted
						? $"ACK|{message.MessageType}|{acceptance.AcceptedPositions}\n"
						: $"NACK|{acceptance.Status}|{acceptance.ReasonCode}\n";
				return Encoding.ASCII.GetBytes(
					response);
			}

			private static ProtocolMessage Message(
				ProtocolMessageType messageType,
				string identifier,
				string eventId)
			{
				var positions =
					messageType ==
					ProtocolMessageType.Positions
						? new[]
						{
							new CanonicalTrackingPosition
							{
								EventId = eventId,
								TimestampUtc =
									DateTime.UtcNow,
								ReceivedOnUtc =
									DateTime.UtcNow,
								Latitude = 47.61m,
								Longitude = -122.33m,
								IsValidFix = true
							}
						}
						: Array.Empty<
							CanonicalTrackingPosition>();
				return new ProtocolMessage
				{
					MessageType = messageType,
					ExternalIdentifier = identifier,
					Positions = positions,
					AcknowledgementToken =
						eventId == null
							? ReadOnlyMemory<byte>.Empty
							: Encoding.ASCII.GetBytes(
								eventId),
					RequiresResponse = true
				};
			}
		}
	}
}
