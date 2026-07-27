using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.TrackerGateway.Health;
using Resgrid.TrackerGateway.Hosting;
using Resgrid.TrackerGateway.Sessions;

namespace Resgrid.TrackerGateway.Listeners
{
	public sealed class UdpTrackingListener : ITrackingListener
	{
		private readonly TrackingGatewaySettings _settings;
		private readonly TrackingConnectionAdmission _connectionAdmission;
		private readonly ITrackingTransportSessionHandler _sessionHandler;
		private readonly TrackingGatewayMetrics _metrics;
		private readonly ILogger<UdpTrackingListener> _logger;
		private readonly ConcurrentDictionary<long, ActiveDatagram>
			_activeDatagrams =
				new ConcurrentDictionary<long, ActiveDatagram>();
		private CancellationTokenSource _receiveCancellation;
		private CancellationTokenSource _sessionCancellation;
		private Socket _listenerSocket;
		private Task _completion = Task.CompletedTask;
		private long _datagramSequence;
		private int _isBound;
		private int _started;
		private int _stopping;

		public UdpTrackingListener(
			TrackingListenerDefinition definition,
			TrackingGatewaySettings settings,
			TrackingConnectionAdmission connectionAdmission,
			ITrackingTransportSessionHandler sessionHandler,
			TrackingGatewayMetrics metrics,
			ILogger<UdpTrackingListener> logger)
		{
			Definition = definition ??
				throw new ArgumentNullException(nameof(definition));
			_settings = settings ??
				throw new ArgumentNullException(nameof(settings));
			_connectionAdmission = connectionAdmission ??
				throw new ArgumentNullException(nameof(connectionAdmission));
			_sessionHandler = sessionHandler ??
				throw new ArgumentNullException(nameof(sessionHandler));
			_metrics = metrics ??
				throw new ArgumentNullException(nameof(metrics));
			_logger = logger ??
				throw new ArgumentNullException(nameof(logger));
		}

		public TrackingListenerDefinition Definition { get; }
		public bool IsBound => Volatile.Read(ref _isBound) != 0;
		public Task Completion => _completion;

		public Task StartAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (Interlocked.Exchange(ref _started, 1) != 0)
				throw new InvalidOperationException("The UDP listener has already started.");

			_receiveCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_sessionCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			var listenerSocket = new Socket(
				AddressFamily.InterNetworkV6,
				SocketType.Dgram,
				ProtocolType.Udp)
			{
				DualMode = true
			};

			try
			{
				listenerSocket.Bind(
					new IPEndPoint(IPAddress.IPv6Any, Definition.Port));
				_listenerSocket = listenerSocket;
				Volatile.Write(ref _isBound, 1);
				_completion = ReceiveLoopAsync(
					listenerSocket,
					_receiveCancellation.Token);
				return Task.CompletedTask;
			}
			catch
			{
				listenerSocket.Dispose();
				_receiveCancellation.Dispose();
				_sessionCancellation.Dispose();
				throw;
			}
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			if (Interlocked.Exchange(ref _stopping, 1) != 0)
				return;

			_receiveCancellation?.Cancel();

			try
			{
				await _completion;
			}
			catch (OperationCanceledException)
				when (_receiveCancellation?.IsCancellationRequested == true)
			{
			}

			var activeDatagrams = GetActiveDatagramSnapshot();
			var drainTask = Task.WhenAll(
				activeDatagrams.Select(datagram => datagram.Completion));
			try
			{
				await drainTask.WaitAsync(cancellationToken);
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				_logger.LogWarning(
					"UDP listener shutdown timed out for {ProtocolKey} on port {Port}; canceling {DatagramCount} in-flight datagrams.",
					Definition.ProtocolKey,
					Definition.Port,
					activeDatagrams.Count);
				_metrics.RecordForcedShutdown(Definition);
				_sessionCancellation?.Cancel();
			}
			finally
			{
				_sessionCancellation?.Cancel();
				_listenerSocket?.Dispose();
				Volatile.Write(ref _isBound, 0);
			}
		}

