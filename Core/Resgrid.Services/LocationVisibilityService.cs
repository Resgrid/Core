using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Autofac;
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
	/// The fallback rules match the matrix checks: an unrestricted matrix means everyone in the department
	/// may see the location; no matrix (cache off, expired or unreadable), or an entity the matrix does not
	/// list yet (it predates the entity; a rebuild is requested), is answered from the permission rows now
	/// (<see cref="IAuthorizationService.GetLiveUnitVisibilityAsync"/>) rather than sent to everyone. When
	/// that live answer cannot be had, the location goes to nobody beyond its own trackers.
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

		// This service is a singleton and the live answer needs request-scoped services, so each live build runs in its
		// own child scope (as CoreEventService does). Null outside the container: then there is no live answer.
		private readonly ILifetimeScope _lifetimeScope;

		private readonly ConcurrentDictionary<(int DepartmentId, SecurityCacheTypes Type), CachedSnapshot> _snapshots =
			new ConcurrentDictionary<(int DepartmentId, SecurityCacheTypes Type), CachedSnapshot>();

		private readonly ConcurrentDictionary<(int DepartmentId, SecurityCacheTypes Type), DateTime> _lastRefreshRequests =
			new ConcurrentDictionary<(int DepartmentId, SecurityCacheTypes Type), DateTime>();

		private DateTime _lastEviction = DateTime.MinValue;

		public LocationVisibilityService(ICacheProvider cacheProvider, IEventAggregator eventAggregator, TimeProvider clock = null,
			ILifetimeScope lifetimeScope = null)
		{
			_cacheProvider = cacheProvider;
			_eventAggregator = eventAggregator;
			_timeProvider = clock ?? TimeProvider.System;
			_lifetimeScope = lifetimeScope;
		}

		public async Task<LocationAudience> GetUnitLocationAudienceAsync(int departmentId, int unitId)
		{
			var snapshot = await GetSnapshotAsync(departmentId, SecurityCacheTypes.WhoCanViewUnitLocations);

			return (await GetAudienceAsync(snapshot, departmentId, SecurityCacheTypes.WhoCanViewUnitLocations, unitId.ToString())).Audience;
		}

		public async Task<LocationAudience> GetPersonnelLocationAudienceAsync(int departmentId, string userId)
		{
			var snapshot = await GetSnapshotAsync(departmentId, SecurityCacheTypes.WhoCanViewPersonnelLocations);

			return (await GetAudienceAsync(snapshot, departmentId, SecurityCacheTypes.WhoCanViewPersonnelLocations, NormalizeUserId(userId))).Audience;
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
			var (audience, resolved) = await GetAudienceAsync(snapshot, departmentId, SecurityCacheTypes.WhoCanViewUnitLocations, unitId.ToString());

			return audience.IsEntireDepartment || resolved.IsViewerInSet(audience.VisibilitySetKey, NormalizeUserId(viewerUserId));
		}

		public async Task<bool> CanViewPersonnelLocationAsync(int departmentId, string userId, string viewerUserId)
		{
			var subject = NormalizeUserId(userId);
			var viewer = NormalizeUserId(viewerUserId);

			// Everyone may see their own location (AuthorizationService: userToView == userId).
			if (subject != null && subject == viewer)
				return true;

			var snapshot = await GetSnapshotAsync(departmentId, SecurityCacheTypes.WhoCanViewPersonnelLocations);
			var (audience, resolved) = await GetAudienceAsync(snapshot, departmentId, SecurityCacheTypes.WhoCanViewPersonnelLocations, subject);

			return audience.IsEntireDepartment || resolved.IsViewerInSet(audience.VisibilitySetKey, viewer);
		}

		/// <summary>
		/// The audience, and the snapshot it was read from: an entity the matrix does not list completes the snapshot
		/// from the permission rows, and the viewer test has to read the completed one.
		/// </summary>
		private async Task<(LocationAudience Audience, VisibilitySnapshot Snapshot)> GetAudienceAsync(VisibilitySnapshot snapshot, int departmentId,
			SecurityCacheTypes type, string entityKey)
		{
			if (snapshot.IsUnrestricted)
				return (LocationAudience.EntireDepartment, snapshot);

			if (entityKey == null)
				return (LocationAudience.EntireDepartment, snapshot);

			if (!snapshot.TryGetSetKey(entityKey, out var setKey) && !snapshot.IsComplete)
			{
				// The matrix predates this unit/person; same self-heal as AuthorizationService, and the same answer: the
				// permission rows, read now, decide who sees it until the rebuild lands.
				RequestMatrixRefresh(departmentId, type);
				snapshot = await CompleteSnapshotAsync(departmentId, type, snapshot);
				snapshot.TryGetSetKey(entityKey, out setKey);
			}

			if (setKey != null)
				return (LocationAudience.ForVisibilitySet(setKey), snapshot);

			// Listed nowhere, even in the live answer: the department sees it only when the live answer is unrestricted.
			return (snapshot.UnlistedVisibleToDepartment
				? LocationAudience.EntireDepartment
				: LocationAudience.ForVisibilitySet(VisibilitySnapshot.NobodySetKey), snapshot);
		}

		/// <summary>
		/// Fills in, from the live answer, every entity the cached matrix does not list, and keeps the result for the rest
		/// of the snapshot's lifetime. Entities the matrix does list keep its answer, exactly as the AuthorizationService
		/// matrix checks do.
		/// </summary>
		private async Task<VisibilitySnapshot> CompleteSnapshotAsync(int departmentId, SecurityCacheTypes type, VisibilitySnapshot matrixSnapshot)
		{
			var completed = matrixSnapshot.CompleteFrom(await LoadLiveSnapshotAsync(departmentId, type));
			var key = (departmentId, type);

			// Only over the snapshot that was completed: a newer load that won the race in between stays.
			if (_snapshots.TryGetValue(key, out var cached) && cached.Snapshot.IsValueCreated && cached.Snapshot.Value.IsCompletedSuccessfully &&
			    ReferenceEquals(cached.Snapshot.Value.Result, matrixSnapshot))
				_snapshots.TryUpdate(key, new CachedSnapshot(cached.LoadedOn, new Lazy<Task<VisibilitySnapshot>>(() => Task.FromResult(completed))), cached);

			return completed;
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

					if (matrix != null)
						return FromUnitPayload(matrix, isComplete: false);
				}
				else
				{
					var usersMatrix = await _cacheProvider.GetAsync<VisibilityPayloadUsers>(string.Format(WhoCanViewPersonnelLocationsCacheKey, departmentId));

					if (usersMatrix != null)
						return FromPersonnelPayload(usersMatrix, isComplete: false);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Unable to read the {type} visibility matrix for department {departmentId}; realtime locations are answered from the permission rows.");
			}

			// No matrix: the REST checks answer from the permission rows, and so does this.
			return await LoadLiveSnapshotAsync(departmentId, type);
		}

		/// <summary>The matrix as the permission rows give it now. Nobody (beyond the entity's own trackers) when that cannot be read.</summary>
		private async Task<VisibilitySnapshot> LoadLiveSnapshotAsync(int departmentId, SecurityCacheTypes type)
		{
			if (_lifetimeScope == null)
				return VisibilitySnapshot.Nobody;

			try
			{
				using var scope = _lifetimeScope.BeginLifetimeScope();
				var authorization = scope.Resolve<IAuthorizationService>();

				if (type == SecurityCacheTypes.WhoCanViewUnitLocations)
				{
					var units = await authorization.GetLiveUnitVisibilityAsync(departmentId, PermissionTypes.CanSeeUnitLocations);
					return units == null ? VisibilitySnapshot.Nobody : FromUnitPayload(units, isComplete: true);
				}

				var users = await authorization.GetLivePersonnelVisibilityAsync(departmentId, PermissionTypes.CanSeePersonnelLocations);
				return users == null ? VisibilitySnapshot.Nobody : FromPersonnelPayload(users, isComplete: true);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Unable to evaluate {type} location visibility for department {departmentId}; realtime locations go to their trackers only.");
				return VisibilitySnapshot.Nobody;
			}
		}

		private static VisibilitySnapshot FromUnitPayload(VisibilityPayloadUnits payload, bool isComplete)
		{
			if (payload.EveryoneNoGroupLock)
				return VisibilitySnapshot.Unrestricted;

			return VisibilitySnapshot.Build(payload.Units?.Select(x => new KeyValuePair<string, List<string>>(x.Key.ToString(), x.Value)), isComplete);
		}

		private static VisibilitySnapshot FromPersonnelPayload(VisibilityPayloadUsers payload, bool isComplete)
		{
			if (payload.EveryoneNoGroupLock)
				return VisibilitySnapshot.Unrestricted;

			return VisibilitySnapshot.Build(payload.Users?
				.Where(x => NormalizeUserId(x.Key) != null)
				.Select(x => new KeyValuePair<string, List<string>>(NormalizeUserId(x.Key), x.Value)), isComplete);
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
			public static readonly VisibilitySnapshot Unrestricted = new VisibilitySnapshot(true, true, false,
				new Dictionary<string, string>(), new Dictionary<string, HashSet<string>>(), new Dictionary<string, List<string>>());

			/// <summary>A complete answer that lists nobody: used when no live answer can be had.</summary>
			public static readonly VisibilitySnapshot Nobody = Build(null, isComplete: true);

			/// <summary>The set an entity nobody may see is addressed to; no viewer is ever given it.</summary>
			public static readonly string NobodySetKey = GetSetKey(new List<string>());

			private readonly Dictionary<string, string> _setKeyByEntity;
			private readonly Dictionary<string, HashSet<string>> _viewersBySetKey;
			private readonly Dictionary<string, List<string>> _setKeysByViewer;

			private VisibilitySnapshot(bool isUnrestricted, bool isComplete, bool unlistedVisibleToDepartment, Dictionary<string, string> setKeyByEntity,
				Dictionary<string, HashSet<string>> viewersBySetKey, Dictionary<string, List<string>> setKeysByViewer)
			{
				IsUnrestricted = isUnrestricted;
				IsComplete = isComplete;
				UnlistedVisibleToDepartment = unlistedVisibleToDepartment;
				_setKeyByEntity = setKeyByEntity;
				_viewersBySetKey = viewersBySetKey;
				_setKeysByViewer = setKeysByViewer;
			}

			public bool IsUnrestricted { get; }

			/// <summary>
			/// False for a cached matrix, which can predate an entity; true once the live answer has filled in what it
			/// does not list, so an entity still unlisted is answered without another live read.
			/// </summary>
			public bool IsComplete { get; }

			/// <summary>True when the live answer is unrestricted: what the matrix does not list, the department sees.</summary>
			public bool UnlistedVisibleToDepartment { get; }

			/// <summary>This snapshot's entries, then the live snapshot's for every entity this one does not list.</summary>
			public VisibilitySnapshot CompleteFrom(VisibilitySnapshot live)
			{
				var entries = Entries().ToList();
				var listed = new HashSet<string>(_setKeyByEntity.Keys, StringComparer.Ordinal);

				if (live != null && !live.IsUnrestricted)
					entries.AddRange(live.Entries().Where(x => !listed.Contains(x.Key)));

				return Build(entries, isComplete: true, unlistedVisibleToDepartment: live != null && live.IsUnrestricted);
			}

			private IEnumerable<KeyValuePair<string, List<string>>> Entries() =>
				_setKeyByEntity.Select(x => new KeyValuePair<string, List<string>>(x.Key, _viewersBySetKey[x.Value].ToList()));

			public static VisibilitySnapshot Build(IEnumerable<KeyValuePair<string, List<string>>> viewersByEntity, bool isComplete,
				bool unlistedVisibleToDepartment = false)
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

				return new VisibilitySnapshot(false, isComplete, unlistedVisibleToDepartment, setKeyByEntity, viewersBySetKey, setKeysByViewer);
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
