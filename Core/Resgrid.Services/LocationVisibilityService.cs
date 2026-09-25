using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Realtime counterpart of the location visibility matrix checks in AuthorizationService. Location pings
	/// arrive far too often to test every viewer per ping, so each department's matrix is reduced to its
	/// distinct viewer lists ("visibility sets"; a department has roughly one per station group): a ping is
	/// addressed to one set, and a viewer subscribes to every set that lists them.
	///
	/// The fail-open rules match the matrix checks: no matrix, an unrestricted matrix, or an entity the
	/// matrix does not list yet (it predates the entity; a rebuild is requested) means everyone in the
	/// department may see the location.
	/// </summary>
	public class LocationVisibilityService : ILocationVisibilityService
	{
		// Must match AuthorizationService and SecurityLogic, which build and read the same entries.
		private const string WhoCanViewUnitLocationsCacheKey = "ViewUnitLocationsSecurityMaxtix_{0}";
		private const string WhoCanViewPersonnelLocationsCacheKey = "ViewUserLocationsSecurityMaxtix_{0}";

		/// <summary>
		/// How long a department's reduced matrix is reused. A permission change reaches realtime
		/// subscribers within this window (plus the Eventing membership sync interval).
		/// </summary>
		public static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(30);

		private static readonly TimeSpan MatrixRefreshDebounce = TimeSpan.FromMinutes(2);
		private static readonly TimeSpan SnapshotEviction = TimeSpan.FromMinutes(10);

		private readonly ICacheProvider _cacheProvider;
		private readonly IEventAggregator _eventAggregator;
		private readonly TimeProvider _timeProvider;

		private readonly ConcurrentDictionary<(int DepartmentId, SecurityCacheTypes Type), CachedSnapshot> _snapshots =
			new ConcurrentDictionary<(int DepartmentId, SecurityCacheTypes Type), CachedSnapshot>();

		private readonly ConcurrentDictionary<(int DepartmentId, SecurityCacheTypes Type), DateTime> _lastRefreshRequests =
			new ConcurrentDictionary<(int DepartmentId, SecurityCacheTypes Type), DateTime>();

		private DateTime _lastEviction = DateTime.MinValue;

		public LocationVisibilityService(ICacheProvider cacheProvider, IEventAggregator eventAggregator, TimeProvider clock = null)
		{
			_cacheProvider = cacheProvider;
			_eventAggregator = eventAggregator;
			_timeProvider = clock ?? TimeProvider.System;
		}

		public async Task<LocationAudience> GetUnitLocationAudienceAsync(int departmentId, int unitId)
		{
			var snapshot = await GetSnapshotAsync(departmentId, SecurityCacheTypes.WhoCanViewUnitLocations);

			return GetAudience(snapshot, departmentId, SecurityCacheTypes.WhoCanViewUnitLocations, unitId.ToString());
		}

		public async Task<LocationAudience> GetPersonnelLocationAudienceAsync(int departmentId, string userId)
		{
			var snapshot = await GetSnapshotAsync(departmentId, SecurityCacheTypes.WhoCanViewPersonnelLocations);

			return GetAudience(snapshot, departmentId, SecurityCacheTypes.WhoCanViewPersonnelLocations, NormalizeUserId(userId));
		}

		public async Task<IReadOnlyCollection<string>> GetVisibilitySetKeysForViewerAsync(int departmentId, string viewerUserId)
		{
			var viewer = NormalizeUserId(viewerUserId);

			if (viewer == null)
				return Array.Empty<string>();

			var unitSnapshot = await GetSnapshotAsync(departmentId, SecurityCacheTypes.WhoCanViewUnitLocations);
			var personnelSnapshot = await GetSnapshotAsync(departmentId, SecurityCacheTypes.WhoCanViewPersonnelLocations);

			return unitSnapshot.GetSetKeysForViewer(viewer)
				.Concat(personnelSnapshot.GetSetKeysForViewer(viewer))
				.Distinct(StringComparer.Ordinal)
				.ToList();
		}

		public async Task<bool> CanViewUnitLocationAsync(int departmentId, int unitId, string viewerUserId)
		{
			var snapshot = await GetSnapshotAsync(departmentId, SecurityCacheTypes.WhoCanViewUnitLocations);
			var audience = GetAudience(snapshot, departmentId, SecurityCacheTypes.WhoCanViewUnitLocations, unitId.ToString());

			return audience.IsEntireDepartment || snapshot.IsViewerInSet(audience.VisibilitySetKey, NormalizeUserId(viewerUserId));
		}

		public async Task<bool> CanViewPersonnelLocationAsync(int departmentId, string userId, string viewerUserId)
		{
			var subject = NormalizeUserId(userId);
			var viewer = NormalizeUserId(viewerUserId);

			// Everyone may see their own location (AuthorizationService: userToView == userId).
			if (subject != null && subject == viewer)
				return true;

			var snapshot = await GetSnapshotAsync(departmentId, SecurityCacheTypes.WhoCanViewPersonnelLocations);
			var audience = GetAudience(snapshot, departmentId, SecurityCacheTypes.WhoCanViewPersonnelLocations, subject);

			return audience.IsEntireDepartment || snapshot.IsViewerInSet(audience.VisibilitySetKey, viewer);
		}

		private LocationAudience GetAudience(VisibilitySnapshot snapshot, int departmentId, SecurityCacheTypes type, string entityKey)
		{
			if (snapshot.IsUnrestricted)
				return LocationAudience.EntireDepartment;

			if (entityKey == null)
				return LocationAudience.EntireDepartment;

			if (!snapshot.TryGetSetKey(entityKey, out var setKey))
			{
				// The matrix predates this unit/person; same answer and self-heal as AuthorizationService.
				RequestMatrixRefresh(departmentId, type);
				return LocationAudience.EntireDepartment;
			}

			return LocationAudience.ForVisibilitySet(setKey);
		}

		private async Task<VisibilitySnapshot> GetSnapshotAsync(int departmentId, SecurityCacheTypes type)
		{
			var now = _timeProvider.GetUtcNow().UtcDateTime;
			var key = (departmentId, type);

			EvictStaleSnapshots(now);

			if (_snapshots.TryGetValue(key, out var cached) && now - cached.LoadedOn < SnapshotLifetime)
				return await cached.Snapshot.Value;

			// Single flight: concurrent callers share whichever load won the swap.
			var loading = new CachedSnapshot(now, new Lazy<Task<VisibilitySnapshot>>(() => LoadSnapshotAsync(departmentId, type)));
			var current = _snapshots.AddOrUpdate(key, loading,
				(_, existing) => now - existing.LoadedOn < SnapshotLifetime ? existing : loading);

			return await current.Snapshot.Value;
		}

		private async Task<VisibilitySnapshot> LoadSnapshotAsync(int departmentId, SecurityCacheTypes type)
		{
			try
			{
				if (type == SecurityCacheTypes.WhoCanViewUnitLocations)
				{
					var matrix = await _cacheProvider.GetAsync<VisibilityPayloadUnits>(string.Format(WhoCanViewUnitLocationsCacheKey, departmentId));

					// Fail open when the matrix is missing, as the REST checks do.
					if (matrix == null || matrix.EveryoneNoGroupLock)
						return VisibilitySnapshot.Unrestricted;

					return VisibilitySnapshot.Build(matrix.Units?.Select(x => new KeyValuePair<string, List<string>>(x.Key.ToString(), x.Value)));
				}

				var usersMatrix = await _cacheProvider.GetAsync<VisibilityPayloadUsers>(string.Format(WhoCanViewPersonnelLocationsCacheKey, departmentId));

				if (usersMatrix == null || usersMatrix.EveryoneNoGroupLock)
					return VisibilitySnapshot.Unrestricted;

				return VisibilitySnapshot.Build(usersMatrix.Users?
					.Where(x => NormalizeUserId(x.Key) != null)
					.Select(x => new KeyValuePair<string, List<string>>(NormalizeUserId(x.Key), x.Value)));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Unable to read the {type} visibility matrix for department {departmentId}; realtime locations fall back to the department.");
				return VisibilitySnapshot.Unrestricted;
			}
		}

		private void RequestMatrixRefresh(int departmentId, SecurityCacheTypes type)
		{
			var now = _timeProvider.GetUtcNow().UtcDateTime;
			var shouldSend = false;

			_lastRefreshRequests.AddOrUpdate((departmentId, type), _ =>
			{
				shouldSend = true;
				return now;
			}, (_, last) =>
			{
				if (now - last < MatrixRefreshDebounce)
					return last;

				shouldSend = true;
				return now;
			});

			if (shouldSend)
				_eventAggregator?.SendMessage(new SecurityRefreshEvent { DepartmentId = departmentId, Type = type });
		}

		private void EvictStaleSnapshots(DateTime now)
		{
			if (now - _lastEviction < SnapshotEviction)
				return;

			_lastEviction = now;

			foreach (var entry in _snapshots)
			{
				if (now - entry.Value.LoadedOn >= SnapshotEviction)
					_snapshots.TryRemove(entry);
			}
		}

		private static string NormalizeUserId(string userId)
		{
			return string.IsNullOrWhiteSpace(userId) ? null : userId.Trim().ToLowerInvariant();
		}

		private sealed class CachedSnapshot
		{
			public CachedSnapshot(DateTime loadedOn, Lazy<Task<VisibilitySnapshot>> snapshot)
			{
				LoadedOn = loadedOn;
				Snapshot = snapshot;
			}

			public DateTime LoadedOn { get; }
			public Lazy<Task<VisibilitySnapshot>> Snapshot { get; }
		}

		private sealed class VisibilitySnapshot
		{
			public static readonly VisibilitySnapshot Unrestricted = new VisibilitySnapshot(true,
				new Dictionary<string, string>(), new Dictionary<string, HashSet<string>>(), new Dictionary<string, List<string>>());

			private readonly Dictionary<string, string> _setKeyByEntity;
			private readonly Dictionary<string, HashSet<string>> _viewersBySetKey;
			private readonly Dictionary<string, List<string>> _setKeysByViewer;

			private VisibilitySnapshot(bool isUnrestricted, Dictionary<string, string> setKeyByEntity,
				Dictionary<string, HashSet<string>> viewersBySetKey, Dictionary<string, List<string>> setKeysByViewer)
			{
				IsUnrestricted = isUnrestricted;
				_setKeyByEntity = setKeyByEntity;
				_viewersBySetKey = viewersBySetKey;
				_setKeysByViewer = setKeysByViewer;
			}

			public bool IsUnrestricted { get; }

			public static VisibilitySnapshot Build(IEnumerable<KeyValuePair<string, List<string>>> viewersByEntity)
			{
				var setKeyByEntity = new Dictionary<string, string>(StringComparer.Ordinal);
				var viewersBySetKey = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

				foreach (var entry in viewersByEntity ?? Enumerable.Empty<KeyValuePair<string, List<string>>>())
				{
					var viewers = (entry.Value ?? new List<string>())
						.Select(NormalizeUserId)
						.Where(x => x != null)
						.Distinct(StringComparer.Ordinal)
						.OrderBy(x => x, StringComparer.Ordinal)
						.ToList();
					var setKey = GetSetKey(viewers);

					setKeyByEntity[entry.Key] = setKey;

					if (!viewersBySetKey.ContainsKey(setKey))
						viewersBySetKey[setKey] = new HashSet<string>(viewers, StringComparer.Ordinal);
				}

				var setKeysByViewer = new Dictionary<string, List<string>>(StringComparer.Ordinal);

				foreach (var set in viewersBySetKey)
				{
					foreach (var viewer in set.Value)
					{
						if (!setKeysByViewer.TryGetValue(viewer, out var setKeys))
							setKeysByViewer[viewer] = setKeys = new List<string>();

						setKeys.Add(set.Key);
					}
				}

				return new VisibilitySnapshot(false, setKeyByEntity, viewersBySetKey, setKeysByViewer);
			}

			public bool TryGetSetKey(string entityKey, out string setKey) => _setKeyByEntity.TryGetValue(entityKey, out setKey);

			public bool IsViewerInSet(string setKey, string viewer) =>
				viewer != null && setKey != null && _viewersBySetKey.TryGetValue(setKey, out var viewers) && viewers.Contains(viewer);

			public IEnumerable<string> GetSetKeysForViewer(string viewer) =>
				_setKeysByViewer.TryGetValue(viewer, out var setKeys) ? setKeys : Enumerable.Empty<string>();

			/// <summary>
			/// Derived from the viewer list alone, so a matrix rebuild that leaves a list unchanged keeps its
			/// key and existing SignalR group memberships stay valid.
			/// </summary>
			private static string GetSetKey(IReadOnlyCollection<string> sortedViewers)
			{
				var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", sortedViewers)));

				return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
			}
		}
	}
}