		private async Task ReceiveLoopAsync(
			Socket listenerSocket,
			CancellationToken cancellationToken)
		{
			var receiveBuffer = ArrayPool<byte>.Shared.Rent(
				_settings.MaxFrameBytes + 1);
			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					SocketReceiveFromResult result;
					try
					{
						result = await listenerSocket.ReceiveFromAsync(
							receiveBuffer.AsMemory(
								0,
								_settings.MaxFrameBytes + 1),
							SocketFlags.None,
							new IPEndPoint(IPAddress.IPv6Any, 0),
							cancellationToken);
					}
					catch (OperationCanceledException)
						when (cancellationToken.IsCancellationRequested)
					{
						return;
					}
					catch (SocketException ex)
						when (ex.SocketErrorCode == SocketError.MessageSize)
					{
						_logger.LogDebug(
							"UDP tracking datagram exceeded the configured frame limit for {ProtocolKey}.",
							Definition.ProtocolKey);
						_metrics.RecordParseFailure(
							Definition.ProtocolKey,
							"frame-too-large");
						continue;
					}

					if (result.ReceivedBytes <= 0)
						continue;
					if (result.ReceivedBytes > _settings.MaxFrameBytes)
					{
						_logger.LogDebug(
							"UDP tracking datagram exceeded the configured frame limit for {ProtocolKey} from {RemoteEndpoint}.",
							Definition.ProtocolKey,
							TrackingEndpointMasker.Mask(result.RemoteEndPoint));
						_metrics.RecordParseFailure(
							Definition.ProtocolKey,
							"frame-too-large");
						continue;
					}

					var remoteAddress =
						(result.RemoteEndPoint as IPEndPoint)?.Address;
					if (!_connectionAdmission.TryAcquire(
						    remoteAddress,
						    out var admissionLease))
					{
						_logger.LogDebug(
							"UDP tracking datagram rejected by admission limits from {RemoteEndpoint}.",
							TrackingEndpointMasker.Mask(result.RemoteEndPoint));
						_metrics.ConnectionRejected(Definition);
						continue;
					}

					var datagram = receiveBuffer
						.AsMemory(0, result.ReceivedBytes)
						.ToArray();
					var datagramId =
						Interlocked.Increment(ref _datagramSequence);
					var activeDatagram = new ActiveDatagram();
					_activeDatagrams.TryAdd(datagramId, activeDatagram);
					_metrics.ConnectionStarted(Definition);
					_ = ProcessDatagramAsync(
						datagramId,
						activeDatagram,
						admissionLease,
						datagram,
						result.RemoteEndPoint,
						_sessionCancellation.Token);
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(
					receiveBuffer,
					clearArray: true);
			}
		}

		private async Task ProcessDatagramAsync(
			long datagramId,
			ActiveDatagram activeDatagram,
			TrackingConnectionLease admissionLease,
			ReadOnlyMemory<byte> datagram,
			EndPoint remoteEndPoint,
			CancellationToken cancellationToken)
		{
			var connectionOutcome = "completed";
			try
			{
				var response = await _sessionHandler.HandleUdpAsync(
					Definition,
					datagram,
					remoteEndPoint,
					cancellationToken);
				if (!response.IsEmpty)
				{
					if (response.Length > _settings.MaxFrameBytes)
					{
						_logger.LogWarning(
							"UDP tracking response exceeded the configured frame limit for {ProtocolKey}; response was dropped.",
							Definition.ProtocolKey);
					}
					else
					{
						await _listenerSocket.SendToAsync(
							response,
							SocketFlags.None,
							remoteEndPoint,
							cancellationToken);
					}
				}
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				connectionOutcome = "cancelled";
			}
			catch (Exception ex)
			{
				connectionOutcome = "failed";
				_logger.LogWarning(
					ex,
					"UDP tracking datagram failed for {ProtocolKey} from {RemoteEndpoint}.",
					Definition.ProtocolKey,
					TrackingEndpointMasker.Mask(remoteEndPoint));
			}
			finally
			{
				admissionLease.Dispose();
				_activeDatagrams.TryRemove(datagramId, out _);
				_metrics.ConnectionCompleted(
					Definition,
					connectionOutcome);
				activeDatagram.CompletionSource.TrySetResult();
			}
		}

		private IReadOnlyCollection<ActiveDatagram>
			GetActiveDatagramSnapshot()
		{
			return _activeDatagrams.Values.ToList();
		}

		private sealed class ActiveDatagram
		{
			public ActiveDatagram()
			{
				CompletionSource = new TaskCompletionSource(
					TaskCreationOptions.RunContinuationsAsynchronously);
			}

			public TaskCompletionSource CompletionSource { get; }
			public Task Completion => CompletionSource.Task;
		}
	}
}
