using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Resgrid.TrackerGateway.Hosting;

namespace Resgrid.TrackerGateway.Listeners
{
	public sealed class TrackingConnectionAdmission
	{
		private readonly object _syncRoot = new object();
		private readonly int _maximumConnections;
		private readonly int _maximumConnectionsPerIp;
		private readonly Dictionary<IPAddress, int> _connectionsByAddress =
			new Dictionary<IPAddress, int>();
		private int _connectionCount;

		public TrackingConnectionAdmission(TrackingGatewaySettings settings)
			: this(
				settings?.MaxConnections ??
				throw new ArgumentNullException(nameof(settings)),
				settings.MaxConnectionsPerIp)
		{
		}

		public TrackingConnectionAdmission(
			int maximumConnections,
			int maximumConnectionsPerIp)
		{
			if (maximumConnections <= 0)
				throw new ArgumentOutOfRangeException(nameof(maximumConnections));
			if (maximumConnectionsPerIp <= 0 ||
			    maximumConnectionsPerIp > maximumConnections)
			{
				throw new ArgumentOutOfRangeException(
					nameof(maximumConnectionsPerIp));
			}

			_maximumConnections = maximumConnections;
			_maximumConnectionsPerIp = maximumConnectionsPerIp;
		}

		public int CurrentConnections
		{
			get
			{
				lock (_syncRoot)
				{
					return _connectionCount;
				}
			}
		}

		public bool TryAcquire(
			IPAddress remoteAddress,
			out TrackingConnectionLease lease)
		{
			lease = null;
			if (remoteAddress == null)
				return false;

			var normalizedAddress = Normalize(remoteAddress);
			lock (_syncRoot)
			{
				_connectionsByAddress.TryGetValue(
					normalizedAddress,
					out var addressCount);
				if (_connectionCount >= _maximumConnections ||
				    addressCount >= _maximumConnectionsPerIp)
					return false;

				_connectionCount++;
				_connectionsByAddress[normalizedAddress] = addressCount + 1;
			}

			lease = new TrackingConnectionLease(
				() => Release(normalizedAddress));
			return true;
		}

		private void Release(IPAddress remoteAddress)
		{
			lock (_syncRoot)
			{
				if (!_connectionsByAddress.TryGetValue(
					    remoteAddress,
					    out var addressCount))
					return;

				if (addressCount <= 1)
					_connectionsByAddress.Remove(remoteAddress);
				else
					_connectionsByAddress[remoteAddress] = addressCount - 1;

				if (_connectionCount > 0)
					_connectionCount--;
			}
		}

		private static IPAddress Normalize(IPAddress address)
		{
			return address.IsIPv4MappedToIPv6
				? address.MapToIPv4()
				: address;
		}
	}

	public sealed class TrackingConnectionLease : IDisposable
	{
		private Action _release;

		internal TrackingConnectionLease(Action release)
		{
			_release = release;
		}

		public void Dispose()
		{
			Interlocked.Exchange(ref _release, null)?.Invoke();
		}
	}
}
