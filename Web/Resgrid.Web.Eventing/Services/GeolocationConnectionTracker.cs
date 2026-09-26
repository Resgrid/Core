using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Resgrid.Web.Eventing.Services
{
	/// <summary>
	/// The geolocation hub connections owned by this Eventing instance and what each one subscribed to.
	/// Location visibility changes when the matrix is rebuilt, so the subscriptions are kept to recompute
	/// the connection's SignalR groups (see <see cref="GeolocationMembership"/>).
	/// </summary>
	public sealed class GeolocationConnectionTracker
	{
		private readonly ConcurrentDictionary<string, GeolocationConnection> _connections =
			new ConcurrentDictionary<string, GeolocationConnection>(StringComparer.Ordinal);

		public GeolocationConnection GetOrAdd(string connectionId, int departmentId, string userId)
		{
			return _connections.GetOrAdd(connectionId, id => new GeolocationConnection(id, departmentId, userId));
		}

		public bool Contains(string connectionId) => _connections.ContainsKey(connectionId);

		public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

		public IReadOnlyCollection<GeolocationConnection> GetAll() => _connections.Values.ToList();
	}

	public sealed class GeolocationConnection
	{
		private readonly object _subscriptionsLock = new object();
		private readonly HashSet<int> _units = new HashSet<int>();
		private readonly HashSet<string> _people = new HashSet<string>(StringComparer.Ordinal);
		private bool _departmentMap;

		public GeolocationConnection(string connectionId, int departmentId, string userId)
		{
			ConnectionId = connectionId;
			DepartmentId = departmentId;
			UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim().ToLowerInvariant();
		}

		public string ConnectionId { get; }
		public int DepartmentId { get; }

		/// <summary>Lower-cased, or null when the connection carries no user id claim.</summary>
		public string UserId { get; }

		/// <summary>Serializes group changes for this connection (hub calls and the periodic sync).</summary>
		internal SemaphoreSlim MembershipGate { get; } = new SemaphoreSlim(1, 1);

		/// <summary>The groups this connection is currently in. Only touched while holding <see cref="MembershipGate"/>.</summary>
		internal HashSet<string> JoinedGroups { get; } = new HashSet<string>(StringComparer.Ordinal);

		public void SubscribeToDepartmentMap()
		{
			lock (_subscriptionsLock)
				_departmentMap = true;
		}

		public void SubscribeToUnit(int unitId)
		{
			lock (_subscriptionsLock)
				_units.Add(unitId);
		}

		public void SubscribeToPerson(string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return;

			lock (_subscriptionsLock)
				_people.Add(userId.Trim().ToLowerInvariant());
		}

		public GeolocationSubscriptions GetSubscriptions()
		{
			lock (_subscriptionsLock)
				return new GeolocationSubscriptions(_departmentMap, _units.ToList(), _people.ToList());
		}
	}

	public sealed class GeolocationSubscriptions
	{
		public GeolocationSubscriptions(bool departmentMap, IReadOnlyCollection<int> units, IReadOnlyCollection<string> people)
		{
			DepartmentMap = departmentMap;
			Units = units;
			People = people;
		}

		public bool DepartmentMap { get; }
		public IReadOnlyCollection<int> Units { get; }
		public IReadOnlyCollection<string> People { get; }
	}
}
