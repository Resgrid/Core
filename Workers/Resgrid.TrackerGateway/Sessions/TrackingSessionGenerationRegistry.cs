using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Resgrid.TrackerGateway.Sessions
{
	public sealed class TrackingSessionGenerationRegistry
	{
		private readonly ConcurrentDictionary<string, ActiveSession>
			_activeSessions =
				new ConcurrentDictionary<string, ActiveSession>(
					StringComparer.Ordinal);
		private long _generationSequence;

		public int ActiveCount => _activeSessions.Count;

		public TrackingSessionGenerationLease Activate(
			string deviceId,
			CancellationTokenSource sessionCancellation)
		{
			if (string.IsNullOrWhiteSpace(deviceId))
				throw new ArgumentNullException(nameof(deviceId));
			if (sessionCancellation == null)
				throw new ArgumentNullException(nameof(sessionCancellation));

			var normalizedDeviceId = deviceId.Trim();
			var activeSession = new ActiveSession(
				Interlocked.Increment(ref _generationSequence),
				sessionCancellation);

			while (true)
			{
				if (!_activeSessions.TryGetValue(
					    normalizedDeviceId,
					    out var previous))
				{
					if (_activeSessions.TryAdd(
						    normalizedDeviceId,
						    activeSession))
					{
						return new TrackingSessionGenerationLease(
							this,
							normalizedDeviceId,
							activeSession);
					}

					continue;
				}

				if (!_activeSessions.TryUpdate(
					    normalizedDeviceId,
					    activeSession,
					    previous))
					continue;

				try
				{
					previous.Cancellation.Cancel();
				}
				catch (ObjectDisposedException)
				{
				}

				return new TrackingSessionGenerationLease(
					this,
					normalizedDeviceId,
					activeSession);
			}
		}

		internal bool IsCurrent(
			string deviceId,
			ActiveSession session)
		{
			return _activeSessions.TryGetValue(
				       deviceId,
				       out var current) &&
			       ReferenceEquals(current, session);
		}

		internal void Release(
			string deviceId,
			ActiveSession session)
		{
			((ICollection<KeyValuePair<string, ActiveSession>>)
				_activeSessions).Remove(
				new KeyValuePair<string, ActiveSession>(
					deviceId,
					session));
		}

		internal sealed class ActiveSession
		{
			public ActiveSession(
				long generation,
				CancellationTokenSource cancellation)
			{
				Generation = generation;
				Cancellation = cancellation;
			}

			public long Generation { get; }
			public CancellationTokenSource Cancellation { get; }
		}
	}

	public sealed class TrackingSessionGenerationLease : IDisposable
	{
		private readonly TrackingSessionGenerationRegistry _registry;
		private readonly string _deviceId;
		private readonly TrackingSessionGenerationRegistry.ActiveSession _session;
		private int _disposed;

		internal TrackingSessionGenerationLease(
			TrackingSessionGenerationRegistry registry,
			string deviceId,
			TrackingSessionGenerationRegistry.ActiveSession session)
		{
			_registry = registry;
			_deviceId = deviceId;
			_session = session;
		}

		public long Generation => _session.Generation;

		public bool IsCurrent =>
			Volatile.Read(ref _disposed) == 0 &&
			_registry.IsCurrent(_deviceId, _session);

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) != 0)
				return;

			_registry.Release(_deviceId, _session);
		}
	}
}
