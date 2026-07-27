using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
	public sealed class TcpTrackingListener : ITrackingListener
	{
		private readonly TrackingGatewaySettings _settings;
		private readonly TrackingConnectionAdmission _connectionAdmission;
		private readonly ITrackingTransportSessionHandler _sessionHandler;
		private readonly TrackingGatewayMetrics _metrics;
		private readonly ILogger<TcpTrackingListener> _logger;
		private readonly ConcurrentDictionary<long, ActiveConnection>
			_activeConnections =
				new ConcurrentDictionary<long, ActiveConnection>();
		private CancellationTokenSource _acceptCancellation;
		private CancellationTokenSource _sessionCancellation;
		private Socket _listenerSocket;
		private Task _completion = Task.CompletedTask;
		private long _connectionSequence;
		private int _isBound;
		private int _started;
		private int _stopping;

		public TcpTrackingListener(
			TrackingListenerDefinition definition,
			TrackingGatewaySettings settings,
			TrackingConnectionAdmission connectionAdmission,
			ITrackingTransportSessionHandler sessionHandler,
			TrackingGatewayMetrics metrics,
			ILogger<TcpTrackingListener> logger)
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
				throw new InvalidOperationException("The TCP listener has already started.");

			_acceptCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_sessionCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			var listenerSocket = new Socket(
				AddressFamily.InterNetworkV6,
				SocketType.Stream,
				ProtocolType.Tcp)
			{
				DualMode = true
			};

			try
			{
				listenerSocket.Bind(
					new IPEndPoint(IPAddress.IPv6Any, Definition.Port));
				listenerSocket.Listen(
					Math.Min(_settings.MaxConnections, 512));
				_listenerSocket = listenerSocket;
				Volatile.Write(ref _isBound, 1);
				_completion = AcceptLoopAsync(
					listenerSocket,
					_acceptCancellation.Token);
				return Task.CompletedTask;
			}
			catch
			{
				listenerSocket.Dispose();
				_acceptCancellation.Dispose();
				_sessionCancellation.Dispose();
				throw;
			}
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			if (Interlocked.Exchange(ref _stopping, 1) != 0)
				return;

			_acceptCancellation?.Cancel();
			_listenerSocket?.Dispose();
			Volatile.Write(ref _isBound, 0);

			try
			{
				await _completion;
			}
			catch (OperationCanceledException)
				when (_acceptCancellation?.IsCancellationRequested == true)
			{
			}

			var activeConnections = GetActiveConnectionSnapshot();
			var drainTask = Task.WhenAll(
				activeConnections.Select(connection => connection.Completion));
			try
			{
				await drainTask.WaitAsync(cancellationToken);
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				_logger.LogWarning(
					"TCP listener shutdown timed out for {ProtocolKey} on port {Port}; closing {ConnectionCount} active sessions.",
					Definition.ProtocolKey,
					Definition.Port,
					activeConnections.Count);
				_metrics.RecordForcedShutdown(Definition);
				_sessionCancellation?.Cancel();
				foreach (var connection in activeConnections)
					connection.Socket.Dispose();
			}
			finally
			{
				_sessionCancellation?.Cancel();
				_listenerSocket?.Dispose();
				Volatile.Write(ref _isBound, 0);
			}
		}

		private async Task AcceptLoopAsync(
			Socket listenerSocket,
			CancellationToken cancellationToken)
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				Socket connectionSocket;
				try
				{
					connectionSocket =
						await listenerSocket.AcceptAsync(cancellationToken);
				}
				catch (OperationCanceledException)
					when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (ObjectDisposedException)
					when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (SocketException)
					when (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				connectionSocket.NoDelay = true;
				var remoteEndPoint = connectionSocket.RemoteEndPoint;
				var remoteAddress = (remoteEndPoint as IPEndPoint)?.Address;
				if (!_connectionAdmission.TryAcquire(
					    remoteAddress,
					    out var admissionLease))
				{
					_logger.LogDebug(
						"TCP tracking connection rejected by admission limits from {RemoteEndpoint}.",
						TrackingEndpointMasker.Mask(remoteEndPoint));
					_metrics.ConnectionRejected(Definition);
					connectionSocket.Dispose();
					continue;
				}

				var connectionId =
					Interlocked.Increment(ref _connectionSequence);
				var activeConnection =
					new ActiveConnection(connectionSocket);
				_activeConnections.TryAdd(connectionId, activeConnection);
				_metrics.ConnectionStarted(Definition);
				_ = ProcessConnectionAsync(
					connectionId,
					activeConnection,
					admissionLease,
					remoteEndPoint,
					_sessionCancellation.Token);
			}
		}

		private async Task ProcessConnectionAsync(
			long connectionId,
			ActiveConnection activeConnection,
			TrackingConnectionLease admissionLease,
			EndPoint remoteEndPoint,
			CancellationToken cancellationToken)
		{
			var startedTimestamp = Stopwatch.GetTimestamp();
			var connectionOutcome = "completed";
			try
			{
				using var stream = new NetworkStream(
					activeConnection.Socket,
					ownsSocket: false);
				await _sessionHandler.HandleTcpAsync(
					Definition,
					stream,
					remoteEndPoint,
					cancellationToken);
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
					"TCP tracking session failed for {ProtocolKey} from {RemoteEndpoint}.",
					Definition.ProtocolKey,
					TrackingEndpointMasker.Mask(remoteEndPoint));
			}
			finally
			{
				activeConnection.Socket.Dispose();
				admissionLease.Dispose();
				_activeConnections.TryRemove(connectionId, out _);
				_metrics.ConnectionCompleted(
					Definition,
					connectionOutcome);
				_metrics.ObserveSessionDuration(
					Definition.ProtocolKey,
					Stopwatch.GetElapsedTime(startedTimestamp));
				activeConnection.CompletionSource.TrySetResult();
			}
		}

		private IReadOnlyCollection<ActiveConnection>
			GetActiveConnectionSnapshot()
		{
			return _activeConnections.Values.ToList();
		}

		private sealed class ActiveConnection
		{
			public ActiveConnection(Socket socket)
			{
				Socket = socket;
				CompletionSource = new TaskCompletionSource(
					TaskCreationOptions.RunContinuationsAsynchronously);
			}

			public Socket Socket { get; }
			public TaskCompletionSource CompletionSource { get; }
			public Task Completion => CompletionSource.Task;
		}
	}
}
