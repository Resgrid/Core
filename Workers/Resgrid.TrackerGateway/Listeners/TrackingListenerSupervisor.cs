using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resgrid.TrackerGateway.Health;
using Resgrid.TrackerGateway.Hosting;

namespace Resgrid.TrackerGateway.Listeners
{
	public sealed class TrackingListenerSupervisor : BackgroundService
	{
		private readonly TrackingListenerPlan _plan;
		private readonly ITrackingListenerFactory _listenerFactory;
		private readonly TrackingGatewaySettings _settings;
		private readonly TrackingGatewayReadinessState _readiness;
		private readonly ILogger<TrackingListenerSupervisor> _logger;
		private readonly object _listenerSyncRoot = new object();
		private readonly List<ITrackingListener> _listeners =
			new List<ITrackingListener>();
		private int _stopping;

		public TrackingListenerSupervisor(
			TrackingListenerPlan plan,
			ITrackingListenerFactory listenerFactory,
			TrackingGatewaySettings settings,
			TrackingGatewayReadinessState readiness,
			ILogger<TrackingListenerSupervisor> logger)
		{
			_plan = plan;
			_listenerFactory = listenerFactory;
			_settings = settings;
			_readiness = readiness;
			_logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_readiness.Initialize(_plan);
			if (_plan.Listeners.Count == 0)
			{
				_logger.LogInformation(
					"Tracker gateway is running without native socket listeners.");
				return;
			}

			try
			{
				var listeners = _plan.Listeners
					.Select(_listenerFactory.Create)
					.ToList();
				lock (_listenerSyncRoot)
				{
					_listeners.AddRange(listeners);
				}

				foreach (var listener in listeners)
				{
					await listener.StartAsync(stoppingToken);
					if (!listener.IsBound)
					{
						throw new InvalidOperationException(
							$"Tracking listener did not report a bound state after startup: {listener.Definition}.");
					}

					_readiness.MarkBound(listener.Definition);
					_logger.LogInformation(
						"Tracking listener bound for {ProtocolKey} over {Transport} on port {Port}.",
						listener.Definition.ProtocolKey,
						listener.Definition.Transport,
						listener.Definition.Port);
				}

				await Task.WhenAll(listeners.Select(listener => listener.Completion));
				if (Volatile.Read(ref _stopping) == 0 &&
				    !stoppingToken.IsCancellationRequested)
				{
					throw new InvalidOperationException(
						"A tracking listener stopped unexpectedly.");
				}
			}
			catch (OperationCanceledException)
				when (stoppingToken.IsCancellationRequested ||
				      Volatile.Read(ref _stopping) != 0)
			{
			}
			catch (Exception ex)
			{
				_readiness.MarkFailed();
				_logger.LogError(ex, "Tracker gateway listener supervisor failed.");
				throw;
			}
			finally
			{
				foreach (var listener in GetListenerSnapshot())
					_readiness.MarkStopped(listener.Definition);
			}
		}

		public override async Task StopAsync(CancellationToken cancellationToken)
		{
			Interlocked.Exchange(ref _stopping, 1);
			_readiness.MarkStopping();

			using var shutdownCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			shutdownCancellation.CancelAfter(
				TimeSpan.FromSeconds(_settings.GracefulShutdownSeconds));

			try
			{
				var listeners = GetListenerSnapshot();
				for (var index = listeners.Count - 1; index >= 0; index--)
				{
					try
					{
						await listeners[index].StopAsync(
							shutdownCancellation.Token);
						_readiness.MarkStopped(
							listeners[index].Definition);
					}
					catch (Exception ex)
					{
						_logger.LogError(
							ex,
							"Tracking listener failed to stop cleanly for {ProtocolKey} over {Transport} on port {Port}; continuing shutdown.",
							listeners[index].Definition.ProtocolKey,
							listeners[index].Definition.Transport,
							listeners[index].Definition.Port);
					}
				}
			}
			finally
			{
				await base.StopAsync(shutdownCancellation.Token);
			}
		}

		private IReadOnlyList<ITrackingListener> GetListenerSnapshot()
		{
			lock (_listenerSyncRoot)
			{
				return _listeners.ToList();
			}
		}
	}
}
