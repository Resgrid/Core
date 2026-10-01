using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The open SignalR connections of one host, by the session each belongs to (passkey workbook section 12, slice 16).
	/// Hub filters register and remove connections; the host's sweep closes every connection whose session ended, locked
	/// or passed its idle deadline, so a connection that never invokes anything stops receiving broadcasts too.
	/// </summary>
	public sealed class SessionConnectionRegistry
	{
		private readonly ConcurrentDictionary<string, (string SessionId, Action Abort)> _connections = new(StringComparer.Ordinal);

		public int Count => _connections.Count;

		public void Register(string connectionId, string sessionId, Action abort)
		{
			if (!string.IsNullOrWhiteSpace(connectionId) && !string.IsNullOrWhiteSpace(sessionId) && abort != null)
				_connections[connectionId] = (sessionId, abort);
		}

		public void Unregister(string connectionId)
		{
			if (!string.IsNullOrWhiteSpace(connectionId))
				_connections.TryRemove(connectionId, out _);
		}

		/// <summary>Closes this host's connections of one session now. Returns how many were closed.</summary>
		public int CloseSession(string sessionId)
		{
			if (string.IsNullOrWhiteSpace(sessionId))
				return 0;

			return _connections.Where(c => string.Equals(c.Value.SessionId, sessionId, StringComparison.Ordinal)).ToArray().Count(Close);
		}

		/// <summary>Checks every distinct session behind an open connection, in one batched read, and closes the unusable ones.</summary>
		public async Task<int> SweepAsync(IUserSessionService sessions, CancellationToken cancellationToken = default)
		{
			var snapshot = _connections.ToArray();
			if (snapshot.Length == 0)
				return 0;

			var unusable = await sessions.GetUnusableSessionIdsAsync(snapshot.Select(c => c.Value.SessionId).Distinct(StringComparer.Ordinal).ToList(),
				cancellationToken);
			return snapshot.Where(c => unusable.Contains(c.Value.SessionId)).Count(Close);
		}

		private bool Close(System.Collections.Generic.KeyValuePair<string, (string SessionId, Action Abort)> connection)
		{
			if (!_connections.TryRemove(connection.Key, out _))
				return false;

			try
			{
				connection.Value.Abort();
			}
			catch (Exception ex)
			{
				// A connection that is already going away; nothing else to do for it.
				Logging.LogException(ex, "A SignalR connection could not be closed.");
			}

			return true;
		}
	}
}
